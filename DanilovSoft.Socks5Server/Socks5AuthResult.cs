using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace DanilovSoft.Socks5Server
{
    internal readonly struct Socks5AuthResult
    {
        /// <summary>
        /// Фрагмент входного буфера.
        /// </summary>
        public readonly ReadOnlyMemory<byte> BufferSlice;

        public Socks5AuthResult(Memory<byte> outputBuffer, bool allow)
        {
            if (outputBuffer.Length < 2)
                throw new ArgumentOutOfRangeException(nameof(outputBuffer), "Размер буфера должен быть больше или равен 2.");

            outputBuffer.Span[0] = 1;
            outputBuffer.Span[1] = (byte)(allow ? 0 : 1);

            BufferSlice = outputBuffer.Slice(0, 2);
        }
    }
}
