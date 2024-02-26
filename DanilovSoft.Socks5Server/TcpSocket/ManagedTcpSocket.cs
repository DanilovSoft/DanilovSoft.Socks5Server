using System.Diagnostics;
using System.Net.Sockets;
using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;
using System.Threading.Tasks.Sources;
using DanilovSoft.Socks5Server;

namespace System.Net;

public sealed class ManagedTcpSocket : IDisposable
{
    private readonly AwaitableSocketAsyncEventArgs _receiveArgs;
    private readonly AwaitableSocketAsyncEventArgs _sendArgs;
    private readonly Socket _socket;
    public Socket Client => _socket;
    private int _disposed;
    private bool IsDisposed => _disposed != 0;

    public ManagedTcpSocket(Socket socket)
    {
        _socket = socket;
        //NoDelay = DefaultNoDelay;

        _receiveArgs = new AwaitableSocketAsyncEventArgs();
        _sendArgs = new AwaitableSocketAsyncEventArgs();
    }

#if NETSTANDARD2_0 || NET46

    /// <summary>
    /// Использует MemoryMarshal.TryGetArray.
    /// </summary>
    /// <exception cref="ObjectDisposedException"/>
    public ValueTask<SocketReceiveResult> ReceiveAsync(Memory<byte> buffer, CancellationToken cancellationToken)
    {
        if (MemoryMarshal.TryGetArray(buffer, out ArraySegment<byte> segment))
        {
            return InnerReceiveAsync(segment.Array, segment.Offset, segment.Count, cancellationToken);
        }
        else
            throw new NotSupportedException(Resources.Strings.MemoryGetArray);
    }

    /// <summary>
    /// Использует MemoryMarshal.TryGetArray.
    /// </summary>
    /// <exception cref="SocketException"/>
    /// <exception cref="ObjectDisposedException"/>
    public ValueTask<int> ReadAsync(Memory<byte> memory, CancellationToken cancellationToken)
    {
        if (MemoryMarshal.TryGetArray<byte>(memory, out var segment))
        {
            return ReadAsync(segment.Array, segment.Offset, segment.Count, cancellationToken);
        }
        else
            throw new NotSupportedException(Resources.Strings.MemoryGetArray);
    }

    /// <summary>
    /// Использует MemoryMarshal.TryGetArray.
    /// </summary>
    /// <exception cref="ObjectDisposedException"/>
    public ValueTask<SocketError> SendAsync(ReadOnlyMemory<byte> memory, CancellationToken cancellationToken)
    {
        if (MemoryMarshal.TryGetArray(memory, out var segment))
        {
            return SendAsync(segment.Array, segment.Offset, segment.Count, cancellationToken);
        }
        else
            throw new NotSupportedException(Resources.Strings.MemoryGetArray);
    }
#else
    /// <exception cref="ObjectDisposedException"/>
    public ValueTask<SocketReceiveResult> ReceiveAsync(Memory<byte> buffer, CancellationToken cancellationToken = default)
    {
        if (!IsDisposed)
        {
            if (!cancellationToken.IsCancellationRequested)
            {
                if (_receiveArgs.Reserve())
                {
                    return _receiveArgs.ReceiveAsync(_socket, buffer);
                }
                else
                    return new ValueTask<SocketReceiveResult>(task: Task.FromException<SocketReceiveResult>(SimultaneouslyOperationException()));
            }
            else
                return new ValueTask<SocketReceiveResult>(Task.FromCanceled<SocketReceiveResult>(cancellationToken));
        }
        else
            return DisposedValueTask<SocketReceiveResult>();
    }

    /// <exception cref="ObjectDisposedException"/>
    public ValueTask<SocketError> SendAsync(ReadOnlyMemory<byte> buffer, CancellationToken cancellationToken = default)
    {
        if (!IsDisposed)
        {
            if (!cancellationToken.IsCancellationRequested)
            {
                if (_sendArgs.Reserve())
                {
                    return _sendArgs.SendAsync(_socket, buffer);
                }
                else
                    return new ValueTask<SocketError>(task: Task.FromException<SocketError>(SimultaneouslyOperationException()));
            }
            else
                return new ValueTask<SocketError>(Task.FromCanceled<SocketError>(cancellationToken));
        }
        else
            return DisposedValueTask<SocketError>();
    }
#endif
    /// <exception cref="ObjectDisposedException"/>
    public ValueTask<SocketReceiveResult> ReceiveAsync(byte[] buffer, int offset, int count, CancellationToken cancellationToken)
    {
        return InnerReceiveAsync(buffer, offset, count, cancellationToken);
    }

