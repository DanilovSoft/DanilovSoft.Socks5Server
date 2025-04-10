using System.Net.Sockets;
using DanilovSoft.Socks5Server.TcpSocket;

namespace DanilovSoft.Socks5Server.Extensions;

internal static class ExtensionMethods
{
    /// <exception cref="SocketException"/>
    public static Exception ToException(this SocketError socketError)
    {
        return new SocketException((int)socketError);
    }

    public static ValueTask<SocketReceiveResult> ReceiveExactAsync(this Socket socket, Memory<byte> buffer, CancellationToken ct = default)
    {
        var count = buffer.Length;
        while (buffer.Length > 0)
        {
            ValueTask<SocketReceiveResult> task = socket.Receive2(buffer, ct);

            if (task.IsCompletedSuccessfully)
            {
                var result = task.Result;

                if (result.BytesReceived > 0 && result.ErrorCode == SocketError.Success)
                {
                    // Уменьшить буфер на столько, сколько приняли.
                    buffer = buffer[result.BytesReceived..];
                }
                else
                {
                    return new ValueTask<SocketReceiveResult>(result);
                }
            }
            else
            {
                return Wait(task, count, socket, buffer, ct);
            }
        }

        // Всё выполнено синхронно.
        return new ValueTask<SocketReceiveResult>(new SocketReceiveResult(count, SocketError.Success));

        static async ValueTask<SocketReceiveResult> Wait(ValueTask<SocketReceiveResult> task, int count, 
            Socket socket, Memory<byte> buffer, CancellationToken ct)
        {
            var result = await task;

            if (result.BytesReceived > 0 && result.ErrorCode == SocketError.Success)
            {
                // Уменьшить буфер на сколько приняли.
                buffer = buffer[result.BytesReceived..];

                if (buffer.Length == 0)
                {
                    return new SocketReceiveResult(count, SocketError.Success);
                }
                else
                // Прочитали всё что необходимо.
                {
                    return await ReceiveExactAsync(socket, buffer, ct);
                }
            }
            else
            {
                return result;
            }
        }
    }
}
