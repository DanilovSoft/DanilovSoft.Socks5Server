using System.Net;
using System.Runtime.InteropServices;

namespace DanilovSoft.Socks5Server;

[StructLayout(LayoutKind.Auto)]
internal readonly struct Socks5Response
{
    private readonly ResponseCode _code;
    private readonly AddressType _addressType;
    private readonly IPAddress _ipaddress;

    public Socks5Response(ResponseCode code, IPAddress ipaddress)
    {
        if (ipaddress == null)
        {
            ThrowHelper.ThrowArgumentNullException(nameof(ipaddress));
        }

        _code = code;
        _ipaddress = ipaddress;
        _addressType = ipaddress.AddressFamily == System.Net.Sockets.AddressFamily.InterNetwork ? AddressType.IPv4 : AddressType.IPv6;
    }

    public Memory<byte> Write(Memory<byte> buffer)
    {
        buffer.Span[0] = 5;
        buffer.Span[1] = (byte)_code;
        buffer.Span[2] = 0;
        buffer.Span[3] = (byte)_addressType;

        var addressSpan = buffer.Slice(4);

        var addressLen = _addressType switch
        {
            AddressType.IPv4 => 4,
            AddressType.IPv6 => 16,
            _ => throw new NotSupportedException(),
        };
        
        var address = _ipaddress.GetAddressBytes();
        address.CopyTo(addressSpan);

        return buffer.Slice(0, 6 + addressLen);
    }
}
