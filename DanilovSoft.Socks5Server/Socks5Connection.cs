using System.Buffers;
using System.Diagnostics;
using System.Net;
using System.Net.Sockets;

namespace DanilovSoft.Socks5Server;

internal sealed class Socks5Connection(TcpClient tcp, Socks5Listener listener) : IDisposable
{
    private readonly ManagedTcpSocket _managedTcp = new(tcp.Client);

    /// <summary>
    /// Получает и выполняет один SOCKS5 запрос.
    /// </summary>
    public async Task ProcessRequestsAsync(CancellationToken ct = default)
    {
        var buf = ArrayPool<byte>.Shared.Rent(4096);
        //var rentedMemToDispose = buf;
        try
        {
            // В самом начале получаем список коддерживаемых способов аутентификации.
            var socksAuthRequest = await Socks5AuthRequest.ReceiveAsync(_managedTcp, buf, ct).ConfigureAwait(false);
            if (socksAuthRequest == default)
            {
                return; // Обрыв соединения.
            }

            if (socksAuthRequest.AuthMethods?.Contains(Socks5AuthMethod.LoginAndPassword) == true)
            {
                var authResponse = new Socks5AuthResponse(buf, Socks5AuthMethod.LoginAndPassword);
                var socResult = await _managedTcp.SendAsync(authResponse.BufferSlice, ct).ConfigureAwait(false);
                if (socResult != SocketError.Success)
                {
                    return; // Обрыв соединения.
                }

                var loginPassword = await Socks5LoginPassword.ReceiveAsync(_managedTcp, buf).ConfigureAwait(false);
                if (loginPassword == default)
                {
                    return; // Обрыв соединения.
                }

                var authResult = new Socks5AuthResult(buf, allow: true);
                socResult = await _managedTcp.SendAsync(authResult.BufferSlice, ct).ConfigureAwait(false);
                if (socResult != SocketError.Success)
                {
                    return; // Обрыв соединения.
                }
            }
            else if (socksAuthRequest.AuthMethods?.Contains(Socks5AuthMethod.NoAuth) == true) // Мы поддерживаем только способ без аутентификации.
            {
                // Отправляем ответ в котором выбрали способ без аутентификации.
                var authResponse = new Socks5AuthResponse(buf, Socks5AuthMethod.NoAuth);
                var socResult = await _managedTcp.SendAsync(authResponse.BufferSlice, ct).ConfigureAwait(false);
                if (socResult != SocketError.Success)
                {
                    return; // Закрыть соединение.
                }
            }
            else // Не было предложено приемлемого метода.
            {
                Socks5AuthResponse authResponse = new(buf, Socks5AuthMethod.NotSupported);
                await _managedTcp.SendAsync(authResponse.BufferSlice, ct).ConfigureAwait(false);
                return; // Закрыть соединение.
            }

            // Читаем запрос клиента.
            var socksRequest = await Socks5Request.ReceiveRequest(_managedTcp, buf, ct).ConfigureAwait(false);
            if (socksRequest == default)
            {
                return; // Обрыв соединения.
            }

            switch (socksRequest.Command)
            {
                case Socks5Command.ConnectTcp:
                    {
                        var connectionId = Interlocked.Increment(ref listener._connectionIdSeq);
                        try
                        {
                            // Подключиться к запрошенному адресу через ноду.
                            using var connectTcpResult = await ConnectAsync(in socksRequest, connectionId, ct).ConfigureAwait(false);

                            if (connectTcpResult.SocketError != SocketError.Success)
                            {
                                await SendConnectionRefusedAndDisconnectAsync(buf, connectTcpResult.SocketError, ct).ConfigureAwait(false);
                                return;
                            }

                            Debug.Assert(connectTcpResult.Socket != null);

                            var ip = ((IPEndPoint)connectTcpResult.Socket.Client.RemoteEndPoint!).Address;

                            // Отвечаем клиенту по SOKCS что всё ОК.
                            Socks5Response response = new(ResponseCode.RequestSuccess, ip);

                            var socErr = await SendResponseAsync(in response, buf, ct).ConfigureAwait(false);
                            if (socErr != SocketError.Success)
                            {
                                return; // Обрыв.
                            }

                            ArrayPool<byte>.Shared.Return(buf, clearArray: false);
                            buf = null;
                            //rentedMemToDispose.Dispose();
                            //rentedMemToDispose = null;

                            Interlocked.Increment(ref listener._connectionsCount);

                            await Proxy.RunAsync(connectionId, _managedTcp, connectTcpResult.Socket, ct).ConfigureAwait(ConfigureAwaitOptions.SuppressThrowing);

                            LogDisconnect(in socksRequest, connectionId);

                            Interlocked.Decrement(ref listener._connectionsCount);
                        }
                        catch (Exception e) when (!ct.IsCancellationRequested) // Не удалось подключиться к запрошенному адресу.
                        {
                            Debug.WriteLine(e);
                            await SendConnectionRefusedAndDisconnectAsync(buf, ct).ConfigureAwait(false);
                            return;
                        }
                        break;
                    }
                case Socks5Command.BindingTcpPort:
                    ThrowHelper.ThrowNotSupportedException("Binding tcp port not supported yet");
                    break;
                case Socks5Command.AssocUdp:
                    ThrowHelper.ThrowNotSupportedException("UDP association not supported yet");
                    break;
            }
        }
        finally
        {
            if (buf != null)
            {
                ArrayPool<byte>.Shared.Return(buf, clearArray: false);
            }
            //rentedMemToDispose?.Dispose();
        }
    }

