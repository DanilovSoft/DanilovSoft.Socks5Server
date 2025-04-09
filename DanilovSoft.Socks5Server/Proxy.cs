using System.Buffers;
using System.Diagnostics;
using System.Net;
using System.Net.Sockets;

namespace DanilovSoft.Socks5Server;

internal sealed class Proxy
{
    private const int ProxyBufferSize = 4096;
    public int ConnectionId { get; }
    private readonly ManagedTcpSocket _socket;
    private readonly CancellationToken _cancellationToken;
    private int _socketCloseRequestCount;
    private Proxy _other = null!;

#if DEBUG
    private volatile bool _shutdownSend;
    private volatile bool _shutdownReceive;
    private volatile bool _closed;
    private volatile bool _disconnect;
#endif

    public Proxy(ManagedTcpSocket socket, int connectionId, CancellationToken cancellationToken)
    {
        _socket = socket;
        ConnectionId = connectionId;
        _cancellationToken = cancellationToken;
    }

    public static Task RunAsync(int connectionId, ManagedTcpSocket socketA, ManagedTcpSocket socketB, CancellationToken ct = default)
    {
        var proxy1 = new Proxy(socketA, connectionId, ct);
        var proxy2 = new Proxy(socketB, connectionId, ct);

        proxy1._other = proxy2;
        proxy2._other = proxy1;

        var task1 = Task.Factory.StartNew(static proxy1 => ((Proxy)proxy1!).RunAsync(), proxy1, ct, TaskCreationOptions.DenyChildAttach, TaskScheduler.Default).Unwrap();
        var task2 = Task.Factory.StartNew(static proxy2 => ((Proxy)proxy2!).RunAsync(), proxy2, ct, TaskCreationOptions.DenyChildAttach, TaskScheduler.Default).Unwrap();

        return Task.WhenAll(task1, task2);
    }

    private Task RunAsync() => Run(_cancellationToken);

    private async Task Run(CancellationToken ct)
    {
        Debug.Assert(_other != null);

        // Арендуем память на длительное время(!).
        // TO TNINK возможно лучше создать свой пул что-бы не истощить общий 
        // в случае когда мы не одни его используем.
        byte[] rent = ArrayPool<byte>.Shared.Rent(ProxyBufferSize);

        try
        {
            while (true)
            {
                SocketReceiveResult rcv;
                try
                {
                    rcv = await _socket.ReceiveAsync(rent, ct).ConfigureAwait(false);
                }
                catch when (!ct.IsCancellationRequested)
                {
                    ShutdownReceiveOtherAndTryCloseSelf(); // Отправлять в другой сокет больше не будем.
                    return;
                }

                if (rcv.BytesReceived > 0 && rcv.ErrorCode == SocketError.Success)
                {
                    SocketError sendError;
                    try
                    {
                        sendError = await _other._socket.SendAsync(rent.AsMemory(..rcv.BytesReceived), ct).ConfigureAwait(false);
                    }
                    catch when (!ct.IsCancellationRequested)
                    {
                        ShutdownReceiveOtherAndTryCloseSelf(); // Читать из своего сокета больше не будем.
                        return;
                    }

                    if (sendError != SocketError.Success)
                    // Сокет не принял данные.
                    {
                        ShutdownReceiveOtherAndTryCloseSelf(); // Читать из своего сокета больше не будем.
                        return;
                    }
                }
                else // tcp соединение закрылось на приём.
                {
                    if (rcv.BytesReceived == 0 && rcv.ErrorCode == SocketError.Success)
                    // Грациозное закрытие —> инициатором закрытия была удалённая сторона tcp.
                    {
                        Debug.WriteLine($"ConId {ConnectionId} ShutdownSend");

                        _other.ShutdownSend(); // Делаем грациозное закрытие.
                        return;
                    }
                    else
                    // Обрыв tcp.
                    {
                        if (rcv.ErrorCode == SocketError.Shutdown || rcv.ErrorCode == SocketError.OperationAborted)
                        // Другой поток сделал Shutdown-Receive что-бы здесь мы сделали Close.
                        {
                            return; // Блок finally сделает Close.
                        }
                        else
                        // Удалённая сторона tcp была инициатором обрыва.
                        {
                            // ConnectionAborted или ConnectionReset.
                            Debug.Assert(rcv.ErrorCode == SocketError.ConnectionAborted || rcv.ErrorCode == SocketError.ConnectionReset);

                            // Делаем грязный обрыв у другого потока.
                            _other.Disconnect();

                            return;
                        }
                    }
                }
            }
        }
        finally
        {
            ArrayPool<byte>.Shared.Return(rent, clearArray: false);
            TryClose();
        }
    }

