using System;
using System.Net;
using System.Threading;
using System.Threading.Tasks;

namespace MLAPI.Puncher.Client.Console
{
    class Program
    {
        static void Main(string[] args)
        {
            Task listenTask = Task.Factory.StartNew(() =>
            {
                try
                {
                    PuncherClient listenPeer = new PuncherClient(new IPEndPoint(IPAddress.Parse("127.0.0.1"), 6776));
                    System.Console.WriteLine("[LISTENER] Listening for single punch...");
                    IPEndPoint endpoint = listenPeer.ListenForSinglePunch(new IPEndPoint(IPAddress.Any, 1234));
                    System.Console.WriteLine("[LISTENER] Connector: " + endpoint + " punched through our NAT");
                }
                catch (Exception e)
                {
                    System.Console.WriteLine(e);
                }
            });

            // Wait a bit to make sure the listener has a chance to register.
            Thread.Sleep(1000);

            PuncherClient connectPeer = new PuncherClient(new IPEndPoint(IPAddress.Parse("127.0.0.1"), 6776));
            System.Console.WriteLine("[CONNECTOR] Punching...");
            IPEndPoint connectResult = connectPeer.Punch(IPAddress.Parse("127.0.0.1"));

            if (connectResult != null)
            {
                System.Console.WriteLine("[CONNECTOR] Punched through to peer: " + connectResult);
            }
            else
            {
                System.Console.WriteLine("[CONNECTOR] Failed to punch");
            }

            // Prevent application from exiting before listener has ended
            listenTask.Wait();
        }
    }
}
