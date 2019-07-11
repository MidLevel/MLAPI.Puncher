using System;

namespace MLAPI.Puncher.Client.Exceptions
{
    public class SocketSendException : Exception
    {
        public SocketSendException()
        {
        }

        public SocketSendException(string message) : base(message)
        {
        }
    }
}
