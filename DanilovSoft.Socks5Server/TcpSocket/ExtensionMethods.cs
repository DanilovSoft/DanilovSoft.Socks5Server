using System.Net;
using System.Net.Sockets;

internal static class ExtensionMethods
{
    /// <summary>
    /// 
    /// </summary>
    /// <exception cref="SocketException"/>
    //[DebuggerStepThrough]
    public static Exception ToException(this SocketError socketError)
    {
        return new SocketException((int)socketError);
    }

    public static ValueTask<SocketReceiveResult> ReceiveBlockAsync(this ManagedTcpSocket managedTcp, Memory<byte> buffer, CancellationToken cancellationToken = default)
    {
        var count = buffer.Length;
        while (buffer.Length > 0)
        {
            var t = managedTcp.ReceiveAsync(buffer, cancellationToken);
            if (t.IsCompletedSuccessfully)
            {
                var result = t.Result;

                if (result.BytesReceived > 0 && result.ErrorCode == SocketError.Success)
                {
                    // Уменьшить буфер на столько, сколько приняли.
                    buffer = buffer.Slice(result.BytesReceived);
                }
                else
                {
                    return new ValueTask<SocketReceiveResult>(result);
                }
            }
            else
            {
                return WaitForReceiveBlockAsync(t, count, managedTcp, buffer, cancellationToken);
            }
        }

        // Всё выполнено синхронно.
        return new ValueTask<SocketReceiveResult>(new SocketReceiveResult(count, SocketError.Success));

        static async ValueTask<SocketReceiveResult> WaitForReceiveBlockAsync(ValueTask<SocketReceiveResult> t, int count, 
            ManagedTcpSocket managedTcp, Memory<byte> buffer, CancellationToken cancellationToken)
        {
            var result = await t.ConfigureAwait(false);

            if (result.BytesReceived > 0 && result.ErrorCode == SocketError.Success)
            {
                // Уменьшить буфер на сколько приняли.
                buffer = buffer.Slice(result.BytesReceived);

                if (buffer.Length == 0)
                {
                    return new SocketReceiveResult(count, SocketError.Success);
                }
                else
                // Прочитали всё что необходимо.
                {
                    return await ReceiveBlockAsync(managedTcp, buffer, cancellationToken).ConfigureAwait(false);
                }
            }
            else
            {
                return result;
            }
        }
    }
}
