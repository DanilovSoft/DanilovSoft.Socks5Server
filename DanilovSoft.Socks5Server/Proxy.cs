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
    internal sealed class ProxyState
    {
        internal volatile bool GotClose;
        internal ProxyState State { get; set; }
    }

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
            var state1 = new ProxyState();
            var state2 = new ProxyState();

            state1.State = state2;
            state2.State = state1;

            Task task1 = Task.Factory.StartNew(ProxyAsync, Tuple.Create(_socketA, _socketB, state1), CancellationToken.None, TaskCreationOptions.None, TaskScheduler.Default).Unwrap();
            Task task2 = Task.Factory.StartNew(ProxyAsync, Tuple.Create(_socketB, _socketA, state2), CancellationToken.None, TaskCreationOptions.None, TaskScheduler.Default).Unwrap();

            return Task.WhenAll(task1, task2);
        }

        private static Task ProxyAsync(object? state)
        {
            Debug.Assert(state != null);

            var a = (Tuple<ManagedTcpSocket, ManagedTcpSocket, ProxyState>)state;
            return ProxyAsync(a.Item1, a.Item2, a.Item3);
        }

        private static async Task ProxyAsync(ManagedTcpSocket socketFrom, ManagedTcpSocket socketTo, ProxyState state)
        {
            // Арендуем память на длительное время(!).
            // TO TNINK возможно лучше создать свой пул что-бы не истощить общий 
            // в случае когда мы не одни его используем.
            using (var rent = MemoryPool<byte>.Shared.Rent(ProxyBufferSize))
            {
                while (true)
                {
                    SocketReceiveResult rcv;
                    try
                    {
                        rcv = await socketFrom.ReceiveAsync(rent.Memory).ConfigureAwait(false);
                    }
                    catch
                    {
                        ConveyClose(socketTo, state); // Отправлять в этот сокет больше не будем.
                        return;
                    }

                    if (rcv.BytesReceived > 0 && rcv.ErrorCode == SocketError.Success)
                    {
                        SocketError sendError;
                        try
                        {
                            sendError = await socketTo.SendAsync(rent.Memory.Slice(0, rcv.BytesReceived)).ConfigureAwait(false);
                        }
                        catch
                        {
                            // Другая сторона получит RST если что-то пришлёт.
                            TcpReset(socketFrom); // Читать из этого сокета больше не будем.
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
                            TcpReset(socketFrom); // Читать из этого сокета больше не будем.
                            return;
                        }
                    }
                    else
                    // tcp соединение закрылось на приём.
                    {
                        if (rcv.BytesReceived == 0 && rcv.ErrorCode == SocketError.Success)
                        // Грациозное закрытие —> инициатором закрытия была удалённая сторона tcp.
                        {
                            ShutdownSend(socketTo); // Делаем грациозное закрытие на стороне RPC.
                            return;
                        }
                        else
                        // Обрыв tcp.
                        // Инициатором обрыва может быть как удалённая сторона tcp, так и другой поток.
                        {
                            if (rcv.ErrorCode == SocketError.Shutdown || rcv.ErrorCode == SocketError.OperationAborted)
                            // Другой поток получил Close (или Data но не смог записать в tcp) и сделал Shutdown-Receive что-бы здесь мы сделали Reset.
                            {
                                // Делаем грязный обрыв.
                                TcpReset(socketFrom);

                                if (!state.GotClose)
                                {
                                    // Делаем грязный обрыв у другого потока.
                                    ConveyClose(socketTo, state);
                                    return;
                                }
                                else
                                {

                                }
                            }
                            else
                            // Удалённая сторона tcp была инициатором обрыва.
                            {
                                // ConnectionAborted или ConnectionReset.
                                Debug.Assert(rcv.ErrorCode == SocketError.ConnectionAborted || rcv.ErrorCode == SocketError.ConnectionReset);

                                // Отправлять данные в этот сокет больше невозможно поэтому сразу закрываем его.
                                TcpReset(socketFrom);

                                // Делаем грязный обрыв у другого потока.
                                ConveyClose(socketTo, state);

                                return;
                            }
                        }
                    }
                }
            }
        }

        private static void TcpReset(ManagedTcpSocket socket)
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
        private static void ConveyClose(ManagedTcpSocket socket, ProxyState state)
        {
            state.State.GotClose = true;
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
