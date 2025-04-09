using System.Buffers;
using System.Diagnostics;
using System.Net;
using System.Net.Sockets;
using System.Runtime.CompilerServices;
using DanilovSoft.Socks5Server.TcpSocket;

namespace DanilovSoft.Socks5Server;

internal sealed class Socks5Connection(TcpClient connectedSocket) : IDisposable
{
    private readonly ManagedTcpSocket _managedTcp = new(connectedSocket.Client);

    /// <summary>
    /// Получает и выполняет один SOCKS5 запрос.
    /// </summary>
    public async Task ProcessRequestsAsync(CancellationToken ct = default)
    {
        byte[]? buffer = ArrayPool<byte>.Shared.Rent(4096);
        try
        {
            // В самом начале получаем список коддерживаемых способов аутентификации.
            var socksAuthRequest = await Socks5AuthRequest.ReceiveAsync(_managedTcp, buffer, ct);
            if (socksAuthRequest == default)
            {
                return; // Обрыв соединения.
            }

            if (socksAuthRequest.AuthMethods?.Contains(Socks5AuthMethod.LoginAndPassword) == true)
            {
                var authResponse = new Socks5AuthResponse(buffer, Socks5AuthMethod.LoginAndPassword);
                var socResult = await _managedTcp.Send(authResponse.BufferSlice, ct);
                if (socResult != SocketError.Success)
                {
                    return; // Обрыв соединения.
                }

                var loginPassword = await Socks5LoginPassword.ReceiveAsync(_managedTcp, buffer);
                if (loginPassword == default)
                {
                    return; // Обрыв соединения.
                }

                var authResult = new Socks5AuthResult(buffer, allow: true);
                socResult = await _managedTcp.Send(authResult.BufferSlice, ct);
                if (socResult != SocketError.Success)
                {
                    return; // Обрыв соединения.
                }
            }
            else if (socksAuthRequest.AuthMethods?.Contains(Socks5AuthMethod.NoAuth) == true) // Мы поддерживаем только способ без аутентификации.
            {
                // Отправляем ответ в котором выбрали способ без аутентификации.
                var authResponse = new Socks5AuthResponse(buffer, Socks5AuthMethod.NoAuth);
                var socResult = await _managedTcp.Send(authResponse.BufferSlice, ct);
                if (socResult != SocketError.Success)
                {
                    return; // Закрыть соединение.
                }
            }
            else // Не было предложено приемлемого метода.
            {
                Socks5AuthResponse authResponse = new(buffer, Socks5AuthMethod.NotSupported);
                await _managedTcp.Send(authResponse.BufferSlice, ct);
                return; // Закрыть соединение.
            }

            // Читаем запрос клиента.
            var socksRequest = await Socks5Request.ReceiveRequest(_managedTcp, buffer, ct);
            if (socksRequest == default)
            {
                return; // Обрыв соединения.
            }

            switch (socksRequest.Command)
            {
                case Socks5Command.ConnectTcp:
                    {
                        nint connectionId = connectedSocket.Client.Handle;
                        try
                        {
                            // Подключиться к запрошенному адресу через ноду.
                            using var connectTcpResult = await ConnectAsync(in socksRequest, connectionId, ct);

                            if (connectTcpResult.SocketError != SocketError.Success)
                            {
                                await SendConnectionRefusedAndDisconnect(buffer, connectTcpResult.SocketError, ct);
                                return;
                            }

                            Debug.Assert(connectTcpResult.Socket != null);

                            var ip = ((IPEndPoint)connectTcpResult.Socket.Client.RemoteEndPoint!).Address;

                            // Отвечаем клиенту по SOKCS что всё ОК.
                            Socks5Response response = new(ResponseCode.RequestSuccess, ip);

                            var socErr = await SendResponse(in response, buffer, ct);
                            if (socErr != SocketError.Success)
                            {
                                return; // Обрыв.
                            }

                            ArrayPool<byte>.Shared.Return(buffer, clearArray: false);
                            buffer = null;
                            //rentedMemToDispose.Dispose();
                            //rentedMemToDispose = null;

                            //Interlocked.Increment(ref listener._connectionsCount);

                            await Proxy.RunAsync(_managedTcp, connectTcpResult.Socket, ct).ConfigureAwait(ConfigureAwaitOptions.SuppressThrowing);

                            LogDisconnect(in socksRequest, connectionId);

                            //Interlocked.Decrement(ref listener._connectionsCount);
                        }
                        catch (Exception e) when (!ct.IsCancellationRequested) // Не удалось подключиться к запрошенному адресу.
                        {
                            Debug.WriteLine(e);
                            await SendConnectionRefusedAndDisconnect(buffer, ct);
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
            if (buffer != null)
            {
                ArrayPool<byte>.Shared.Return(buffer, clearArray: false);
            }
        }
    }

    private static Task<TcpConnectResult> ConnectAsync(ref readonly Socks5Request socksRequest, nint connectionId, CancellationToken ct)
    {
        switch (socksRequest.Address)
        {
            case AddressType.IPv4:
            case AddressType.IPv6:
                {
                    IPEndPoint remoteEP = new(socksRequest.IPAddress!, socksRequest.Port);

                    if (remoteEP.IsLoopback())
                    {
                        LogDebug(connectionId, "Loopback запрещён");
                        return Task.FromResult(new TcpConnectResult(SocketError.HostUnreachable, null));
                    }

                    LogDebug(connectionId, $"ConnectIP {remoteEP.Address}:{remoteEP.Port}");
                    return Connect(remoteEP, ct);
                }
            case AddressType.DomainName:
                {
                    DnsEndPoint remoteEP = new(socksRequest.DomainName!, socksRequest.Port);

                    if (remoteEP.IsLoopback())
                    {
                        LogDebug(connectionId, "Loopback запрещён");
                        return Task.FromResult(new TcpConnectResult(SocketError.HostUnreachable, null));
                    }

                    LogDebug(connectionId, $"ConnectHost {remoteEP.Host}:{remoteEP.Port}");
                    return Connect(remoteEP, ct);
                }
            default:
                return ThrowHelper.ThrowArgumentOutOfRange<Task<TcpConnectResult>>(nameof(socksRequest));
        }
    }

    private static async Task<TcpConnectResult> Connect(EndPoint remoteEP, CancellationToken ct)
    {
        var managedTcp = CreateManagedSocket();
        try
        {
            var socErr = await managedTcp.Connect(remoteEP, ct);

            return socErr == SocketError.Success
                ? new TcpConnectResult(SocketError.Success, Exchange(ref managedTcp, null))
                : new TcpConnectResult(socErr, null);
        }
        finally
        {
            managedTcp?.Dispose();
        }
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private static ManagedTcpSocket CreateManagedSocket()
    {
        var rawSocket = new Socket(SocketType.Stream, ProtocolType.Tcp);
        try
        {
            return new ManagedTcpSocket(Exchange(ref rawSocket, null));
        }
        finally
        {
            rawSocket?.Dispose();
        }
    }

    private async ValueTask SendConnectionRefusedAndDisconnect(Memory<byte> buffer, SocketError socketError, CancellationToken ct)
    {
        var code = socketError switch
        {
            SocketError.TimedOut => ResponseCode.HostUnreachable,
            _ => ResponseCode.HostUnreachable,
        };

        Socks5Response errResp = new(code, IPAddress.Any);
        var socErr = await SendResponse(in errResp, buffer, ct);
        if (socErr == SocketError.Success)
        {
            await WaitForDisconnect(buffer, timeoutMsec: 2000, ct);
        }
    }

    private async ValueTask SendConnectionRefusedAndDisconnect(Memory<byte> buffer, CancellationToken ct)
    {
        Socks5Response errResp = new(ResponseCode.ConnectionRefused, IPAddress.Any);
        var socErr = await SendResponse(in errResp, buffer, ct);
        if (socErr == SocketError.Success)
        {
            await WaitForDisconnect(buffer, timeoutMsec: 2000, ct);
        }
    }

    private async ValueTask WaitForDisconnect(Memory<byte> buffer, int timeoutMsec, CancellationToken ct)
    {
        using var cts = new CancellationTokenSource(timeoutMsec);
        using var _ = cts.Token.Register(s => ((IDisposable)s!).Dispose(), _managedTcp, useSynchronizationContext: false);

        await ((Task)_managedTcp.Receive(buffer, ct).AsTask()).ConfigureAwait(ConfigureAwaitOptions.SuppressThrowing);
    }

    private ValueTask<SocketError> SendResponse(ref readonly Socks5Response socksResponse, Memory<byte> buffer, CancellationToken ct)
    {
        var span = socksResponse.Write(buffer);
        return _managedTcp.Send(span, ct);
    }

    public void Dispose()
    {
        _managedTcp.Dispose();
    }

    [Conditional("DEBUG")]
    private static void LogDebug(nint connectionId, string message)
    {
        Trace.WriteLine($"ConId {connectionId} {message}");
    }

    [Conditional("DEBUG")]
    private static void LogDisconnect(ref readonly Socks5Request request, nint connectionId)
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
