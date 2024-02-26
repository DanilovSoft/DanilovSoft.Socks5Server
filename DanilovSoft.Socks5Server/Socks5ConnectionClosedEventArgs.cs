namespace DanilovSoft.Socks5Server;

public sealed class Socks5ConnectionClosedEventArgs : EventArgs
{
    public string? UserName { get; }

    public Socks5ConnectionClosedEventArgs(string? userName)
    {
        UserName = userName;
    }
}
