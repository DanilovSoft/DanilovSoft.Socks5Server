using System.Buffers;
using System.Diagnostics;
using System.Net;
using System.Net.Sockets;
using DanilovSoft.Socks5Server.TcpSocket;

namespace DanilovSoft.Socks5Server;

internal sealed class SocksConnection(Socket connectedSocket) : IDisposable
{
    private static readonly IPEndPoint IpEndPointAny = new(IPAddress.Any, 0);

    /// <summary>
    /// Получает и выполняет один SOCKS5 запрос.
    /// </summary>
    public async Task ProcessConnection(CancellationToken ct = default)
    {
        var pool = ArrayPool<byte>.Shared;
        byte[]? buffer = pool.Rent(4096);
        try
        {
            // В самом начале получаем список коддерживаемых способов аутентификации.
            SocksAuthRequest socksAuthRequest = await connectedSocket.ReceiveAuthRequest(buffer, ct);
            if (socksAuthRequest == default)
            {
                return; // Обрыв соединения.
            }

            if (socksAuthRequest.AuthMethods?.Contains(Socks5AuthMethod.LoginAndPassword) == true)
            {
                Socks5AuthResponse authResponse = new(buffer, Socks5AuthMethod.LoginAndPassword);
                
                if (await connectedSocket.SendAll(authResponse.BufferSlice, ct) != SocketError.Success)
                {
                    return; // Обрыв соединения.
                }

                var loginPassword = await connectedSocket.ReceiveSocks5Login(buffer, ct);
                if (loginPassword == default)
                {
                    return; // Обрыв соединения.
                }

                Socks5AuthResult authResult = new(buffer, allow: true);
                
                if (await connectedSocket.SendAll(authResult.BufferSlice, ct) != SocketError.Success)
                {
                    return; // Обрыв соединения.
                }
            }
            else if (socksAuthRequest.AuthMethods?.Contains(Socks5AuthMethod.NoAuth) == true) // Мы поддерживаем только способ без аутентификации.
            {
                // Отправляем ответ в котором выбрали способ без аутентификации.
                Socks5AuthResponse authResponse = new(buffer, Socks5AuthMethod.NoAuth);
                
                if (await connectedSocket.SendAll(authResponse.BufferSlice, ct) != SocketError.Success)
                {
                    return; // Закрыть соединение.
                }
            }
            else // Не было предложено приемлемого метода.
            {
                Socks5AuthResponse authResponse = new(buffer, Socks5AuthMethod.NotSupported);
                await connectedSocket.SendAll(authResponse.BufferSlice, ct);
                return; // Закрыть соединение.
            }

            // Читаем запрос клиента.
            SocksRequest socksRequest = await connectedSocket.ReceiveSocks5Request(buffer, ct);
            if (socksRequest.IsEmpty)
            {
                return; // Обрыв соединения.
            }

            switch (socksRequest.Command)
            {
                case Socks5Command.Connect:
                    {
                        try
                        {
                            // Подключиться к запрошенному адресу через ноду.
                            using var connectTcpResult = await Connect(in socksRequest, connectedSocket, ct);

                            if (!connectTcpResult.IsConnected)
                            {
                                await SendConnectionRefusedAndDisconnect(buffer, connectTcpResult.SocketError, ct);
                                return;
                            }

                            var ipEndPoint = (IPEndPoint)connectTcpResult.Socket.LocalEndPoint!;

                            // Отвечаем клиенту по SOKCS что успешно подключились к TCP.
                            Socks5Reply response = new(ResponseCode.RequestSuccess, ipEndPoint);

                            if (await SendResponse(in response, buffer, ct) != SocketError.Success)
                            {
                                return; // Обрыв.
                            }

                            pool.Return(buffer, clearArray: false);
                            buffer = null;

                            await Proxy.RunAsync(connectedSocket, connectTcpResult.Socket, ct).ConfigureAwait(ConfigureAwaitOptions.SuppressThrowing);

                            LogDisconnect(in socksRequest, connectedSocket);
                        }
                        catch (Exception e) when (!ct.IsCancellationRequested) // Не удалось подключиться к запрошенному адресу.
                        {
                            Debug.WriteLine(e);
                            await SendConnectionRefusedAndDisconnect(buffer, ct);
                            return;
                        }
                        break;
                    }
                case Socks5Command.Binding:
                    ThrowHelper.ThrowNotSupportedException("Binding tcp port not supported yet");
                    break;
                case Socks5Command.UDPAssociate:
                    ThrowHelper.ThrowNotSupportedException("UDP association not supported yet");
                    break;
            }
        }
        finally
        {
            if (buffer != null)
            {
                pool.Return(buffer, clearArray: false);
            }
        }
    }

