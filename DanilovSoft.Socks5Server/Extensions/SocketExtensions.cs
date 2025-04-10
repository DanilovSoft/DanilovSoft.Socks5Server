using System.Diagnostics;
using System.Net;
using System.Net.Sockets;
using DanilovSoft.Socks5Server.TcpSocket;

namespace DanilovSoft.Socks5Server.Extensions;

internal static class SocketExtensions
{
    /// <summary>
    /// Вместо исключения возвращает код ошибки, когда это возможно.
    /// </summary>
    /// <remarks>Сокращает число исключений</remarks>
    public static ValueTask<SocketReceiveResult> Receive2(this Socket socket, Memory<byte> buffer, CancellationToken ct = default)
    {
        var valueTask = socket.ReceiveAsync(buffer, SocketFlags.None, ct);

        if (valueTask.IsCompletedSuccessfully)
        {
            return ValueTask.FromResult(new SocketReceiveResult(valueTask.Result, SocketError.Success));
        }
        else if (valueTask.IsCompleted)
        {
            return FromFaulted(valueTask.AsTask());
        }

        return Wait(valueTask.AsTask());
        static async ValueTask<SocketReceiveResult> Wait(Task<int> task)
        {
            await ((Task)task).ConfigureAwait(ConfigureAwaitOptions.SuppressThrowing);

            return task.IsCompletedSuccessfully 
                ? new SocketReceiveResult(task.Result, SocketError.Success) 
                : await FromFaulted(task);
        }

        static ValueTask<SocketReceiveResult> FromFaulted(Task faultedTask)
        {
            if (faultedTask.IsFaulted)
            {
                if (faultedTask.Exception.InnerException is SocketException e)
                {
                    return ValueTask.FromResult(new SocketReceiveResult(0, e.SocketErrorCode));
                }
                else
                {
                    return ValueTask.FromException<SocketReceiveResult>(faultedTask.Exception.InnerException!);
                }
            }
            else // IsCanceled
            {
                return ValueTask.FromResult(new SocketReceiveResult(0, SocketError.OperationAborted));
            }
        }
    }

    /// <summary>
    /// Вместо исключения возвращает код ошибки, когда это возможно.
    /// </summary>
    /// <remarks>Сокращает число исключений</remarks>
    public static ValueTask<SocketError> SendAll(this Socket socket, ReadOnlyMemory<byte> buffer, CancellationToken ct = default)
    {
        Debug.Assert(buffer.Length > 0);

        // (!) асинхронная отправка не гарантирует что за один раз отправятся все данные.

        ValueTask<int> task = socket.SendAsync(buffer, SocketFlags.None, ct);

        if (task.IsCompletedSuccessfully)
        {
            return FromSuccess(task.Result, socket, buffer, ct);
        }
        else if (task.IsCompleted)
        {
            return FromFaultedTask(task.AsTask());
        }

        return Wait(task.AsTask(), socket, buffer, ct);
        static async ValueTask<SocketError> Wait(Task<int> task, Socket socket, ReadOnlyMemory<byte> buffer, CancellationToken ct)
        {
            await((Task)task).ConfigureAwait(ConfigureAwaitOptions.SuppressThrowing);

            return task.IsCompletedSuccessfully 
                ? await FromSuccess(task.Result, socket, buffer, ct) 
                : await FromFaultedTask(task);
        }

        static ValueTask<SocketError> FromSuccess(int sent, Socket socket, ReadOnlyMemory<byte> buffer, CancellationToken ct)
        {
            if (sent == buffer.Length)
            {
                return ValueTask.FromResult(SocketError.Success);
            }
            else if (sent == 0)
            {
                // Соединение закрыто удалённой стороной
                return ValueTask.FromResult(SocketError.ConnectionReset);
            }
            else
            {
                buffer = buffer[sent..];
                return SendAll(socket, buffer, ct);
            }
        }
    }

    /// <summary>
    /// Вместо исключения возвращает код ошибки, когда это возможно.
    /// </summary>
    /// <remarks>Сокращает число исключений</remarks>
    public static ValueTask<SocketError> Connect2(this Socket socket, EndPoint remoteEP, CancellationToken ct = default)
    {
        ValueTask task = socket.ConnectAsync(remoteEP, ct);

        if (task.IsCompletedSuccessfully)
        {
            task.GetAwaiter().GetResult(); // to reset inner state
            return ValueTask.FromResult(SocketError.Success);
        }
        else if (task.IsCompleted)
        {
            return FromFaultedTask(task.AsTask());
        }

        return Wait(task.AsTask());
        static async ValueTask<SocketError> Wait(Task task)
        {
            await task.ConfigureAwait(ConfigureAwaitOptions.SuppressThrowing);

            return task.IsCompletedSuccessfully 
                ? SocketError.Success 
                : await FromFaultedTask(task);
        }
    }

    private static ValueTask<SocketError> FromFaultedTask(Task completedTask)
    {
        if (completedTask.IsFaulted)
        {
            if (completedTask.Exception.InnerException is SocketException e)
            {
                return ValueTask.FromResult(e.SocketErrorCode);
            }
            else
            {
                return ValueTask.FromException<SocketError>(completedTask.Exception.InnerException!);
            }
        }
        else // IsCanceled
        {
            return ValueTask.FromResult(SocketError.OperationAborted);
        }
    }
}
