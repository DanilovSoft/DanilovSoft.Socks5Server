namespace DanilovSoft.Socks5Server;

internal enum Socks5Command
{
    ConnectTcp = 0x01,
    BindingTcpPort = 0x02,
    AssocUdp = 0x03,
}