    /// <summary>_other.Close()</summary>
    private void ShutdownReceiveOtherAndTryCloseSelf()
    {
        Debug.Assert(_other != null);

        // Прервём другой поток что-бы он сам закрыл свой сокет исли атомарно у нас не получится.
        _other.ShutdownReceive();

        if (Interlocked.Increment(ref _other._socketCloseRequestCount) == 2)
        // Оба потока завершили взаимодействия с этим сокетом и его можно безопасно уничтожить.
        {
            _other.Close();
        }
    }

    /// <remarks>Не бросает исключения.</remarks>
    private void TryClose()
    {
        // Другой поток может отправлять данные в этот момент.
        // Закрытие сокета может спровоцировать ObjectDisposed в другом потоке.

        if (Interlocked.Increment(ref _socketCloseRequestCount) == 2)
        // Оба потока завершили взаимодействия с этим сокетом и его можно безопасно уничтожить.
        {
            Close();
            SetClosedFlag();
        }
    }

    private void Close()
    {
        try
        {
            // Ноль спровоцирует команду RST и удалённая сторона получит обрыв.
            _socket.Client.Close(timeout: 0);

            // Альтернативный способ + Close без таймаута.
            //socketTo.LingerState = new LingerOption(enable: true, seconds: 0);
        }
        catch { }
        SetClosedFlag();
    }

    private void Disconnect()
    {
        _socket.Client.LingerState = new LingerOption(enable: true, seconds: 0);
        try
        {
            // Ноль спровоцирует команду RST и удалённая сторона получит обрыв.
            _socket.Client.Disconnect(reuseSocket: false);

            // Альтернативный способ + Close без таймаута.
            //socketTo.LingerState = new LingerOption(enable: true, seconds: 0);
        }
        catch { }

        Debug.WriteLine($"ConId {ConnectionId} Disconnect");

        SetDisconnectFlag();
    }

    /// <summary>_otherSocket.Shutdown(Receive)</summary>
    /// <remarks>Не бросает исключения.</remarks>
    private void ShutdownReceive()
    {
        Debug.Assert(_socket != null);
        try
        {
            _socket.Client.Shutdown(SocketShutdown.Receive);
        }
        catch { }

        SetShutdownReceiveFlag();
    }

    /// <summary>_otherSocket.Shutdown(Send)</summary>
    /// <remarks>Не бросает исключения.</remarks>
    private void ShutdownSend()
    {
        try
        {
            _socket.Client.Shutdown(SocketShutdown.Send);
        }
        catch (ObjectDisposedException)
        {

        }
        catch 
        {
        
        }

        SetShutdownSendFlag();
    }

    [Conditional("DEBUG")]
    private void SetShutdownSendFlag()
    {
#if DEBUG
        _shutdownSend = true;
#endif
    }

    [Conditional("DEBUG")]
    private void SetShutdownReceiveFlag()
    {
#if DEBUG
        _shutdownReceive = true;
#endif
    }

    [Conditional("DEBUG")]
    private void SetClosedFlag()
    {
#if DEBUG
        _closed = true;
#endif
    }
    
    [Conditional("DEBUG")]
    private void SetDisconnectFlag()
    {
#if DEBUG
        _disconnect = true;
#endif
    }
}
