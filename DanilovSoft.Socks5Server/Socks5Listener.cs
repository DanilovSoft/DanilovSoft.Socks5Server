using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Net;
using System.Net.Sockets;
using System.Text;
using System.Threading;
using System.Threading.Tasks;

namespace DanilovSoft.Socks5Server
{
    public sealed class Socks5Listener : IDisposable
    {
        private readonly TcpListener _tcpListener;
        //public event EventHandler<Socks5ConnectionOpenedEventArgs>? ConnectionOpened;
        //public event EventHandler<Socks5ConnectionClosedEventArgs>? ConnectionClosed;
        internal int _connectionsCount;
        public int ConnectionsCount => Volatile.Read(ref _connectionsCount);
        internal int _connectionIdSeq;
        public int Port { get; }

        public Socks5Listener(int listenPort)
        {
            _tcpListener = new TcpListener(IPAddress.Any, listenPort);
            _tcpListener.Start();
            Port = ((IPEndPoint)_tcpListener.LocalEndpoint).Port;
        }

        /// <summary>
        /// 
        /// </summary>
        /// <exception cref="OperationCanceledException"/>
        public async Task ListenAsync(CancellationToken cancellationToken)
        {
            using (cancellationToken.Register(s => ((IDisposable)s!).Dispose(), this, false))
            {
                while (true)
                {
                    TcpClient tcp;
                    try
                    {
                        tcp = await _tcpListener.AcceptTcpClientAsync().ConfigureAwait(false);
                    }
                    catch (ObjectDisposedException) when (cancellationToken.IsCancellationRequested)
                    {
                        // Нормальная остановка с помощью токена.
                        throw new OperationCanceledException($"{nameof(Socks5Listener)} успешно остановлен по запросу пользователя", cancellationToken);
                    }
                    ThreadPool.UnsafeQueueUserWorkItem(ProcessConnection, tcp, preferLocal: false);
                }
            }
        }

        private async void ProcessConnection(TcpClient tcp)
        {
            using (var connection = new Socks5Connection(tcp, this))
            {
                //connection.ConnectionOpened += Connection_ConnectionOpened;
                //connection.ConnectionClosed += Connection_ConnectionClosed;

                await connection.ProcessRequestsAsync().ConfigureAwait(false);

                //connection.ConnectionOpened -= Connection_ConnectionOpened;
                //connection.ConnectionClosed -= Connection_ConnectionClosed;
            }
        }

        public void Dispose()
        {
            _tcpListener.Stop();

            //ConnectionOpened = null;
            //ConnectionClosed = null;
        }
    }
}
