using System.Net;
using System.Net.Sockets;

namespace DanilovSoft.Socks5Server;

public sealed class Socks5Listener : IDisposable
{
    private readonly TcpListener _tcpListener;
    internal int _connectionsCount;
    internal int _connectionIdSeq;

    public Socks5Listener(int listenPort = 0)
    {
        TcpListener? tcpListener = null;
        try
        {
            tcpListener = new(IPAddress.Any, listenPort);
            tcpListener.Start();
            Port = ((IPEndPoint)tcpListener.LocalEndpoint).Port;
            _tcpListener = Exchange(ref tcpListener, null);
        }
        finally
        {
            tcpListener?.Dispose();
        }
    }

    public int Port { get; }

    public void Dispose()
    {
        _tcpListener.Dispose();
    }

    public async Task ListenAsync(CancellationToken ct = default)
    {
        while (true)
        {
            TcpClient tcpClient;
            try
            {
                tcpClient = await _tcpListener.AcceptTcpClientAsync(ct).ConfigureAwait(false);
            }
            catch (ObjectDisposedException) when (ct.IsCancellationRequested) // Нормальная остановка с помощью токена.
            {
                throw new OperationCanceledException($"{nameof(Socks5Listener)} успешно остановлен по запросу пользователя", ct);
            }

            ThreadPool.UnsafeQueueUserWorkItem(static s => s.ThisRef.ProcessConnection(s.tcpClient, s.ct), state: (ThisRef: this, tcpClient, ct), preferLocal: false);
        }
    }

    private async void ProcessConnection(TcpClient tcp, CancellationToken ct)
    {
        using var connection = new Socks5Connection(tcp, this);
        
        await connection.ProcessRequestsAsync(ct).ConfigureAwait(ConfigureAwaitOptions.SuppressThrowing);
    }
}