    /// <exception cref="SocketException"/>
    /// <exception cref="ObjectDisposedException"/>
    public ValueTask WriteAsync(ReadOnlyMemory<byte> buffer, CancellationToken cancellationToken)
    {
        ValueTask<SocketError> t = SendAsync(buffer, cancellationToken);
        if (t.IsCompletedSuccessfully)
        {
            SocketError socErr = t.Result;

            return socErr == SocketError.Success
                ? default
                : new ValueTask(Task.FromException(socErr.ToException()));
        }
        else
        {
            return WaitForWriteTaskAsync(t);
        }

        static async ValueTask WaitForWriteTaskAsync(ValueTask<SocketError> t)
        {
            SocketError socErr = await t.ConfigureAwait(false);
            if (socErr == SocketError.Success)
                return;

            throw socErr.ToException();
        }
    }

    /// <exception cref="SocketException"/>
    /// <exception cref="ObjectDisposedException"/>
    public ValueTask WriteAsync(byte[] buffer, int offset, int count, CancellationToken cancellationToken)
    {
        ValueTask<SocketError> t = SendAsync(buffer, offset, count, cancellationToken);
        if (t.IsCompletedSuccessfully)
        {
            SocketError socErr = t.Result;

            return socErr == SocketError.Success
                ? default
                : new ValueTask(Task.FromException(socErr.ToException()));
        }
        else
        {
            return WaitForSendAsync(t);
        }

        static async ValueTask WaitForSendAsync(ValueTask<SocketError> t)
        {
            SocketError socErr = await t.ConfigureAwait(false);
            if (socErr == SocketError.Success)
                return;

            throw socErr.ToException();
        }
    }

    /// <summary>
    /// Бросает исключение если операция завершилась с кодом возврата отличным от Success.
    /// </summary>
    /// <exception cref="SocketException"/>
    /// <exception cref="ObjectDisposedException"/>
    public ValueTask<int> ReadAsync(byte[] buffer, int offset, int count, CancellationToken cancellationToken)
    {
        ValueTask<SocketReceiveResult> t = InnerReceiveAsync(buffer, offset, count, cancellationToken);
        if (t.IsCompletedSuccessfully)
        {
            SocketReceiveResult result = t.Result;

            return result.ErrorCode == SocketError.Success
                ? new ValueTask<int>(result.BytesReceived)
                : new ValueTask<int>(Task.FromException<int>(result.ErrorCode.ToException()));
        }
        else
        {
            return WaitForReadAsync(t);
        }

        static async ValueTask<int> WaitForReadAsync(ValueTask<SocketReceiveResult> t)
        {
            SocketReceiveResult result = await t.ConfigureAwait(false);
            if (result.ErrorCode == SocketError.Success)
                return result.BytesReceived;

            throw result.ErrorCode.ToException();
        }
    }

    /// <exception cref="ObjectDisposedException"/>
    public ValueTask<SocketError> SendAsync(byte[] buffer, int offset, int count, CancellationToken cancellationToken = default)
    {
        if (!IsDisposed)
        {
            if (!cancellationToken.IsCancellationRequested)
            {
                if (_sendArgs.Reserve())
                {
                    return _sendArgs.SendAsync(_socket, buffer, offset, count);
                }
                else
                    return new ValueTask<SocketError>(task: Task.FromException<SocketError>(SimultaneouslyOperationException()));
            }
            else
                return new ValueTask<SocketError>(Task.FromCanceled<SocketError>(cancellationToken));
        }
        else
            return DisposedValueTask<SocketError>();
    }

    /// <exception cref="ObjectDisposedException"/>
    private ValueTask<SocketReceiveResult> InnerReceiveAsync(byte[] buffer, int offset, int count, CancellationToken cancellationToken = default)
    {
        if (!IsDisposed)
        {
            if (!cancellationToken.IsCancellationRequested)
            {
                if (_receiveArgs.Reserve())
                {
                    return _receiveArgs.ReceiveAsync(_socket, buffer, offset, count);
                }
                else
                    return new ValueTask<SocketReceiveResult>(task: Task.FromException<SocketReceiveResult>(SimultaneouslyOperationException()));
            }
            else
                return new ValueTask<SocketReceiveResult>(Task.FromCanceled<SocketReceiveResult>(cancellationToken));
        }
        else
            return DisposedValueTask<SocketReceiveResult>();
    }

