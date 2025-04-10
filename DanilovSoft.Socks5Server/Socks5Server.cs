using System.Net;
using System.Net.Sockets;

namespace DanilovSoft.Socks5Server;

public sealed class Socks5Server : IDisposable
{
    private readonly TaskCompletionSource _shutdownTask = new();
    private readonly TcpListener _tcpListener;
    private int _activeConnectionsCount;

    public Socks5Server(int listenPort = 0)
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
    public int ActiveConnections => Volatile.Read(ref _activeConnectionsCount);

    public void Dispose()
    {
        _tcpListener.Dispose();
    }

    public async Task<bool> RunAsync(CancellationToken ct = default)
    {
        bool stoppedGracefully = true;
        try
        {
            while (!ct.IsCancellationRequested)
            {
                Socket connectedSocket;
                try
                {
                    connectedSocket = await _tcpListener.AcceptSocketAsync(ct).ConfigureAwait(false);
                }
                catch (OperationCanceledException)
                {
                    break;
                }

                Interlocked.Increment(ref _activeConnectionsCount);

                if (!ThreadPool.UnsafeQueueUserWorkItem(static s => s.ThisRef.ProcessConnection(s.connectedSocket, s.ct), state: (ThisRef: this, connectedSocket, ct), preferLocal: false))
                {
                    DecrementConnectionsCount(ct);
                }
            }
        }
        finally
        {
            if (Volatile.Read(ref _activeConnectionsCount) > 0)
            {
                // Даём немного времени на завершение всех активных соединений.
                var penaltyTask = _shutdownTask.Task.WaitAsync(TimeSpan.FromSeconds(10), CancellationToken.None);
                await penaltyTask.ConfigureAwait(ConfigureAwaitOptions.SuppressThrowing);
                stoppedGracefully = penaltyTask.IsCompletedSuccessfully;
            }
        }

        return stoppedGracefully;
    }

    private async void ProcessConnection(Socket connectedSocket, CancellationToken ct)
    {
        using var connection = new SocksConnection(connectedSocket);

        await connection.ProcessConnection(ct).ConfigureAwait(ConfigureAwaitOptions.SuppressThrowing);

        DecrementConnectionsCount(ct);
    }

    private void DecrementConnectionsCount(CancellationToken ct)
    {
        if (Interlocked.Decrement(ref _activeConnectionsCount) == 0 && ct.IsCancellationRequested)
        {
            _shutdownTask.TrySetResult();
        }
    }
}
