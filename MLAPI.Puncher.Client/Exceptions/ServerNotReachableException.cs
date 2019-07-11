using System;

namespace MLAPI.Puncher.Client.Exceptions
{
    public class ServerNotReachableException : Exception
    {
        public ServerNotReachableException()
        {
        }

        public ServerNotReachableException(string message) : base(message)
        {
        }
    }
}
