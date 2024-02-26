using System.Runtime.InteropServices;

namespace DanilovSoft.Socks5Server;

[StructLayout(LayoutKind.Auto)]
internal readonly struct Socks5AuthResponse
{
    /// <summary>
    /// Фрагмент входного буфера.
    /// </summary>
    public readonly ReadOnlyMemory<byte> BufferSlice;

    public Socks5AuthResponse(Memory<byte> outputBuffer, Socks5AuthMethod method)
    {
        if (outputBuffer.Length < 2)
        {
            ThrowHelper.ThrowArgumentOutOfRange(nameof(outputBuffer), "Размер буфера должен быть больше или равен 2.");
        }

        outputBuffer.Span[0] = 0x05; // Версия SOCKS.
        outputBuffer.Span[1] = (byte)method;
        BufferSlice = outputBuffer.Slice(0, 2);
    }
}
