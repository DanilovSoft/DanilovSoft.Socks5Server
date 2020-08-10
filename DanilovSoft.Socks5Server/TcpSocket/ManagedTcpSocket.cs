using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Diagnostics.CodeAnalysis;
using System.Linq;
using System.Net.Sockets;
using System.Runtime.CompilerServices;
using System.Runtime.ExceptionServices;
using System.Runtime.InteropServices;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using System.Threading.Tasks.Sources;
using DanilovSoft.Socks5Server;

namespace System.Net
{
    internal sealed class ManagedTcpSocket : IDisposable
    {
        private readonly AwaitableSocketAsyncEventArgs _receiveArgs;
        private readonly AwaitableSocketAsyncEventArgs _sendArgs;
        private readonly Socket _socket;
        public Socket Client => _socket;
        private int _disposed;

        public ManagedTcpSocket(Socket socket)
        {
            _socket = socket;
            _receiveArgs = new AwaitableSocketAsyncEventArgs();
            _sendArgs = new AwaitableSocketAsyncEventArgs();
        }

        public ValueTask<SocketReceiveResult> ReceiveAsync(Memory<byte> buffer, CancellationToken cancellationToken = default)
        {
            ThrowIfDisposed();

            if (!cancellationToken.IsCancellationRequested)
            {
                if (_receiveArgs.Reserve())
                {
                    return _receiveArgs.ReceiveAsync(_socket, buffer, cancellationToken);
                }
                else
                    return new ValueTask<SocketReceiveResult>(task: Task.FromException<SocketReceiveResult>(SimultaneouslyOperationException()));
            }
            else
                return new ValueTask<SocketReceiveResult>(Task.FromCanceled<SocketReceiveResult>(cancellationToken));
        }

        public ValueTask<SocketError> SendAsync(ReadOnlyMemory<byte> buffer, CancellationToken cancellationToken = default)
        {
            ThrowIfDisposed();

            if (!cancellationToken.IsCancellationRequested)
            {
                if (_sendArgs.Reserve())
                {
                    return _sendArgs.SendAsync(_socket, buffer, cancellationToken);
                }
                else
                    return new ValueTask<SocketError>(task: Task.FromException<SocketError>(SimultaneouslyOperationException()));
            }
            else
                return new ValueTask<SocketError>(Task.FromCanceled<SocketError>(cancellationToken));
        }

        public ValueTask<SocketReceiveResult> ReceiveAsync(byte[] buffer, int offset, int count, CancellationToken cancellationToken = default)
        {
            ThrowIfDisposed();
            return InnerReceiveAsync(buffer, offset, count, cancellationToken);
        }

        /// <exception cref="SocketException"/>
        public ValueTask WriteAsync(ReadOnlyMemory<byte> buffer, CancellationToken cancellationToken = default)
        {
            ThrowIfDisposed();

            ValueTask<SocketError> t = SendAsync(buffer, cancellationToken);
            if (t.IsCompletedSuccessfully)
            {
                SocketError socErr = t.Result;

                if (socErr == SocketError.Success)
                    return new ValueTask();

                return new ValueTask(Task.FromException(socErr.ToException()));
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

                ThrowHelper.ThrowException(socErr.ToException());
            }
        }

