using System.Diagnostics;
using System.Net.Sockets;
using System.Runtime.InteropServices;

namespace DanilovSoft.Socks5Server.TcpSocket;

/// <summary>
/// Внимание! Если SocketError = Success, а Count = 0 — это означает что удалённая сторона закрыла соединение.
/// Count может быть больше 0 несмотря на то что SocketError != Success.
/// </summary>
[StructLayout(LayoutKind.Auto)]
[method: DebuggerStepThrough]
[DebuggerDisplay($"{{{nameof(GetDebuggerDisplay)}(),nq}}")]
internal readonly struct SocketReceiveResult(int count, SocketError errorCode)
{
    public readonly int BytesReceived = count;
    public readonly SocketError ErrorCode = errorCode;
    
    /// <summary>
    /// Когда BytesReceived > 0 И ErrorCode.Success.
    /// </summary>
    public bool ReceiveSuccess => BytesReceived > 0 && ErrorCode == SocketError.Success;

    private string GetDebuggerDisplay()
    {
        return $"{{ BytesReceived = {BytesReceived}, ErrorCode = {ErrorCode} }}";
    }
}
