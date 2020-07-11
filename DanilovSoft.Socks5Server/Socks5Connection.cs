using System;
using System.Buffers;
using System.Diagnostics;
using System.Linq;
using System.Net;
using System.Net.Sockets;
using System.Runtime.CompilerServices;
using System.Text;
using System.Threading;
using System.Threading.Tasks;

namespace DanilovSoft.Socks5Server
{
    internal sealed class Socks5Connection : IDisposable
    {
        private const int Socks5MinSizeRequest = 515;
        private const int ProxyBufferSize = 4096;

        private readonly ManagedTcpSocket _managedTcp;
        //public event EventHandler<string?>? ConnectionOpened;
        //public event EventHandler<string?>? ConnectionClosed;

        public Socks5Connection(TcpClient tcp)
        {
            _managedTcp = new ManagedTcpSocket(tcp.Client);
        }

        /// <summary>
        /// Получает и выполняет один SOCKS5 запрос.
        /// </summary>
        /// <remarks>Не бросает исключения.</remarks>
        public async Task ProcessRequestsAsync()
        {
            Socks5LoginPassword loginPassword = default;
            IMemoryOwner<byte> rentedMem = MemoryPool<byte>.Shared.Rent(ProxyBufferSize);
            IMemoryOwner<byte>? rentedMemToDispose = rentedMem;
            Memory<byte> rentedBuffer = rentedMem.Memory;

            try
            {
                // В самом начале получаем список коддерживаемых способов аутентификации.
                Socks5AuthRequest socksAuthRequest = await Socks5AuthRequest.ReceiveAsync(_managedTcp, rentedBuffer).ConfigureAwait(false);
                if (!socksAuthRequest.IsEmpty)
                {
                    if (socksAuthRequest.AuthMethods.Contains(Socks5AuthMethod.LoginAndPassword))
                    {
                        var authResponse = new Socks5AuthResponse(rentedBuffer, Socks5AuthMethod.LoginAndPassword);
                        SocketError socResult = await _managedTcp.SendAsync(authResponse.BufferSlice).ConfigureAwait(false);
                        if (socResult != SocketError.Success)
                            return; // Обрав соединения.

                        loginPassword = await Socks5LoginPassword.ReceiveAsync(_managedTcp, rentedBuffer).ConfigureAwait(false);
                        if (!loginPassword.IsInitialized)
                            return; // Обрав соединения.

                        var authResult = new Socks5AuthResult(rentedBuffer, allow: true);
                        socResult = await _managedTcp.SendAsync(authResult.BufferSlice).ConfigureAwait(false);
                        if (socResult != SocketError.Success)
                            return; // Обрав соединения.
                    }
                    else
                    {
                        // Мы поддерживаем только способ без аутентификации.
                        if (socksAuthRequest.AuthMethods.Contains(Socks5AuthMethod.NoAuth))
                        // Отправляем ответ в котором выбрали способ без аутентификации.
                        {
                            var authResponse = new Socks5AuthResponse(rentedBuffer, Socks5AuthMethod.NoAuth);
                            SocketError socResult = await _managedTcp.SendAsync(authResponse.BufferSlice).ConfigureAwait(false);
                            if (socResult != SocketError.Success)
                                return; // Обрав соединения.
                        }
                        else
                        // Запрос не поддерживает способ без аутентификации.
                        {
                            // Не было предложено приемлемого метода.
                            var authResponse = new Socks5AuthResponse(rentedBuffer, Socks5AuthMethod.NotSupported);
                            await _managedTcp.SendAsync(authResponse.BufferSlice).ConfigureAwait(false);
                            return; // Закрыть соединение.
                        }
                    }

                    // Читаем запрос клиента.
                    Socks5Request socksRequest = await Socks5Request.ReceiveRequestAsync(_managedTcp, rentedBuffer).ConfigureAwait(false);
                    if (!socksRequest.IsEmpty)
                    {
                        switch (socksRequest.Command)
                        {
                            case Socks5Command.ConnectTcp:
                                {
                                    try
                                    {
                                        // Подключиться к запрошенному адресу через ноду.
                                        TcpConnectResult connectTcpResult = await ConnectAsync(in socksRequest).ConfigureAwait(false);
                                        try
                                        {
                                            if (connectTcpResult.SocketError == SocketError.Success)
                                            // Удалённая нода успешно подключилась к запрошенному адресу.
                                            {
                                                Debug.Assert(connectTcpResult.Socket != null);

                                                var ip = (IPEndPoint)connectTcpResult.Socket.Client.RemoteEndPoint;

                                                // Отвечаем клиенту по SOKCS что всё ОК.
                                                var response = new Socks5Response(ResponseCode.RequestSuccess, ip.Address);
                                                SocketError socErr = await SendResponseAsync(in response, rentedBuffer).ConfigureAwait(false);
                                                if (socErr != SocketError.Success)
                                                    return; // Обрыв.

                                                rentedMemToDispose.Dispose();
                                                rentedMemToDispose = null;

                                                //ConnectionOpened?.Invoke(this, loginPassword.Login);
                                                
                                                await RunProxyAsync(_managedTcp, connectTcpResult.Socket).ConfigureAwait(false);

                                                //ConnectionClosed?.Invoke(this, loginPassword.Login);
                                            }
                                            else
                                            // Не удалось подключиться к запрошенному адресу.
                                            {
                                                await SendConnectionRefusedAndDisconnectAsync(rentedBuffer, connectTcpResult.SocketError).ConfigureAwait(false);
                                                return;
                                            }
                                        }
                                        finally
                                        {
                                            connectTcpResult.Dispose();
                                        }
                                    }
                                    catch (Exception ex)
                                    // Не удалось подключиться к запрошенному адресу.
                                    {
                                        Debug.WriteLine(ex);
                                        await SendConnectionRefusedAndDisconnectAsync(rentedBuffer).ConfigureAwait(false);
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
                    else
                    {
                        return; // Обрав соединения.
                    }
                }
                else
                {
                    return; // Обрыв соединения.
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

        /// <remarks>Не бросает исключения.</remarks>
        private static Task RunProxyAsync(ManagedTcpSocket thisSocket, ManagedTcpSocket remoteSocket)
        {
            var task1 = Task.Run(() => ProxyAsync(thisSocket, remoteSocket));
            var task2 = Task.Run(() => ProxyAsync(remoteSocket, thisSocket));

            return Task.WhenAll(task1, task2);
        }

        private static async Task ProxyAsync(ManagedTcpSocket socketFrom, ManagedTcpSocket socketTo)
        {
            // Арендуем память на длительное время(!).
            // TO TNINK возможно лучше создать свой пул что-бы не истощить общий 
            // в случае когда мы не одни его используем.
            using (var rent = MemoryPool<byte>.Shared.Rent(ProxyBufferSize))
            {
                while (true)
                {
                    SocketReceiveResult result;
                    try
                    {
                        result = await socketFrom.ReceiveAsync(rent.Memory).ConfigureAwait(false);
                    }
                    catch
                    {
                        try
                        {
                            socketTo.Client.Shutdown(SocketShutdown.Send);
                        }
                        catch { }
                        return;
                    }

                    if (result.Count > 0 && result.SocketError == SocketError.Success)
                    {
                        SocketError socketError;
                        try
                        {
                            socketError = await socketTo.SendAsync(rent.Memory.Slice(0, result.Count)).ConfigureAwait(false);
                        }
                        catch
                        {
                            try
                            {
                                // Закрываем приём у противоположного сокета.
                                socketFrom.Client.Shutdown(SocketShutdown.Receive);
                            }
                            catch { }
                            return;
                        }

                        if (socketError == SocketError.Success)
                        {
                            continue;
                        }
                        else
                        // Принимающий сокет закрылся.
                        {
                            try
                            {
                                // Закрываем приём у противоположного сокета.
                                socketFrom.Client.Shutdown(SocketShutdown.Receive);
                            }
                            catch { }
                            return;
                        }
                    }
                    else
                    // Соединение закрылось.
                    {
                        try
                        {
                            socketTo.Client.Shutdown(SocketShutdown.Send);
                        }
                        catch { }
                        return;
                    }
                }
            }
        }

        private static Task<TcpConnectResult> ConnectAsync(in Socks5Request socksRequest)
        {
            switch (socksRequest.Address)
            {
                case AddressType.IPv4:
                case AddressType.IPv6:
                    {
                        Debug.Assert(socksRequest.IPAddress != null);

                        return ConnectByIpAsync(new IPEndPoint(socksRequest.IPAddress, socksRequest.Port));
                    }
                case AddressType.DomainName:
                    {
                        return ConnectByHostAsync(new DnsEndPoint(socksRequest.DomainName, socksRequest.Port));
                    }
                default:
                    throw new ArgumentOutOfRangeException(nameof(socksRequest));
            }
        }

        /// <exception cref="SocketException"/>
        private static async Task<TcpConnectResult> ConnectByIpAsync(IPEndPoint endPoint)
        {
            if (endPoint.IsNotLoopback())
            {
                LogDebug($"ConnectIP {endPoint.Address}:{endPoint.Port}");

                var managedTcp = new ManagedTcpSocket(new Socket(SocketType.Stream, ProtocolType.Tcp));
                try
                {
                    SocketError socErr = await managedTcp.ConnectAsync(endPoint).ConfigureAwait(false);

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
                LogDebug("Loopback запрещён");
                return new TcpConnectResult(SocketError.HostUnreachable, null);
            }
        }

        /// <exception cref="SocketException"/>
        private static async Task<TcpConnectResult> ConnectByHostAsync(DnsEndPoint endPoint)
        {
            if (endPoint.IsNotLoopback())
            {
                LogDebug($"ConnectHost {endPoint.Host}:{endPoint.Port}");

                var managedTcp = new ManagedTcpSocket(new Socket(SocketType.Stream, ProtocolType.Tcp));
                try
                {
                    SocketError socErr = await managedTcp.ConnectAsync(endPoint).ConfigureAwait(false);
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
            SocketError socErr = await SendResponseAsync(in errResp, buffer).ConfigureAwait(false);
            if (socErr == SocketError.Success)
            {
                await WaitForDisconnectAsync(buffer).ConfigureAwait(false);
            }
        }

        private async ValueTask SendConnectionRefusedAndDisconnectAsync(Memory<byte> buffer)
        {
            var errResp = new Socks5Response(ResponseCode.ConnectionRefused, IPAddress.Any);
            SocketError socErr = await SendResponseAsync(in errResp, buffer).ConfigureAwait(false);
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
            Memory<byte> span = socksResponse.Write(buffer);
            return _managedTcp.SendAsync(span);
        }

        public void Dispose()
        {
            _managedTcp.Dispose();
        }

        [Conditional("DEBUG")]
        private static void LogDebug(string message)
        {
            Trace.WriteLine(message);
        }
    }
}
