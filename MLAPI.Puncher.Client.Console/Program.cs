using System;
using System.Net;
using System.Threading;
using System.Threading.Tasks;

namespace MLAPI.Puncher.Client.Console
{
    class Program
    {
        public const string PUNCHER_SERVER_HOST = "puncher.midlevel.io";
        public const int PUNCHER_SERVER_PORT = 6776;

        static void Main(string[] args)
        {
            Task listenTask = Task.Factory.StartNew(() =>
            {
                try
                {
                    using (PuncherClient listenPeer = new PuncherClient(PUNCHER_SERVER_HOST, PUNCHER_SERVER_PORT))
                    {
                        System.Console.WriteLine("[LISTENER] Listening for single punch on our port 1234...");
                        IPEndPoint endpoint = listenPeer.ListenForSinglePunch(new IPEndPoint(IPAddress.Any, 1234));
                        System.Console.WriteLine("[LISTENER] Connector: " + endpoint + " punched through our NAT");
                    }
                }
                catch (Exception e)
                {
                    System.Console.WriteLine(e);
                }
            });

            // Wait a bit to make sure the listener has a chance to register.
            Thread.Sleep(1000);

            System.Console.Write("[CONNECTOR] Enter the address of the listener you want to punch: ");
            string address = System.Console.ReadLine();

            using (PuncherClient connectPeer = new PuncherClient(PUNCHER_SERVER_HOST, PUNCHER_SERVER_PORT))
            {
                System.Console.WriteLine("[CONNECTOR] Punching...");

                if (connectPeer.TryPunch(IPAddress.Parse(address), out IPEndPoint connectResult))
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

            // For the plebs
            System.Console.Read();
        }
    }
}
