using System.Diagnostics;
using System.Diagnostics.CodeAnalysis;
using System.Net.Sockets;
using System.Runtime.InteropServices;

namespace DanilovSoft.Socks5Server.TcpSocket;

[StructLayout(LayoutKind.Auto)]
[DebuggerDisplay($"{{{nameof(GetDebuggerDisplay)}(),nq}}")]
internal readonly struct TcpConnectResult(SocketError socketError, Socket? socket) : IDisposable
{
    public SocketError SocketError { get; } = socketError;
    public Socket? Socket { get; } = socket;

    [MemberNotNullWhen(true, nameof(Socket))]
    public bool IsConnected => Socket != null;

    public void Dispose()
    {
        Socket?.Dispose();
    }

    private string GetDebuggerDisplay()
    {
        return $"{GetType().Name} {{ SocketError = {SocketError} }}";
    }
}