    private static Task<TcpConnectResult> ConnectAsync(ref readonly Socks5Request socksRequest, int connectionId, CancellationToken ct)
    {
        switch (socksRequest.Address)
        {
            case AddressType.IPv4:
            case AddressType.IPv6:
                {
                    Debug.Assert(socksRequest.IPAddress != null);

                    return ConnectByIp(connectionId, new IPEndPoint(socksRequest.IPAddress, socksRequest.Port), ct);
                }
            case AddressType.DomainName:
                {
                    return ConnectByHost(connectionId, new DnsEndPoint(socksRequest.DomainName!, socksRequest.Port), ct);
                }
            default:
                return ThrowHelper.ThrowArgumentOutOfRange<Task<TcpConnectResult>>(nameof(socksRequest));
        }
    }

    /// <exception cref="SocketException"/>
    private static async Task<TcpConnectResult> ConnectByIp(int connectionId, IPEndPoint remoteEP, CancellationToken ct)
    {
        if (remoteEP.IsLoopback())
        {
            LogDebug(connectionId, "Loopback запрещён");
            return new TcpConnectResult(SocketError.HostUnreachable, null);
        }

        LogDebug(connectionId, $"ConnectIP {remoteEP.Address}:{remoteEP.Port}");

        var managedTcp = new ManagedTcpSocket(new Socket(SocketType.Stream, ProtocolType.Tcp));
        try
        {
            var socErr = await managedTcp.ConnectAsync(remoteEP, ct).ConfigureAwait(false);

            return socErr == SocketError.Success
                ? new TcpConnectResult(SocketError.Success, Exchange(ref managedTcp, null))
                : new TcpConnectResult(socErr, null);
        }
        finally
        {
            managedTcp?.Dispose();
        }
    }

    /// <exception cref="SocketException"/>
    private static async Task<TcpConnectResult> ConnectByHost(int connectionId, DnsEndPoint endPoint, CancellationToken ct)
    {
        if (endPoint.IsLoopback())
        {
            return new TcpConnectResult(SocketError.HostUnreachable, null);
        }

        LogDebug(connectionId, $"ConnectHost {endPoint.Host}:{endPoint.Port}");

        var managedTcp = new ManagedTcpSocket(new Socket(SocketType.Stream, ProtocolType.Tcp));
        try
        {
            var socErr = await managedTcp.ConnectAsync(endPoint, ct).ConfigureAwait(false);

            return socErr == SocketError.Success
                ? new TcpConnectResult(SocketError.Success, Exchange(ref managedTcp, null))
                : new TcpConnectResult(socErr, null);
        }
        finally
        {
            managedTcp?.Dispose();
        }
    }

    private async ValueTask SendConnectionRefusedAndDisconnectAsync(Memory<byte> buffer, SocketError socketError, CancellationToken ct)
    {
        var code = socketError switch
        {
            SocketError.TimedOut => ResponseCode.HostUnreachable,
            _ => ResponseCode.HostUnreachable,
        };

        Socks5Response errResp = new(code, IPAddress.Any);
        var socErr = await SendResponseAsync(in errResp, buffer, ct).ConfigureAwait(false);
        if (socErr == SocketError.Success)
        {
            await WaitForDisconnectAsync(buffer, timeoutMsec: 2000, ct).ConfigureAwait(false);
        }
    }

    private async ValueTask SendConnectionRefusedAndDisconnectAsync(Memory<byte> buffer, CancellationToken ct)
    {
        Socks5Response errResp = new(ResponseCode.ConnectionRefused, IPAddress.Any);
        var socErr = await SendResponseAsync(in errResp, buffer, ct).ConfigureAwait(false);
        if (socErr == SocketError.Success)
        {
            await WaitForDisconnectAsync(buffer, timeoutMsec: 2000, ct).ConfigureAwait(false);
        }
    }

    private async ValueTask WaitForDisconnectAsync(Memory<byte> buffer, int timeoutMsec, CancellationToken ct)
    {
        using var cts = new CancellationTokenSource(timeoutMsec);
        using var _ = cts.Token.Register(s => ((IDisposable)s!).Dispose(), _managedTcp, useSynchronizationContext: false);

        await ((Task)_managedTcp.ReceiveAsync(buffer, ct).AsTask()).ConfigureAwait(ConfigureAwaitOptions.SuppressThrowing);
    }

    private ValueTask<SocketError> SendResponseAsync(ref readonly Socks5Response socksResponse, Memory<byte> buffer, CancellationToken ct)
    {
        var span = socksResponse.Write(buffer);
        return _managedTcp.SendAsync(span, ct);
    }

    public void Dispose()
    {
        _managedTcp.Dispose();
    }

    [Conditional("DEBUG")]
    private static void LogDebug(int connectionId, string message)
    {
        Trace.WriteLine($"ConId {connectionId} {message}");
    }

    [Conditional("DEBUG")]
    private static void LogDisconnect(in Socks5Request request, int connectionId)
    {
        if (string.IsNullOrEmpty(request.DomainName))
        {
            LogDebug(connectionId, $"Disconnected {request.Address}:{request.Port}");
        }
        else
        {
            LogDebug(connectionId, $"Disconnected {request.DomainName}:{request.Port}");
        }
    }
}
