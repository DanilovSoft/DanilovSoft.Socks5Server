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
                    SocketReceiveResult receiveResult;
                    try
                    {
                        receiveResult = await socketFrom.ReceiveAsync(rent.Memory).ConfigureAwait(false);
                    }
                    catch
                    {
                        ShutdownSend(socketTo); // Отправлять в этот сокет больше не будем.
                        return;
                    }

                    if (receiveResult.Count > 0 && receiveResult.SocketError == SocketError.Success)
                    {
                        SocketError sendError;
                        try
                        {
                            sendError = await socketTo.SendAsync(rent.Memory.Slice(0, receiveResult.Count)).ConfigureAwait(false);
                        }
                        catch
                        {
                            // Другая сторона получит RST если что-то пришлёт.
                            ShutdownReceive(socketFrom); // Читать из этого сокета больше не будем.
                            return;
                        }

                        if (sendError == SocketError.Success)
                        {
                            continue;
                        }
                        else
                        // Сокет не принял данные.
                        {
                            // Другая сторона получит RST если что-то пришлёт.
                            ShutdownReceive(socketFrom); // Читать из этого сокета больше не будем.
                            return;
                        }
                    }
                    else
                    // Не удалось прочитать из сокета.
                    {
                        if (receiveResult.Count == 0 && receiveResult.SocketError == SocketError.Success)
                        // Грациозное закрытие.
                        {
                            ShutdownSend(socketTo); // Отправлять в этот сокет больше не будем.
                            return;
                        }
                        else
                        // Случился грязный обрыв.
                        {
                            // Нужно закрыть соединение так что-бы на другой стороне тоже получился грязный обрыв, 
                            // иначе другая сторона может решить что все данные успешно приняты.
                            AbortConnection(socketTo);
                            return;
                        }
                    }
                }
            }
        }

        private static void AbortConnection(ManagedTcpSocket socket)
        {
            try
            {
                // Ноль спровоцирует команду RST и удалённая сторона получит обрыв.
                socket.Client.Close(timeout: 0);

                // Альтернативный способ + Close без таймаута.
                //socketTo.LingerState = new LingerOption(enable: true, seconds: 0);
            }
            catch { }
        }

        /// <summary>
        /// Соединение завершается, если после вызова Shutdown выполняется одно из следующих условий:
        /// <list type="bullet">
        /// <item>Данные находятся в входящем сетевом буфере, ожидающем получения.</item>
        /// <item>Получены дополнительные данные.</item>
        /// </list>
        /// </summary>
        private static void ShutdownReceive(ManagedTcpSocket socket)
        {
            try
            {
                socket.Client.Shutdown(SocketShutdown.Receive);
            }
            catch { }
        }

        private static void ShutdownSend(ManagedTcpSocket socket)
        {
            try
            {
                socket.Client.Shutdown(SocketShutdown.Send);
            }
            catch { }
        }
    }
}
