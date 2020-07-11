using System;
using System.Net;
using System.Net.Sockets;
using System.Runtime.InteropServices;

namespace DanilovSoft.Socks5Server
{
    [StructLayout(LayoutKind.Auto)]
    internal readonly struct TcpConnectResult : IDisposable
    {
        public SocketError SocketError { get; }
        public ManagedTcpSocket? Socket { get; }

        public TcpConnectResult(SocketError socketError, ManagedTcpSocket? socket)
        {
            SocketError = socketError;
            Socket = socket;
        }

        public void Dispose()
        {
            Socket?.Dispose();
        }
    }
}