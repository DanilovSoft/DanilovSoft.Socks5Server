using System.Diagnostics;
using System.Net;
using System.Net.Sockets;
using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;
using System.Threading.Tasks.Sources;
using DanilovSoft.Socks5Server;

namespace DanilovSoft.Socks5Server.TcpSocket;

internal sealed class ManagedTcpSocket(Socket socket) : IDisposable
{
    private readonly AwaitableSocketAsyncEventArgs _receiveArgs = new();
    private readonly AwaitableSocketAsyncEventArgs _sendArgs = new();
    private int _disposed;
    private bool IsDisposed => _disposed != 0;

    public Socket Client => socket;

    /// <exception cref="ObjectDisposedException"/>
    public ValueTask<SocketReceiveResult> Receive(Memory<byte> buffer, CancellationToken ct = default)
    {
        if (IsDisposed)
        {
            return DisposedValueTask<SocketReceiveResult>();
        }

        if (ct.IsCancellationRequested)
        {
            return new ValueTask<SocketReceiveResult>(Task.FromCanceled<SocketReceiveResult>(ct));
        }

        if (!_receiveArgs.Reserve())
        {
            return ValueTask.FromException<SocketReceiveResult>(SimultaneouslyOperationException());
        }

        return _receiveArgs.Receive(socket, buffer, ct);
    }

    /// <exception cref="ObjectDisposedException"/>
    public ValueTask<SocketError> Send(ReadOnlyMemory<byte> buffer, CancellationToken ct = default)
    {
        if (IsDisposed)
        {
            return DisposedValueTask<SocketError>();
        }

        if (ct.IsCancellationRequested)
        {
            return ValueTask.FromCanceled<SocketError>(ct);
        }

        if (!_sendArgs.Reserve())
        {
            return ValueTask.FromException<SocketError>(SimultaneouslyOperationException());
        }

        return _sendArgs.Send(socket, buffer, ct);
    }

