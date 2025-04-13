using System.Buffers;

namespace DanilovSoft.Socks5Server;

internal static class MyArrayPool
{
    public static ArrayPool<byte> Shared { get; } = ArrayPool<byte>.Create(maxArrayLength: 4096, 2);
}