    private static Task<TcpConnectResult> Connect(ref readonly SocksRequest socksRequest, Socket connection, CancellationToken ct)
    {
        switch (socksRequest.AddressType)
        {
            case AddressType.IPv4:
            case AddressType.IPv6:
                {
                    IPEndPoint remoteEP = new(socksRequest.DestinationAddress!, socksRequest.DestinationPort);

                    if (remoteEP.IsLoopback())
                    {
                        LogDebug("Loopback запрещён", connection);
                        return Task.FromResult(new TcpConnectResult(SocketError.HostUnreachable, null));
                    }

                    LogDebug($"ConnectIP {remoteEP.Address}:{remoteEP.Port}", connection);
                    return Connect(remoteEP, ct);
                }
            case AddressType.DomainName:
                {
                    DnsEndPoint remoteEP = new(socksRequest.DomainName!, socksRequest.DestinationPort);

                    if (remoteEP.IsLoopback())
                    {
                        LogDebug("Loopback запрещён", connection);
                        return Task.FromResult(new TcpConnectResult(SocketError.HostUnreachable, null));
                    }

                    LogDebug($"ConnectHost {remoteEP.Host}:{remoteEP.Port}", connection);
                    return Connect(remoteEP, ct);
                }
            default:
                return ThrowHelper.ThrowArgumentOutOfRange<Task<TcpConnectResult>>(nameof(socksRequest));
        }
    }

    private static async Task<TcpConnectResult> Connect(EndPoint remoteEP, CancellationToken ct)
    {
        var socket = new Socket(SocketType.Stream, ProtocolType.Tcp);
        try
        {
            var socketError = await socket.Connect2(remoteEP, ct);

            return socketError == SocketError.Success
                ? new TcpConnectResult(SocketError.Success, Exchange(ref socket, null))
                : new TcpConnectResult(socketError, null);
        }
        finally
        {
            socket?.Dispose();
        }
    }

    private async ValueTask SendConnectionRefusedAndDisconnect(Memory<byte> buffer, SocketError socketError, CancellationToken ct)
    {
        var code = socketError switch
        {
            SocketError.TimedOut => ResponseCode.HostUnreachable,
            _ => ResponseCode.HostUnreachable
        };

        Socks5Reply errResp = new(code, IpEndPointAny);
        var socErr = await SendResponse(in errResp, buffer, ct);
        if (socErr == SocketError.Success)
        {
            await WaitForDisconnect(buffer, timeoutMsec: 2000, ct);
        }
    }

    private async ValueTask SendConnectionRefusedAndDisconnect(Memory<byte> buffer, CancellationToken ct)
    {
        Socks5Reply errResp = new(ResponseCode.ConnectionRefused, IpEndPointAny);

        var socketError = await SendResponse(in errResp, buffer, ct);
        if (socketError == SocketError.Success)
        {
            await WaitForDisconnect(buffer, timeoutMsec: 2000, ct);
        }
    }

    private async ValueTask WaitForDisconnect(Memory<byte> buffer, int timeoutMsec, CancellationToken ct)
    {
        using var cts = new CancellationTokenSource(timeoutMsec);
        using var _ = cts.Token.Register(s => ((IDisposable)s!).Dispose(), connectedSocket, useSynchronizationContext: false);

        await ((Task)connectedSocket.Receive2(buffer, ct).AsTask()).ConfigureAwait(ConfigureAwaitOptions.SuppressThrowing);
    }

    private ValueTask<SocketError> SendResponse(ref readonly Socks5Reply socksResponse, Memory<byte> buffer, CancellationToken ct)
    {
        int bytesWritten = socksResponse.WriteTo(buffer.Span);
        
        return connectedSocket.SendAll(buffer[..bytesWritten], ct);
    }

    public void Dispose()
    {
        connectedSocket.Dispose();
    }

    [Conditional("DEBUG")]
    private static void LogDebug(string message, Socket connection)
    {
        Trace.WriteLine($"Con[{connection.Handle}] {message}");
    }

    [Conditional("DEBUG")]
    private static void LogDisconnect(ref readonly SocksRequest request, Socket connection)
    {
        if (string.IsNullOrEmpty(request.DomainName))
        {
            LogDebug($"Disconnected {request.AddressType}:{request.DestinationPort}", connection);
        }
        else
        {
            LogDebug($"Disconnected {request.DomainName}:{request.DestinationPort}", connection);
        }
    }
}
