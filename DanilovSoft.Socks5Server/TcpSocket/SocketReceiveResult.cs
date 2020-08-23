using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Net.Sockets;
using System.Runtime.InteropServices;
using System.Text;
using System.Threading.Tasks;

namespace System.Net
{
    /// <summary>
    /// Внимание! Если SocketError = Success, а Count = 0 — это означает что удалённая сторона закрыла соединение.
    /// Count может быть больше 0 несмотря на то что SocketError != Success.
    /// </summary>
    [StructLayout(LayoutKind.Auto)]
    [DebuggerDisplay(@"\{BytesReceived = {BytesReceived}, ErrorCode = {ErrorCode}\}")]
    internal readonly struct SocketReceiveResult
    {
        public readonly int BytesReceived;
        public readonly SocketError ErrorCode;
        /// <summary>
        /// Когда BytesReceived > 0 И ErrorCode.Success.
        /// </summary>
        public bool ReceiveSuccess => BytesReceived > 0 && ErrorCode == SocketError.Success;

        [DebuggerStepThrough]
        public SocketReceiveResult(int count, SocketError errorCode)
        {
            BytesReceived = count;
            ErrorCode = errorCode;
        }
    }
}