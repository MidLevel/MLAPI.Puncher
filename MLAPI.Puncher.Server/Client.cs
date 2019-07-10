using System;
using System.Net;

namespace MLAPI.Puncher.Server
{
    internal class Client
    {
        public IPEndPoint EndPoint { get; set; }
        public bool IsConnector { get; set; }
        public bool IsListener { get; set; }
        public DateTime LastRegisterTime { get; set; }
    }
}
