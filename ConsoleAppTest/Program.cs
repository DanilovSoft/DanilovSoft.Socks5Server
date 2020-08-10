using System;
using System.Net;
using System.Net.Sockets;
using System.Threading;
using DanilovSoft.Socks5Server;

namespace ConsoleAppTest
{
    class Program
    {
        static void Main(string[] args)
        {
            TcpListener listener = new TcpListener(IPAddress.Any, 1234);
            listener.Start();

            TcpClient cli1 = new TcpClient("127.0.0.1", 1234);
            var mCli1 = new ManagedTcpSocket(cli1.Client);
            TcpClient cli2 = listener.AcceptTcpClient();
            var mCli2 = new ManagedTcpSocket(cli2.Client);

            ThreadPool.QueueUserWorkItem(delegate 
            {
                var buf = new byte[] { 1, 2, 3 };
                while (true)
                {
                    //Thread.Sleep(4000);

                    //SocketError result = mCli1.SendAsync(buf).AsTask().Result;
                    //if (result != SocketError.Success)
                    //{
                    //    break;
                    //}

                    Thread.Sleep(500);
                }
            });

            Thread.Sleep(2000);

            byte[] buf = new byte[1024];
            var r = mCli1.SendAsync(buf).AsTask().Result;
            var res = mCli2.ReceiveAsync(buf).AsTask().GetAwaiter().GetResult();
            cli2.Client.Shutdown(SocketShutdown.Receive);
            var res2 = mCli2.ReceiveAsync(buf).AsTask().GetAwaiter().GetResult();

            //cli2.Client.Shutdown(SocketShutdown.Send);

            //cli2.LingerState = new LingerOption(enable: true, seconds: 0); // Альтернативный способ + Close без таймаута.

            //cli2.Client.Close(timeout: 0); // Ноль спровоцирует команду RST и удалённая сторона получит обрыв.

            Thread.Sleep(-1);
        }
    }
}
