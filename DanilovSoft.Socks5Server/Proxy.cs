using System;
using System.Buffers;
using System.Collections.Generic;
using System.Diagnostics;
using System.Diagnostics.CodeAnalysis;
using System.Net;
using System.Net.Sockets;
using System.Runtime.InteropServices;
using System.Text;
using System.Threading;
using System.Threading.Tasks;

namespace DanilovSoft.Socks5Server
{
    internal sealed class Proxy
    {
        private const int ProxyBufferSize = 4096;
        public int ConnectionId { get; }
        private readonly ManagedTcpSocket Socket;
        private int _socketCloseRequestCount;
        private Proxy _other;

#if DEBUG
        private bool _shutdownSend;
        private bool _shutdownReceive;
        private bool _closed;
#endif

        public Proxy(ManagedTcpSocket socket, int connectionId)
        {
            Socket = socket;
            ConnectionId = connectionId;
        }

        /// <remarks>Не бросает исключения.</remarks>
        public static Task RunAsync(int connectionId, ManagedTcpSocket socketA, ManagedTcpSocket socketB)
        {
            var proxy1 = new Proxy(socketA, connectionId);
            var proxy2 = new Proxy(socketB, connectionId);

            proxy1._other = proxy2;
            proxy2._other = proxy1;

            Task task1 = Task.Factory.StartNew(proxy1.ReceiveAsync, CancellationToken.None, TaskCreationOptions.None, TaskScheduler.Default).Unwrap();
            Task task2 = Task.Factory.StartNew(proxy2.ReceiveAsync, CancellationToken.None, TaskCreationOptions.None, TaskScheduler.Default).Unwrap();

            return Task.WhenAll(task1, task2);
        }

        private async Task ReceiveAsync()
        {
            // Арендуем память на длительное время(!).
            // TO TNINK возможно лучше создать свой пул что-бы не истощить общий 
            // в случае когда мы не одни его используем.
            var rent = MemoryPool<byte>.Shared.Rent(ProxyBufferSize);
            try
            {
                while (true)
                {
                    SocketReceiveResult rcv;
                    try
                    {
                        rcv = await Socket.ReceiveAsync(rent.Memory).ConfigureAwait(false);
                    }
                    catch
                    {
                        ShutdownReceiveOtherAndTryClose(); // Отправлять в другой сокет больше не будем.
                        return;
                    }

                    if (rcv.BytesReceived > 0 && rcv.ErrorCode == SocketError.Success)
                    {
                        SocketError sendError;
                        try
                        {
                            sendError = await _other.Socket.SendAsync(rent.Memory.Slice(0, rcv.BytesReceived)).ConfigureAwait(false);
                        }
                        catch
                        {
                            ShutdownReceiveOtherAndTryClose(); // Читать из своего сокета больше не будем.
                            return;
                        }

                        if (sendError != SocketError.Success)
                        // Сокет не принял данные.
                        {
                            ShutdownReceiveOtherAndTryClose(); // Читать из своего сокета больше не будем.
                            return;
                        }
                    }
                    else
                    // tcp соединение закрылось на приём.
                    {
                        if (rcv.BytesReceived == 0 && rcv.ErrorCode == SocketError.Success)
                        // Грациозное закрытие —> инициатором закрытия была удалённая сторона tcp.
                        {
                            ShutdownSendOther(); // Делаем грациозное закрытие.
                            return;
                        }
                        else
                        // Не нормальное закрытие tcp.
                        {
                            if (rcv.ErrorCode == SocketError.Shutdown || rcv.ErrorCode == SocketError.OperationAborted)
                            // Другой поток сделал Shutdown-Receive что-бы здесь мы сделали Close.
                            {
                                return; // Блок finally сдлает Close.
                            }
                            else
                            // Обрыв tcp.
                            // Удалённая сторона tcp была инициатором обрыва.
                            {
                                // ConnectionAborted или ConnectionReset.
                                Debug.Assert(rcv.ErrorCode == SocketError.ConnectionAborted || rcv.ErrorCode == SocketError.ConnectionReset);

                                // Делаем грязный обрыв у другого потока.
                                ShutdownReceiveOtherAndTryClose();
                                
                                return;
                            }
                        }
                    }
                }
            }
            finally
            {
                rent.Dispose();
                TryCloseSelf();
            }
        }

        /// <summary>_other.Close()</summary>
        private bool ShutdownReceiveOtherAndTryClose()
        {
            // Прервём другой поток что-бы он сам закрыл свой сокет исли атомарно у нас не получится.
            ShutdownReceiveOther();

            if (Interlocked.Increment(ref _other._socketCloseRequestCount) == 2)
            // Оба потока завершили взаимодействия с этим сокетом и его можно безопасно уничтожить.
            {
                try
                {
                    // Ноль спровоцирует команду RST и удалённая сторона получит обрыв.
                    _other.Socket.Client.Close(timeout: 0);

                    // Альтернативный способ + Close без таймаута.
                    //socketTo.LingerState = new LingerOption(enable: true, seconds: 0);
                }
                catch { }
                _other.DebugSetClosed();
                return true;
            }
            else
                return false;
        }

        /// <remarks>Не бросает исключения.</remarks>
        private void TryCloseSelf()
        {
            // Другой поток может отправлять данные в этот момент.
            // Закрытие сокета может спровоцировать ObjectDisposed в другом потоке.

            if (Interlocked.Increment(ref _socketCloseRequestCount) == 2)
            // Оба потока завершили взаимодействия с этим сокетом и его можно безопасно уничтожить.
            {
                try
                {
                    // Ноль спровоцирует команду RST и удалённая сторона получит обрыв.
                    Socket.Client.Close(timeout: 0);

                    // Альтернативный способ + Close без таймаута.
                    //socketTo.LingerState = new LingerOption(enable: true, seconds: 0);
                }
                catch { }

                DebugSetClosed();
            }
        }

        /// <summary>_otherSocket.Shutdown(Receive)</summary>
        /// <remarks>Не бросает исключения.</remarks>
        private void ShutdownReceiveOther()
        {
            try
            {
                _other.Socket.Client.Shutdown(SocketShutdown.Receive);
            }
            catch (ObjectDisposedException)
            {

            }
            catch
            {

            }

            _other.DebugSetShutdownReceive();
        }

        //private void ShutdownReceive()
        //{
        //    try
        //    {
        //        Socket.Client.Shutdown(SocketShutdown.Receive);
        //    }
        //    catch (ObjectDisposedException)
        //    {

        //    }
        //    catch
        //    {

        //    }
        //    Debug.WriteLine("Shutdown(Receive)");
        //    DebugSetShutdownReceive();
        //}

        /// <summary>_otherSocket.Shutdown(Send)</summary>
        /// <remarks>Не бросает исключения.</remarks>
        private void ShutdownSendOther()
        {
            try
            {
                _other.Socket.Client.Shutdown(SocketShutdown.Send);
            }
            catch (ObjectDisposedException)
            {

            }
            catch 
            {
            
            }

            _other.DebugSetShutdownSend();
        }

        [Conditional("DEBUG")]
        private void DebugSetShutdownSend()
        {
#if DEBUG
            _shutdownSend = true;
#endif
        }

        [Conditional("DEBUG")]
        private void DebugSetShutdownReceive()
        {
#if DEBUG
            _shutdownReceive = true;
#endif
        }

        [Conditional("DEBUG")]
        private void DebugSetClosed()
        {
#if DEBUG
            _closed = true;
#endif
        }
    }
}