        /// <exception cref="SocketException"/>
        public ValueTask WriteAsync(byte[] buffer, int offset, int count, CancellationToken cancellationToken = default)
        {
            ThrowIfDisposed();

            ValueTask<SocketError> t = SendAsync(buffer, offset, count, cancellationToken);
            if (t.IsCompletedSuccessfully)
            {
                var socErr = t.Result;
                if (socErr == SocketError.Success)
                    return new ValueTask();

                return new ValueTask(Task.FromException(socErr.ToException()));
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
        public ValueTask<int> ReadAsync(byte[] buffer, int offset, int count, CancellationToken cancellationToken = default)
        {
            ThrowIfDisposed();

            ValueTask<SocketReceiveResult> t = InnerReceiveAsync(buffer, offset, count, cancellationToken);
            if (t.IsCompletedSuccessfully)
            {
                SocketReceiveResult result = t.Result;
                if (result.SocketError == SocketError.Success)
                    return new ValueTask<int>(result.Count);

                return new ValueTask<int>(Task.FromException<int>(result.SocketError.ToException()));
            }
            else
            {
                return WaitForReadAsync(t);
            }

            static async ValueTask<int> WaitForReadAsync(ValueTask<SocketReceiveResult> t)
            {
                SocketReceiveResult result = await t.ConfigureAwait(false);
                if (result.SocketError == SocketError.Success)
                    return result.Count;

                throw result.SocketError.ToException();
            }
        }

        public ValueTask<SocketError> SendAsync(byte[] buffer, int offset, int count, CancellationToken cancellationToken = default)
        {
            ThrowIfDisposed();

            if (!cancellationToken.IsCancellationRequested)
            {
                if (_sendArgs.Reserve())
                {
                    return _sendArgs.SendAsync(_socket, buffer, offset, count, cancellationToken);
                }
                else
                    return new ValueTask<SocketError>(task: Task.FromException<SocketError>(SimultaneouslyOperationException()));
            }
            else
                return new ValueTask<SocketError>(Task.FromCanceled<SocketError>(cancellationToken));
        }

        private ValueTask<SocketReceiveResult> InnerReceiveAsync(byte[] buffer, int offset, int count, CancellationToken cancellationToken = default)
        {
            if (!cancellationToken.IsCancellationRequested)
            {
                if (_receiveArgs.Reserve())
                {
                    return _receiveArgs.ReceiveAsync(_socket, buffer, offset, count, cancellationToken);
                }
                else
                    return new ValueTask<SocketReceiveResult>(task: Task.FromException<SocketReceiveResult>(SimultaneouslyOperationException()));
            }
            else
                return new ValueTask<SocketReceiveResult>(Task.FromCanceled<SocketReceiveResult>(cancellationToken));
        }

        /// <exception cref="ObjectDisposedException"/>
        public Task<SocketError> ConnectAsync(EndPoint endPoint)
        {
            ThrowIfDisposed();

            // Можем использовать слот чтения или отправки.
            AwaitableSocketAsyncEventArgs saea = _receiveArgs;

            // Слот должен быть свободен потому что подключение это самая первая операция (только для TCP).
            if (!saea.Reserve())
            {
                Debug.Assert(false);
                return Task.FromException<SocketError>(SimultaneouslyOperationException());
                //saea = new AwaitableSocketAsyncEventArgs();
                //saea.Reserve();
            }

            saea.RemoteEndPoint = endPoint;
            return saea.ConnectAsync(_socket).AsTask();
        }

        private void ThrowIfDisposed()
        {
            if (_disposed == 0)
            {
                return;
            }
            else
            {
                ThrowHelper.ThrowObjectDisposedException(GetType().FullName);
            }
        }

        public void Dispose()
        {
            if (Interlocked.CompareExchange(ref _disposed, 1, 0) == 0)
            {
                //Disposed?.Invoke(this, EventArgs.Empty);
                _receiveArgs.Dispose();
                _sendArgs.Dispose();
            }
        }

        // https://source.dot.net/#System.Net.Sockets/System/Net/Sockets/Socket.Tasks.cs,2bb049b54914ceee
        private sealed class AwaitableSocketAsyncEventArgs : SocketAsyncEventArgs, IValueTaskSource<SocketError>, IValueTaskSource<SocketReceiveResult>
        {
            /// <summary>Sentinel object used to indicate that the operation has completed prior to OnCompleted being called.</summary>
            private static readonly Action<object?> s_completedSentinel = new Action<object?>(state => throw new Exception(nameof(s_completedSentinel)));
            /// <summary>Sentinel object used to indicate that the instance is available for use.</summary>
            private static readonly Action<object?> s_availableSentinel = new Action<object?>(state => throw new Exception(nameof(s_availableSentinel)));
            /// <summary>
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
            /// <summary>Current token value given to a ValueTask and then verified against the value it passes back to us.</summary>
            /// <remarks>
            /// This is not meant to be a completely reliable mechanism, doesn't require additional synchronization, etc.
            /// It's purely a best effort attempt to catch misuse, including awaiting for a value task twice and after
            /// it's already being reused by someone else.
            /// </remarks>
            private short _token;

            public AwaitableSocketAsyncEventArgs()
            {

            }

            public bool Reserve() =>
                ReferenceEquals(Interlocked.CompareExchange(ref _continuation, null, s_availableSentinel), s_availableSentinel);

            private void Release()
            {
                _token++;
                Volatile.Write(ref _continuation, s_availableSentinel);
            }

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
                            var t = (Tuple<AwaitableSocketAsyncEventArgs, Action<object?>, object>)runState!;
                            t.Item1.InvokeContinuation(t.Item2, t.Item3, forceAsync: false, requiresExecutionContextFlow: false);
                        }, Tuple.Create(this, c, continuationState));
                    }
                }
            }

            internal ValueTask<SocketReceiveResult> ReceiveAsync(Socket socket, Memory<byte> buffer, CancellationToken cancellationToken)
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

            internal ValueTask<SocketError> SendAsync(Socket socket, ReadOnlyMemory<byte> buffer, CancellationToken cancellationToken = default)
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

            internal ValueTask<SocketError> SendAsync(Socket socket, byte[] buffer, int offset, int count, CancellationToken cancellationToken)
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

            internal ValueTask<SocketReceiveResult> ReceiveAsync(Socket socket, byte[] buffer, int offset, int count, CancellationToken cancellationToken)
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

            /// <exception cref="Exception"/>
            public ValueTask<SocketError> ConnectAsync(Socket socket)
            {
                Debug.Assert(Volatile.Read(ref _continuation) == null, $"Expected null continuation to indicate reserved for use");

                try
                {
                    // Tcp всегда завершается асинхронно.
                    if (socket.ConnectAsync(this))
                    {
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

            // Результат отправки.
            // Нельзя выполнять больше одного раза.
            SocketError IValueTaskSource<SocketError>.GetResult(short token)
            {
                if (token != _token)
                {
                    ThrowIncorrectTokenException();
                }

                // Результат нужно взять перед Release.
                SocketError error = SocketError;

                Release();

                return error;
            }

            public ValueTaskSourceStatus GetStatus(short token)
            {
                if (token != _token)
                {
                    ThrowIncorrectTokenException();
                }

                // Так как мы сами не провоцируем исключения, а возвращаем коды ошибок, 
                // то таск никогда не будет в статусе Faulted.
                return
                    ReferenceEquals(_continuation, s_completedSentinel) ? ValueTaskSourceStatus.Succeeded : ValueTaskSourceStatus.Pending;
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

            // Результат приёма.
            // Нельзя выполнять больше одного раза.
            public SocketReceiveResult GetResult(short token)
            {
                if (token != _token)
                {
                    ThrowIncorrectTokenException();
                }

                // Результат нужно взять перед Release.
                SocketError error = SocketError;
                int bytes = BytesTransferred;

                Release();

                // Мы не провоцируем исключения.
                //if (error != SocketError.Success)
                //{
                //    ThrowException(error, cancellationToken);
                //}

                return new SocketReceiveResult(bytes, error);
            }

            private static void ThrowIncorrectTokenException() =>
                throw new InvalidOperationException("Произошла попытка одновременного выполнения операции чтения или записи на сокете.");

            private static void ThrowMultipleContinuationsException()
                => throw new InvalidOperationException("Multiple continuations not allowed.");
        }

        private static InvalidOperationException SimultaneouslyOperationException()
                => new InvalidOperationException("Operation already in progress.");
    }
}