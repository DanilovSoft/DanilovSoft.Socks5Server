using System.Diagnostics;
using System.Diagnostics.CodeAnalysis;
using System.Net;
using System.Net.Sockets;
using System.Runtime.InteropServices;
using System.Text;

namespace DanilovSoft.Socks5Server;

/// <summary>
/// Максимальный размер 262 байт.
/// +----+-----+-------+------+----------+----------+
/// |VER | CMD |  RSV  | ATYP | DST.ADDR | DST.PORT |
/// +----+-----+-------+------+----------+----------+
/// | 1  |  1  | X'00' |  1   | Variable |    2     |
/// +----+-----+-------+------+----------+----------+
/// </summary>
[StructLayout(LayoutKind.Auto)]
[DebuggerDisplay($"{{{nameof(GetDebuggerDisplay)}(),nq}}")]
internal readonly struct SocksRequest(Socks5Command command, AddressType addressType, IPAddress? destinationAddress, string? domainName, ushort destinationPort)
{
    /// <summary>
    /// Поддерживается только ConnectTcp.
    /// </summary>
    public Socks5Command Command { get; } = command;
    public AddressType AddressType { get; } = addressType;
    public IPAddress? DestinationAddress { get; } = destinationAddress;
    public string? DomainName { get; } = domainName;
    public ushort DestinationPort { get; } = destinationPort;

    public bool IsEmpty => (DestinationAddress, DomainName) == default;

    private string GetDebuggerDisplay()
    {
        if (IsEmpty)
        {
            return "{Empty}";
        }

        return $"CMD = {Command}, DST = {DestinationAddress?.ToString() ?? DomainName}:{DestinationPort}";
    }
}
