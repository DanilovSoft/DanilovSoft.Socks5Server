using System;
using System.Net;
using System.Net.Sockets;
using System.Threading;
using System.Threading.Tasks;
using DanilovSoft.Socks5Server;

namespace ConsoleAppTest
{
    class Program
    {
        static async Task Main(string[] args)
        {
            TcpListener listener = new(IPAddress.Any, 1234);
            listener.Start();

            TcpClient cli1 = new("127.0.0.1", 1234);
            var mCli1 = new ManagedTcpSocket(cli1.Client);
            TcpClient cli2 = listener.AcceptTcpClient();
            var mCli2 = new ManagedTcpSocket(cli2.Client);

            ThreadPool.QueueUserWorkItem(delegate
            {
                var buf = new byte[] { 1, 2, 3 };
                Thread.Sleep(2000);
                mCli2.Client.Shutdown(SocketShutdown.Send);

                //while (true)
                //{
                //    var rcv = await mCli2.ReceiveAsync(buf);
                //}
            });

            Thread.Sleep(500);

            var rcv = await mCli2.ReceiveAsync(new byte[10]);

            while (true)
            {
                var snd = await mCli1.SendAsync(new byte[] { 1, 2, 3 });
                Thread.Sleep(500);
            }

            Thread.Sleep(-1);
        }
    }
}
