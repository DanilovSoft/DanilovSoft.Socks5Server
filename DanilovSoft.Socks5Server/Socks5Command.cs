namespace DanilovSoft.Socks5Server;

internal enum Socks5Command
{
    Unknown = 0,
    Connect = 0x01,
    Binding = 0x02,
    UDPAssociate = 0x03
}
