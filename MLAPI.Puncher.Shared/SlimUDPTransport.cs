using System.Net;
using System.Net.Sockets;

namespace MLAPI.Puncher.Shared
{
    /// <summary>
    /// Default UDP transport implementation
    /// </summary>
    public class SlimUDPTransport : IUDPTransport
    {
        private readonly Socket _socket = new Socket(AddressFamily.InterNetwork, SocketType.Dgram, ProtocolType.Udp);

        /// <summary>
        /// Binds the UDP socket to the specified local endpoint.
        /// </summary>
        /// <param name="endpoint">The local endpoint to bind to.</param>
        public void Bind(IPEndPoint endpoint)
        {
            _socket.Bind(endpoint);
        }

        /// <summary>
        /// Receives bytes from endpoint.
        /// </summary>
        /// <returns>The amount of bytes received. 0 or elss if failed.</returns>
        /// <param name="buffer">The buffer to receive to.</param>
        /// <param name="offset">The offer of the buffer to receive at.</param>
        /// <param name="length">The max length to receive.</param>
        /// <param name="timeoutMs">The operation timeout in milliseconds.</param>
        /// <param name="endpoint">The endpoint the packet came from.</param>
        public int ReceiveFrom(byte[] buffer, int offset, int length, int timeoutMs, out IPEndPoint endpoint)
        {
            _socket.ReceiveTimeout = timeoutMs;

            try
            {
                EndPoint inEndpoint = new IPEndPoint(IPAddress.Any, 0);
                int size = _socket.ReceiveFrom(buffer, offset, length, SocketFlags.None, ref inEndpoint);
                endpoint = (IPEndPoint)inEndpoint;

                return size;
            }
            catch (SocketException e)
            {
                if (e.SocketErrorCode == SocketError.TimedOut)
                {
                    endpoint = null;
                    return -1;
                }
                else
                {
                    throw e;
                }
            }
        }

        /// <summary>
        /// Sends bytes to endpoint.
        /// </summary>
        /// <returns>The bytes sent. 0 or less if failed.</returns>
        /// <param name="buffer">The buffer to send.</param>
        /// <param name="offset">The offset of the buffer to start sending at.</param>
        /// <param name="length">The length to send from the buffer.</param>
        /// <param name="timeoutMs">The operation timeout in milliseconds.</param>
        /// <param name="endpoint">The endpoint to send to.</param>
        public int SendTo(byte[] buffer, int offset, int length, int timeoutMs, IPEndPoint endpoint)
        {
            _socket.SendTimeout = timeoutMs;

            try
            {
                int size = _socket.SendTo(buffer, offset, length, SocketFlags.None, endpoint);
                return size;
            }
            catch (SocketException e)
            {
                if (e.SocketErrorCode == SocketError.TimedOut)
                {
                    return -1;
                }
                else
                {
                    throw e;
                }
            }
        }
    }
}
