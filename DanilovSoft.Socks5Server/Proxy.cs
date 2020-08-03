using System;
using System.Buffers;
using System.Collections.Generic;
using System.Diagnostics;
using System.Net;
using System.Net.Sockets;
using System.Runtime.InteropServices;
using System.Text;
using System.Threading;
using System.Threading.Tasks;

namespace DanilovSoft.Socks5Server
{
    [StructLayout(LayoutKind.Auto)]
    internal readonly struct Proxy
    {
        private const int ProxyBufferSize = 4096;
        private readonly ManagedTcpSocket _socketA;
        private readonly ManagedTcpSocket _socketB;

        public Proxy(ManagedTcpSocket socketA, ManagedTcpSocket socketB)
        {
            _socketA = socketA;
            _socketB = socketB;
        }

        public Task RunAsync()
        {
            var task1 = Task.Factory.StartNew(ProxyAsync, (_socketA, _socketB), CancellationToken.None, TaskCreationOptions.None, TaskScheduler.Default).Unwrap();
            var task2 = Task.Factory.StartNew(ProxyAsync, (_socketB, _socketA), CancellationToken.None, TaskCreationOptions.None, TaskScheduler.Default).Unwrap();

            return Task.WhenAll(task1, task2);
        }

        private static Task ProxyAsync(object? state)
        {
            Debug.Assert(state != null);
            (ManagedTcpSocket socket1, ManagedTcpSocket socket2) = ((ManagedTcpSocket, ManagedTcpSocket))state;

            return ProxyAsync(socket1, socket2);
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
                        SocketError sendError;
                        try
                        {
                            sendError = await socketTo.SendAsync(rent.Memory.Slice(0, result.Count)).ConfigureAwait(false);
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

                        if (sendError == SocketError.Success)
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
                        if (result.Count == 0 && result.SocketError == SocketError.Success)
                        // Грациозное закрытие.
                        {
                            try
                            {
                                socketTo.Client.Shutdown(SocketShutdown.Send);
                            }
                            catch { }
                        }
                        else
                        // Случился грязный обрыв.
                        {
                            // Нужно закрыть соединение так что-бы на другой стороне тоже получился грязный обрыв, 
                            // иначе другая сторона может решить что все данные успешно приняты.
                            try
                            {
                                // Ноль спровоцирует команду RST и удалённая сторона получит обрыв.
                                socketTo.Client.Close(timeout: 0);

                                // Альтернативный способ + Close без таймаута.
                                //socketTo.LingerState = new LingerOption(enable: true, seconds: 0);
                            }
                            catch { }
                        }
                        return;
                    }
                }
            }
        }
    }
}
