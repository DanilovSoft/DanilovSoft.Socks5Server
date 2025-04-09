using System.Net.Sockets;
using DanilovSoft.Socks5Server.TcpSocket;

internal static class ExtensionMethods
{
    /// <exception cref="SocketException"/>
    public static Exception ToException(this SocketError socketError)
    {
        return new SocketException((int)socketError);
    }

    public static ValueTask<SocketReceiveResult> ReceiveExactAsync(this ManagedTcpSocket managedTcp, Memory<byte> buffer, CancellationToken cancellationToken = default)
    {
        var count = buffer.Length;
        while (buffer.Length > 0)
        {
            var task = managedTcp.Receive(buffer, cancellationToken);
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
                return WaitForReceiveBlockAsync(task, count, managedTcp, buffer, cancellationToken);
            }
        }

        // Всё выполнено синхронно.
        return new ValueTask<SocketReceiveResult>(new SocketReceiveResult(count, SocketError.Success));

        static async ValueTask<SocketReceiveResult> WaitForReceiveBlockAsync(ValueTask<SocketReceiveResult> task, int count, 
            ManagedTcpSocket managedTcp, Memory<byte> buffer, CancellationToken ct)
        {
            var result = await task.ConfigureAwait(false);

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
                    return await ReceiveExactAsync(managedTcp, buffer, ct).ConfigureAwait(false);
                }
            }
            else
            {
                return result;
            }
        }
    }
}