    /// <exception cref="ObjectDisposedException"/>
    public Task<SocketError> Connect(EndPoint remoteEP, CancellationToken ct = default)
    {
        if (IsDisposed)
        {
            return Task.FromException<SocketError>(DisposedException());
        }

        // Можем использовать слот чтения или отправки.
        var socketArgs = _receiveArgs;

        // Слот должен быть свободен потому что подключение это самая первая операция (только для TCP).
        if (!socketArgs.Reserve())
        {
            Debug.Assert(false);
            return Task.FromException<SocketError>(SimultaneouslyOperationException());
        }

        socketArgs.RemoteEndPoint = remoteEP;

        if (ct.CanBeCanceled)
        {
            return Connect(socket, socketArgs, ct);
        }
        else
        {
            return socketArgs.ConnectAsync(socket).AsTask();
        }

        static async Task<SocketError> Connect(Socket socket, AwaitableSocketAsyncEventArgs socketArgs, CancellationToken ct)
        {
            using (ct.UnsafeRegister(static s => Socket.CancelConnectAsync((AwaitableSocketAsyncEventArgs)s!), socketArgs))
            {
                return await socketArgs.ConnectAsync(socket).ConfigureAwait(false);
            }
        }
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private ValueTask<T> DisposedValueTask<T>() => ValueTask.FromException<T>(DisposedException());

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private ObjectDisposedException DisposedException() => ThrowHelper.ObjectDisposedException(GetType().FullName);

    /// <summary>
    /// Атомарный.
    /// </summary>
    public void Dispose()
    {
        if (Interlocked.CompareExchange(ref _disposed, 1, 0) == 0)
        {
            _receiveArgs.Dispose();
            _sendArgs.Dispose();
            socket.Dispose();
        }
    }

    private sealed class AwaitableSocketAsyncEventArgs : SocketAsyncEventArgs, IValueTaskSource<SocketError>, IValueTaskSource<SocketReceiveResult>
    {
        /// <summary>Sentinel object used to indicate that the operation has completed prior to OnCompleted being called.</summary>
        private static readonly Action<object?> CompletedSentinel = new(static state => throw new Exception(nameof(CompletedSentinel)));
        /// <summary>Sentinel object used to indicate that the instance is available for use.</summary>
        private static readonly Action<object?> AvailableSentinel = new(static state => throw new Exception(nameof(AvailableSentinel)));
        /// <summary>
        /// <see cref="AvailableSentinel"/> if the object is available for use, after GetResult has been called on a previous use.
        /// null if the operation has not completed.
        /// <see cref="CompletedSentinel"/> if it has completed.
        /// Another delegate if OnCompleted was called before the operation could complete, in which case it's the delegate to invoke
        /// when the operation does complete.
        /// </summary>
        private Action<object?>? _continuation = AvailableSentinel;
        private ExecutionContext? _executionContext;
        private object? _scheduler;
        /// <summary>
        /// Текущее значение токена отданное ValueTask'у которое затем будет сравнено со значением переданным нам обратно.
        /// </summary>
        /// <remarks>
        /// Это не обеспечивает абсолютную синхронизацию, а даёт превентивную защиту от неправильного
        /// использования ValueTask, таких как многократное ожидание и обращение к уже использованому прежде.
        /// </remarks>
        private short _token;

        public AwaitableSocketAsyncEventArgs() : base()
        {

        }

        internal bool Reserve() => ReferenceEquals(Interlocked.CompareExchange(ref _continuation, null, AvailableSentinel), AvailableSentinel);

        protected override void OnCompleted(SocketAsyncEventArgs _)
        {
            // When the operation completes, see if OnCompleted was already called to hook up a continuation.
            // If it was, invoke the continuation.
            var c = _continuation;
            if (c != null || (c = Interlocked.CompareExchange(ref _continuation, CompletedSentinel, null)) != null)
            {
                Debug.Assert(c != AvailableSentinel, "The delegate should not have been the available sentinel.");
                Debug.Assert(c != CompletedSentinel, "The delegate should not have been the completed sentinel.");

                var continuationState = UserToken;
                UserToken = null;
                _continuation = CompletedSentinel; // in case someone's polling IsCompleted

                var ec = _executionContext;
                if (ec == null)
                {
                    InvokeContinuation(c, continuationState, forceAsync: false, requiresExecutionContextFlow: false);
                }
                else
                {
                    // This case should be relatively rare, as the async Task/ValueTask method builders
                    // use the awaiter's UnsafeOnCompleted, so this will only happen with code that
                    // explicitly uses the awaiter's OnCompleted instead.
                    _executionContext = null;
                    ExecutionContext.Run(ec, runState =>
                    {
                        var t = (Tuple<AwaitableSocketAsyncEventArgs, Action<object?>, object?>)runState!;
                        t.Item1.InvokeContinuation(t.Item2, t.Item3, forceAsync: false, requiresExecutionContextFlow: false);
                    }, Tuple.Create(this, c, continuationState));
                }
            }
        }

        internal ValueTask<SocketReceiveResult> Receive(Socket socket, Memory<byte> buffer, CancellationToken ct = default)
        {
            Debug.Assert(Volatile.Read(ref _continuation) == null, $"Expected null continuation to indicate reserved for use");

            SetBuffer(buffer);

            if (socket.ReceiveAsync(this))
            {
                // Ждём пока прочитаются данные.
                return new ValueTask<SocketReceiveResult>(this, _token);
            }
            else
            {
                Release();
                return new ValueTask<SocketReceiveResult>(new SocketReceiveResult(BytesTransferred, SocketError));
            }
        }

        internal ValueTask<SocketError> Send(Socket socket, ReadOnlyMemory<byte> buffer, CancellationToken ct = default)
        {
            Debug.Assert(Volatile.Read(ref _continuation) == null, $"Expected null continuation to indicate reserved for use");

            // Меняет тип буффера на доступный для записи(!).
            // Но запись ни в коем случае не должна осуществляться.
            // Это безопасно так как SetBuffer будет производить только чтение.
            var memory = MemoryMarshal.AsMemory(buffer);

            SetBuffer(memory);

            if (socket.SendAsync(this))
            // Операция завершится асинхронно.
            {
                // Ждём пока отправится.
                return new ValueTask<SocketError>(this, _token);
            }
            else
            {
                Release();
                return new ValueTask<SocketError>(SocketError);
            }
        }

        /// <exception cref="Exception"/>
        internal ValueTask<SocketError> ConnectAsync(Socket socket)
        {
            Debug.Assert(Volatile.Read(ref _continuation) == null, $"Expected null continuation to indicate reserved for use");

            try
            {
                // Tcp всегда завершается асинхронно.
                // Синхронно может случиться NoBufferSpaceAvailable.

                if (socket.ConnectAsync(this))
                {
                    // Может случиться AddressAlreadyInUse если занять все клиентские порты.
                    return new ValueTask<SocketError>(this, _token);
                }
            }
            catch
            {
                Release();
                throw;
            }

            var socketError = SocketError;
            Release();
            return new ValueTask<SocketError>(result: socketError);
        }

        internal ValueTask<SocketError> SendAsync(Socket socket, byte[] buffer, int offset, int count)
        {
            Debug.Assert(Volatile.Read(ref _continuation) == null, $"Expected null continuation to indicate reserved for use");

            SetBuffer(buffer, offset, count);

            if (socket.SendAsync(this))
            // Операция завершится асинхронно.
            {
                // Ждём пока отправится.
                return new ValueTask<SocketError>(this, _token);
            }
            else
            {
                Release();

                return new ValueTask<SocketError>(SocketError);
            }
        }

        internal ValueTask<SocketReceiveResult> ReceiveAsync(Socket socket, byte[] buffer, int offset, int count)
        {
            Debug.Assert(Volatile.Read(ref _continuation) == null, $"Expected null continuation to indicate reserved for use");

            SetBuffer(buffer, offset, count);

            if (socket.ReceiveAsync(this))
            // Операция завершится асинхронно.
            {
                return new ValueTask<SocketReceiveResult>(this, _token);
            }
            else
            // Операция выполнилась синхронно.
            {
                Release();
                return new ValueTask<SocketReceiveResult>(new SocketReceiveResult(BytesTransferred, SocketError));
            }
        }

        private void Release()
        {
            _token++;
            // Тоже самое что CAS но дополняет барьером памяти.
            Volatile.Write(ref _continuation, AvailableSentinel);
        }

        // Результат отправки.
        // Нельзя выполнять больше одного раза.
        SocketError IValueTaskSource<SocketError>.GetResult(short token)
        {
            if (token != _token)
            {
                ThrowIncorrectTokenException();
            }

            var error = SocketError;

            Release();

            return error;
        }

        // Результат приёма.
        // Нельзя выполнять больше одного раза.
        public SocketReceiveResult GetResult(short token)
        {
            if (token != _token)
            {
                ThrowIncorrectTokenException();
            }

            var error = SocketError;
            var bytes = BytesTransferred;

            Release();

            return new SocketReceiveResult(bytes, error);
        }

        public ValueTaskSourceStatus GetStatus(short token)
        {
            if (token != _token)
            {
                ThrowIncorrectTokenException();
            }

            return
                !ReferenceEquals(_continuation, CompletedSentinel) ? ValueTaskSourceStatus.Pending : ValueTaskSourceStatus.Succeeded;
            //base.SocketError == SocketError.Success ? ValueTaskSourceStatus.Succeeded :
            //ValueTaskSourceStatus.Faulted;
        }

        public void OnCompleted(Action<object?> continuation, object? state, short token, ValueTaskSourceOnCompletedFlags flags)
        {
            if (token != _token)
            {
                ThrowIncorrectTokenException();
            }

            if ((flags & ValueTaskSourceOnCompletedFlags.FlowExecutionContext) != 0)
            {
                _executionContext = ExecutionContext.Capture();
            }

            if ((flags & ValueTaskSourceOnCompletedFlags.UseSchedulingContext) != 0)
            {
                var sc = SynchronizationContext.Current;
                if (sc != null && sc.GetType() != typeof(SynchronizationContext))
                {
                    _scheduler = sc;
                }
                else
                {
                    var ts = TaskScheduler.Current;
                    if (ts != TaskScheduler.Default)
                    {
                        _scheduler = ts;
                    }
                }
            }

            UserToken = state; // Use UserToken to carry the continuation state around
            var prevContinuation = Interlocked.CompareExchange(ref _continuation, continuation, null);
            if (ReferenceEquals(prevContinuation, CompletedSentinel))
            {
                // Lost the race condition and the operation has now already completed.
                // We need to invoke the continuation, but it must be asynchronously to
                // avoid a stack dive.  However, since all of the queueing mechanisms flow
                // ExecutionContext, and since we're still in the same context where we
                // captured it, we can just ignore the one we captured.
                var requiresExecutionContextFlow = _executionContext != null;
                _executionContext = null;
                UserToken = null; // we have the state in "state"; no need for the one in UserToken
                InvokeContinuation(continuation, state, forceAsync: true, requiresExecutionContextFlow);
            }
            else if (prevContinuation != null)
            {
                // Flag errors with the continuation being hooked up multiple times.
                // This is purely to help alert a developer to a bug they need to fix.
                ThrowMultipleContinuationsException();
            }
        }

        private void InvokeContinuation(Action<object?> continuation, object? state, bool forceAsync, bool requiresExecutionContextFlow)
        {
            var scheduler = _scheduler;
            _scheduler = null;

            if (scheduler != null)
            {
                if (scheduler is SynchronizationContext sc)
                {
                    sc.Post(s =>
                    {
                        var t = (Tuple<Action<object?>, object?>)s!;
                        t.Item1(t.Item2);
                    }, Tuple.Create(continuation, state));
                }
                else
                {
                    Debug.Assert(scheduler is TaskScheduler, $"Expected TaskScheduler, got {scheduler}");
                    Task.Factory.StartNew(continuation, state, CancellationToken.None, TaskCreationOptions.DenyChildAttach, (TaskScheduler)scheduler);
                }
            }
            else if (forceAsync)
            {
                if (requiresExecutionContextFlow)
                {
                    ThreadPool.QueueUserWorkItem(continuation, state, preferLocal: true);
                }
                else
                {
                    ThreadPool.UnsafeQueueUserWorkItem(continuation, state, preferLocal: true);
                }
            }
            else
            {
                continuation(state);
            }
        }

        private static void ThrowIncorrectTokenException() =>
            throw new InvalidOperationException("Произошла попытка одновременного выполнения операции чтения или записи на сокете.");

        private static void ThrowMultipleContinuationsException() => throw new InvalidOperationException("Multiple continuations not allowed.");
    }

    private static InvalidOperationException SimultaneouslyOperationException() => new("Operation already in progress.");
}