    /// <exception cref="ObjectDisposedException"/>
    public Task<SocketError> ConnectAsync(EndPoint endPoint)
    {
        if (!IsDisposed)
        {
            // Можем использовать слот чтения или отправки.
            AwaitableSocketAsyncEventArgs saea = _sendArgs;

            // Слот должен быть свободен потому что подключение это самая первая операция (только для TCP).
            if (!saea.Reserve())
            {
                Debug.Assert(false);
                return Task.FromException<SocketError>(SimultaneouslyOperationException());
            }

            saea.RemoteEndPoint = endPoint;
            return saea.ConnectAsync(_socket).AsTask();
        }
        else
            return Task.FromException<SocketError>(DisposedException());
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private ValueTask<T> DisposedValueTask<T>() => new ValueTask<T>(Task.FromException<T>(DisposedException()));

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
            _socket.Dispose();
        }
    }

    private sealed class AwaitableSocketAsyncEventArgs : SocketAsyncEventArgs, IValueTaskSource<SocketError>, IValueTaskSource<SocketReceiveResult>
    {
        /// <summary>Sentinel object used to indicate that the operation has completed prior to OnCompleted being called.</summary>
        private static readonly Action<object?> s_completedSentinel = new Action<object?>(state => throw new Exception(nameof(s_completedSentinel)));
        /// <summary>Sentinel object used to indicate that the instance is available for use.</summary>
        private static readonly Action<object?> s_availableSentinel = new Action<object?>(state => throw new Exception(nameof(s_availableSentinel)));
        /// <summary>
        /// <see cref="s_availableSentinel"/> if the object is available for use, after GetResult has been called on a previous use.
        /// null if the operation has not completed.
        /// <see cref="s_completedSentinel"/> if it has completed.
        /// Another delegate if OnCompleted was called before the operation could complete, in which case it's the delegate to invoke
        /// when the operation does complete.
        /// </summary>
        private Action<object?>? _continuation = s_availableSentinel;
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

        internal bool Reserve() => ReferenceEquals(Interlocked.CompareExchange(ref _continuation, null, s_availableSentinel), s_availableSentinel);

        protected override void OnCompleted(SocketAsyncEventArgs _)
        {
            // When the operation completes, see if OnCompleted was already called to hook up a continuation.
            // If it was, invoke the continuation.
            Action<object?>? c = _continuation;
            if (c != null || (c = Interlocked.CompareExchange(ref _continuation, s_completedSentinel, null)) != null)
            {
                Debug.Assert(c != s_availableSentinel, "The delegate should not have been the available sentinel.");
                Debug.Assert(c != s_completedSentinel, "The delegate should not have been the completed sentinel.");

                object continuationState = UserToken;
                UserToken = null;
                _continuation = s_completedSentinel; // in case someone's polling IsCompleted

                ExecutionContext? ec = _executionContext;
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

#if NETSTANDARD2_0 || NET46

#else
        internal ValueTask<SocketReceiveResult> ReceiveAsync(Socket socket, Memory<byte> buffer)
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

        internal ValueTask<SocketError> SendAsync(Socket socket, ReadOnlyMemory<byte> buffer)
        {
            Debug.Assert(Volatile.Read(ref _continuation) == null, $"Expected null continuation to indicate reserved for use");

            // Меняет тип буффера на доступный для записи(!).
            // Но запись ни в коем случае не должна осуществляться.
            // Это безопасно так как SetBuffer будет производить только чтение.
            Memory<byte> memory = MemoryMarshal.AsMemory(buffer);

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
#endif

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

            SocketError error = SocketError;

            Release();

            return new ValueTask<SocketError>(result: error);
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
            Volatile.Write(ref _continuation, s_availableSentinel);
        }

        // Результат отправки.
        // Нельзя выполнять больше одного раза.
        SocketError IValueTaskSource<SocketError>.GetResult(short token)
        {
            if (token != _token)
            {
                ThrowIncorrectTokenException();
            }

            SocketError error = SocketError;

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

            SocketError error = SocketError;
            int bytes = BytesTransferred;

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
                !ReferenceEquals(_continuation, s_completedSentinel) ? ValueTaskSourceStatus.Pending : ValueTaskSourceStatus.Succeeded;
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
                SynchronizationContext? sc = SynchronizationContext.Current;
                if (sc != null && sc.GetType() != typeof(SynchronizationContext))
                {
                    _scheduler = sc;
                }
                else
                {
                    TaskScheduler ts = TaskScheduler.Current;
                    if (ts != TaskScheduler.Default)
                    {
                        _scheduler = ts;
                    }
                }
            }

            UserToken = state; // Use UserToken to carry the continuation state around
            Action<object?>? prevContinuation = Interlocked.CompareExchange(ref _continuation, continuation, null);
            if (ReferenceEquals(prevContinuation, s_completedSentinel))
            {
                // Lost the race condition and the operation has now already completed.
                // We need to invoke the continuation, but it must be asynchronously to
                // avoid a stack dive.  However, since all of the queueing mechanisms flow
                // ExecutionContext, and since we're still in the same context where we
                // captured it, we can just ignore the one we captured.
                bool requiresExecutionContextFlow = _executionContext != null;
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
            object? scheduler = _scheduler;
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

        private void ThrowIncorrectTokenException() =>
            throw new InvalidOperationException("Произошла попытка одновременного выполнения операции чтения или записи на сокете.");

        private void ThrowMultipleContinuationsException() => throw new InvalidOperationException("Multiple continuations not allowed.");
    }

    private static InvalidOperationException SimultaneouslyOperationException()
            => new InvalidOperationException("Operation already in progress.");
}