using System.Net;

namespace MLAPI.Puncher.Shared
{
    /// <summary>
    /// Represents the transport protocol where the puncher communicates.
    /// </summary>
    public interface IUDPTransport
    {
        /// <summary>
        /// Sends bytes to endpoint.
        /// </summary>
        /// <returns>The bytes sent. 0 or less if failed.</returns>
        /// <param name="buffer">The buffer to send.</param>
        /// <param name="offset">The offset of the buffer to start sending at.</param>
        /// <param name="length">The length to send from the buffer.</param>
        /// <param name="timeoutMs">The operation timeout in milliseconds.</param>
        /// <param name="endpoint">The endpoint to send to.</param>
        int SendTo(byte[] buffer, int offset, int length, int timeoutMs, IPEndPoint endpoint);
        /// <summary>
        /// Receives bytes from endpoint.
        /// </summary>
        /// <returns>The amount of bytes received. 0 or elss if failed.</returns>
        /// <param name="buffer">The buffer to receive to.</param>
        /// <param name="offset">The offer of the buffer to receive at.</param>
        /// <param name="length">The max length to receive.</param>
        /// <param name="timeoutMs">The operation timeout in milliseconds.</param>
        /// <param name="endpoint">The endpoint the packet came from.</param>
        int ReceiveFrom(byte[] buffer, int offset, int length, int timeoutMs, out IPEndPoint endpoint);
        /// <summary>
        /// Bind transport the specified local endpoint.
        /// </summary>
        /// <param name="endpoint">The local endpoint to bind to.</param>
        void Bind(IPEndPoint endpoint);
        /// <summary>
        /// Closes the transport.
        /// </summary>
        void Close();
    }
}
