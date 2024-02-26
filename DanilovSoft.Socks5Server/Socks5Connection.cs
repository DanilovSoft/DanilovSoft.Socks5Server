using System.Buffers;
using System.Diagnostics;
using System.Net;
using System.Net.Sockets;

namespace DanilovSoft.Socks5Server;

internal sealed class Socks5Connection : IDisposable
{
    private readonly ManagedTcpSocket _managedTcp;
    private readonly Socks5Listener _listener;

    public Socks5Connection(TcpClient tcp, Socks5Listener listener)
    {
        _managedTcp = new ManagedTcpSocket(tcp.Client);
        _listener = listener;
    }

    /// <summary>
    /// Получает и выполняет один SOCKS5 запрос.
    /// </summary>
    /// <remarks>Не бросает исключения.</remarks>
    public async Task ProcessRequestsAsync()
    {
        var buf = MemoryPool<byte>.Shared.Rent(4096);
        var rentedMemToDispose = buf;
        try
        {
            // В самом начале получаем список коддерживаемых способов аутентификации.
            var socksAuthRequest = await Socks5AuthRequest.ReceiveAsync(_managedTcp, buf.Memory).ConfigureAwait(false);
            if (socksAuthRequest == default)
            {
                return; // Обрыв соединения.
            }

            if (socksAuthRequest.AuthMethods.Contains(Socks5AuthMethod.LoginAndPassword))
            {
                var authResponse = new Socks5AuthResponse(buf.Memory, Socks5AuthMethod.LoginAndPassword);
                var socResult = await _managedTcp.SendAsync(authResponse.BufferSlice).ConfigureAwait(false);
                if (socResult != SocketError.Success)
                {
                    return; // Обрыв соединения.
                }

                var loginPassword = await Socks5LoginPassword.ReceiveAsync(_managedTcp, buf.Memory).ConfigureAwait(false);
                if (loginPassword == default)
                {
                    return; // Обрыв соединения.
                }

                var authResult = new Socks5AuthResult(buf.Memory, allow: true);
                socResult = await _managedTcp.SendAsync(authResult.BufferSlice).ConfigureAwait(false);
                if (socResult != SocketError.Success)
                {
                    return; // Обрыв соединения.
                }
            }
            else
            {
                // Мы поддерживаем только способ без аутентификации.
                if (socksAuthRequest.AuthMethods.Contains(Socks5AuthMethod.NoAuth))
                // Отправляем ответ в котором выбрали способ без аутентификации.
                {
                    var authResponse = new Socks5AuthResponse(buf.Memory, Socks5AuthMethod.NoAuth);
                    var socResult = await _managedTcp.SendAsync(authResponse.BufferSlice).ConfigureAwait(false);
                    if (socResult != SocketError.Success)
                    {
                        return; // Обрав соединения.
                    }
                }
                else
                // Запрос не поддерживает способ без аутентификации.
                {
                    // Не было предложено приемлемого метода.
                    var authResponse = new Socks5AuthResponse(buf.Memory, Socks5AuthMethod.NotSupported);
                    await _managedTcp.SendAsync(authResponse.BufferSlice).ConfigureAwait(false);
                    return; // Закрыть соединение.
                }
            }

            // Читаем запрос клиента.
            var socksRequest = await Socks5Request.ReceiveRequest(_managedTcp, buf.Memory).ConfigureAwait(false);
            if (socksRequest == default)
            {
                return; // Обрыв соединения.
            }

            switch (socksRequest.Command)
            {
                case Socks5Command.ConnectTcp:
                    {
                        var connectionId = Interlocked.Increment(ref _listener._connectionIdSeq);
                        try
                        {
                            // Подключиться к запрошенному адресу через ноду.
                            using (var connectTcpResult = await ConnectAsync(in socksRequest, connectionId).ConfigureAwait(false))
                            {
                                if (connectTcpResult.SocketError == SocketError.Success)
                                // Удалённая нода успешно подключилась к запрошенному адресу.
                                {
                                    Debug.Assert(connectTcpResult.Socket != null);

                                    var ip = ((IPEndPoint)connectTcpResult.Socket.Client.RemoteEndPoint!).Address;

                                    // Отвечаем клиенту по SOKCS что всё ОК.
                                    var response = new Socks5Response(ResponseCode.RequestSuccess, ip);

                                    var socErr = await SendResponseAsync(in response, buf.Memory).ConfigureAwait(false);
                                    if (socErr != SocketError.Success)
                                    {
                                        return; // Обрыв.
                                    }

                                    rentedMemToDispose.Dispose();
                                    rentedMemToDispose = null;

                                    Interlocked.Increment(ref _listener._connectionsCount);

                                    // Не бросает исключения.
                                    await Proxy.RunAsync(connectionId, _managedTcp, connectTcpResult.Socket).ConfigureAwait(false);

                                    LogDisconnect(in socksRequest, connectionId);

                                    Interlocked.Decrement(ref _listener._connectionsCount);
                                }
                                else
                                // Не удалось подключиться к запрошенному адресу.
                                {
                                    await SendConnectionRefusedAndDisconnectAsync(buf.Memory, connectTcpResult.SocketError).ConfigureAwait(false);
                                    return;
                                }
                            }
                        }
                        catch (Exception ex)
                        // Не удалось подключиться к запрошенному адресу.
                        {
                            Debug.WriteLine(ex);
                            await SendConnectionRefusedAndDisconnectAsync(buf.Memory).ConfigureAwait(false);
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
        catch (Exception ex)
        {
            Debug.WriteLine(ex);
            return;
        }
        finally
        {
            rentedMemToDispose?.Dispose();
        }
    }

    private static Task<TcpConnectResult> ConnectAsync(in Socks5Request socksRequest, int connectionId)
    {
        switch (socksRequest.Address)
        {
            case AddressType.IPv4:
            case AddressType.IPv6:
                {
                    Debug.Assert(socksRequest.IPAddress != null);

                    return ConnectByIpAsync(connectionId, new IPEndPoint(socksRequest.IPAddress, socksRequest.Port));
                }
            case AddressType.DomainName:
                {
                    return ConnectByHostAsync(connectionId, new DnsEndPoint(socksRequest.DomainName, socksRequest.Port));
                }
            default:
                throw new ArgumentOutOfRangeException(nameof(socksRequest));
        }
    }

    /// <exception cref="SocketException"/>
    private static async Task<TcpConnectResult> ConnectByIpAsync(int connectionId, IPEndPoint endPoint)
    {
        if (endPoint.IsNotLoopback())
        {
            LogDebug(connectionId, $"ConnectIP {endPoint.Address}:{endPoint.Port}");

            var managedTcp = new ManagedTcpSocket(new Socket(SocketType.Stream, ProtocolType.Tcp));
            try
            {
                var socErr = await managedTcp.ConnectAsync(endPoint).ConfigureAwait(false);

                if (socErr == SocketError.Success)
                {
                    var refCopy = managedTcp;

                    // Предотвратить Dispose.
                    managedTcp = null;

                    return new TcpConnectResult(SocketError.Success, refCopy);
                }
                else
                {
                    return new TcpConnectResult(socErr, null);
                }
            }
            finally
            {
                managedTcp?.Dispose();
            }
        }
        else
        {
            LogDebug(connectionId, "Loopback запрещён");
            return new TcpConnectResult(SocketError.HostUnreachable, null);
        }
    }

    /// <exception cref="SocketException"/>
    private static async Task<TcpConnectResult> ConnectByHostAsync(int connectionId, DnsEndPoint endPoint)
    {
        if (endPoint.IsNotLoopback())
        {
            LogDebug(connectionId, $"ConnectHost {endPoint.Host}:{endPoint.Port}");

            var managedTcp = new ManagedTcpSocket(new Socket(SocketType.Stream, ProtocolType.Tcp));
            try
            {
                var socErr = await managedTcp.ConnectAsync(endPoint).ConfigureAwait(false);
                if (socErr == SocketError.Success)
                {
                    var refCopy = managedTcp;

                    // Предотвратить Dispose.
                    managedTcp = null;

                    return new TcpConnectResult(SocketError.Success, refCopy);
                }
                else
                {
                    return new TcpConnectResult(socErr, null);
                }
            }
            finally
            {
                managedTcp?.Dispose();
            }
        }
        else
        {
            return new TcpConnectResult(SocketError.HostUnreachable, null);
        }
    }

    private async ValueTask SendConnectionRefusedAndDisconnectAsync(Memory<byte> buffer, SocketError socketError)
    {
        var code = socketError switch
        {
            SocketError.TimedOut => ResponseCode.HostUnreachable,
            _ => ResponseCode.HostUnreachable,
        };

        var errResp = new Socks5Response(code, IPAddress.Any);
        var socErr = await SendResponseAsync(in errResp, buffer).ConfigureAwait(false);
        if (socErr == SocketError.Success)
        {
            await WaitForDisconnectAsync(buffer).ConfigureAwait(false);
        }
    }

    private async ValueTask SendConnectionRefusedAndDisconnectAsync(Memory<byte> buffer)
    {
        var errResp = new Socks5Response(ResponseCode.ConnectionRefused, IPAddress.Any);
        var socErr = await SendResponseAsync(in errResp, buffer).ConfigureAwait(false);
        if (socErr == SocketError.Success)
        {
            await WaitForDisconnectAsync(buffer).ConfigureAwait(false);
        }
    }

    private async ValueTask WaitForDisconnectAsync(Memory<byte> buffer)
    {
        using (var cts = new CancellationTokenSource(1000))
        using (cts.Token.Register(s => ((IDisposable)s!).Dispose(), _managedTcp, false))
        {
            try
            {
                await _managedTcp.ReceiveAsync(buffer).ConfigureAwait(false);
            }
            catch { }
        }
    }

    private ValueTask<SocketError> SendResponseAsync(in Socks5Response socksResponse, Memory<byte> buffer)
    {
        var span = socksResponse.Write(buffer);
        return _managedTcp.SendAsync(span);
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
