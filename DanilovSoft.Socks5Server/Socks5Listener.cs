using System.Net;
using System.Net.Sockets;

namespace DanilovSoft.Socks5Server;

public sealed class Socks5Listener : IDisposable
{
    private readonly TcpListener _tcpListener;
    internal int _connectionsCount;
    internal int _connectionIdSeq;
    public int Port { get; }

    public Socks5Listener(int listenPort)
    {
        _tcpListener = new TcpListener(IPAddress.Any, listenPort);
        _tcpListener.Start();
        Port = ((IPEndPoint)_tcpListener.LocalEndpoint).Port;
    }

    public void Dispose() => _tcpListener.Stop();

    public async Task ListenAsync(CancellationToken cancellationToken = default)
    {
        using var _ = cancellationToken.Register(s => ((IDisposable)s!).Dispose(), this);

        while (true)
        {
            TcpClient tcp;
            try
            {
                tcp = await _tcpListener.AcceptTcpClientAsync(cancellationToken).ConfigureAwait(false);
            }
            catch (ObjectDisposedException) when (cancellationToken.IsCancellationRequested)
            {
                // Нормальная остановка с помощью токена.
                throw new OperationCanceledException($"{nameof(Socks5Listener)} успешно остановлен по запросу пользователя", cancellationToken);
            }
            ThreadPool.UnsafeQueueUserWorkItem(ProcessConnection, tcp, preferLocal: false);
        }
    }

    private async void ProcessConnection(TcpClient tcp)
    {
        using var connection = new Socks5Connection(tcp, this);
        //connection.ConnectionOpened += Connection_ConnectionOpened;
        //connection.ConnectionClosed += Connection_ConnectionClosed;

        await connection.ProcessRequestsAsync().ConfigureAwait(false);

        //connection.ConnectionOpened -= Connection_ConnectionOpened;
        //connection.ConnectionClosed -= Connection_ConnectionClosed;
    }
}
