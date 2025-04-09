using System.Net;
using System.Net.Sockets;

namespace DanilovSoft.Socks5Server;

public sealed class Socks5Listener : IDisposable
{
    private readonly TaskCompletionSource _shutdownTask = new();
    private readonly TcpListener _tcpListener;
    private int _activeConnectionsCount;

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
        try
        {
            while (!ct.IsCancellationRequested)
            {
                TcpClient connectedSocket;
                try
                {
                    connectedSocket = await _tcpListener.AcceptTcpClientAsync(ct).ConfigureAwait(false);
                }
                catch (ObjectDisposedException) when (ct.IsCancellationRequested) // Нормальная остановка с помощью токена.
                {
                    throw new OperationCanceledException($"{nameof(Socks5Listener)} успешно остановлен по запросу пользователя", ct);
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
            // Даём немного времени на завершение всех активных соединений.
            await _shutdownTask.Task.WaitAsync(TimeSpan.FromSeconds(5), CancellationToken.None);
        }
    }

    private async void ProcessConnection(TcpClient connectedSocket, CancellationToken ct)
    {
        using var connection = new Socks5Connection(connectedSocket);
        
        await connection.ProcessRequestsAsync(ct).ConfigureAwait(ConfigureAwaitOptions.SuppressThrowing);

        DecrementConnectionsCount(ct);
    }

    private void DecrementConnectionsCount(CancellationToken ct)
    {
        if (Interlocked.Decrement(ref _activeConnectionsCount) == 1 && ct.IsCancellationRequested)
        {
            _shutdownTask.TrySetResult();
        }
    }
}
