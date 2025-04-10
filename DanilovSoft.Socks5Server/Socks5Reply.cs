using System.Diagnostics;
using System.Net;
using System.Net.Sockets;
using System.Runtime.InteropServices;

namespace DanilovSoft.Socks5Server;

/// <summary>
/// Пример для IPv6 ответа
/// +----+-----+-------+------+----------+----------+
/// |VER | REP |  RSV  | ATYP | BND.ADDR | BND.PORT |
/// +----+-----+-------+------+----------+----------+
/// | 1  |  1  |   1   |  1   |   16     |    2     |
/// +----+-----+-------+------+----------+----------+
/// </summary>
/// <remarks>https://datatracker.ietf.org/doc/html/rfc1928#section-6</remarks>
[StructLayout(LayoutKind.Auto)]
[DebuggerDisplay($"{{{nameof(GetDebuggerDisplay)}(),nq}}")]
internal readonly struct Socks5Reply(ResponseCode code, IPEndPoint boundEndPoint)
{
    private readonly AddressType _addressType = boundEndPoint.AddressFamily == AddressFamily.InterNetwork ? AddressType.IPv4 : AddressType.IPv6;

    public readonly int WriteTo(Span<byte> span)
    {
        span[0] = 5; // VER: 0x05 — версия SOCKS
        span[1] = (byte)code; // REP: 0x00 — success
        span[2] = 0; // RSV: 0x00 — зарезервировано
        span[3] = (byte)_addressType; // ATYP: 0x04 — IPv6

        if (!boundEndPoint.Address.TryWriteBytes(span[4..], out var bytesWritten))
        {
            ThrowHelper.ThrowUnreachable();
        }

        var bndPort = span.Slice(4 + bytesWritten, 2);

        ushort boundPort = (ushort)boundEndPoint.Port;

        // Порт (big-endian)
        bndPort[0] = (byte)(boundPort >> 8);
        bndPort[1] = (byte)(boundPort & 0xFF);

        return 6 + bytesWritten;
    }

    private string GetDebuggerDisplay()
    {
        return $"REP = {code}, ATYP = {_addressType}, BND = {boundEndPoint}";
    }
}
