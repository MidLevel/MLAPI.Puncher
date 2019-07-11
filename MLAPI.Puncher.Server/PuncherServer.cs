using System;
using System.Collections.Generic;
using System.Net;
using MLAPI.Puncher.Shared;

namespace MLAPI.Puncher.Server
{
    /// <summary>
    /// A puncher server capable of routing and organizing client punches.
    /// </summary>
    public class PuncherServer
    {
        private readonly byte[] _buffer = new byte[64];
        private readonly byte[] _tokenBuffer = new byte[64];
        private readonly byte[] _ipBuffer = new byte[4];
        // TODO: We never clear dictionary of old records
        private readonly Dictionary<IPAddress, Client> _listenerClients = new Dictionary<IPAddress, Client>();
        /// <summary>
        /// Gets or sets the transport used to communicate with puncher clients.
        /// </summary>
        /// <value>The transport used to communcate with puncher clients.</value>
        public IUDPTransport Transport { get; set; } = new SlimUDPTransport();

        /// <summary>
        /// Start a server bound to the specified endpoint.
        /// </summary>
        /// <param name="endpoint">Endpoint.</param>
        public void Start(IPEndPoint endpoint)
        {
            Transport.Bind(endpoint);

            while (true)
            {
                ProcessMessage();
            }
        }

        private void ProcessMessage()
        {
            int receiveSize = Transport.ReceiveFrom(_buffer, 0, _buffer.Length, -1, out IPEndPoint senderEndpoint);

            // Address
            IPAddress senderAddress = senderEndpoint.Address;

            if (receiveSize != _buffer.Length)
            {
                return;
            }

            if (_buffer[0] != (byte)MessageType.Register)
            {
                return;
            }

            // Register client packet
            byte registerFlags = _buffer[1];
            bool isConnector = (registerFlags & 1) == 1;
            bool isListener = ((registerFlags >> 1) & 1) == 1;

            if (isListener)
            {
                if (_listenerClients.TryGetValue(senderAddress, out Client client))
                {
                    client.EndPoint = senderEndpoint;
                    client.IsConnector = isConnector;
                    client.IsListener = isListener;
                    client.LastRegisterTime = DateTime.Now;
                }
                else
                {
                    _listenerClients.Add(senderAddress, new Client()
                    {
                        EndPoint = senderEndpoint,
                        IsConnector = isConnector,
                        IsListener = isListener,
                        LastRegisterTime = DateTime.Now
                    });
                }
            }
            else
            {
                // Below line breaks when multiple clinets use the same address
                //_listenerClients.Remove(senderAddress);
            }

            if (isConnector)
            {
                // Copy address to address buffer
                Buffer.BlockCopy(_buffer, 2, _ipBuffer, 0, 4);

                // Parse address
                IPAddress listenerAddress = new IPAddress(_ipBuffer);

                // Read token size
                byte tokenSize = _buffer[6];

                // Validate token size
                if (tokenSize > _buffer.Length - 6)
                {
                    // Invalid token size
                    return;
                }

                // Copy token to token buffer
                Buffer.BlockCopy(_buffer, 7, _tokenBuffer, 0, tokenSize);

                // Look for the client they wish to connec tto
                if (_listenerClients.TryGetValue(listenerAddress, out Client listenerClient) && listenerClient.IsListener)
                {
                    // Write message type
                    _buffer[0] = (byte)MessageType.ConnectTo;

                    // Write address
                    Buffer.BlockCopy(listenerClient.EndPoint.Address.GetAddressBytes(), 0, _buffer, 1, 4);

                    // Write port
                    _buffer[5] = (byte)listenerClient.EndPoint.Port;
                    _buffer[6] = (byte)(listenerClient.EndPoint.Port >> 8);

                    // Write token length
                    _buffer[7] = tokenSize;

                    // Write token
                    Buffer.BlockCopy(_tokenBuffer, 0, _buffer, 8, tokenSize);

                    // Send to connector
                    Transport.SendTo(_buffer, 0, _buffer.Length, -1, senderEndpoint);

                    // Write address
                    Buffer.BlockCopy(senderAddress.GetAddressBytes(), 0, _buffer, 1, 4);

                    // Write port
                    _buffer[5] = (byte)senderEndpoint.Port;
                    _buffer[6] = (byte)(senderEndpoint.Port >> 8);

                    // Send to listener
                    Transport.SendTo(_buffer, 0, _buffer.Length, -1, listenerClient.EndPoint);
                }
                else
                {
                    // Prevent info leaks
                    Array.Clear(_buffer, 2, _buffer.Length - 2);

                    _buffer[0] = (byte)MessageType.Error;
                    _buffer[1] = (byte)ErrorType.ClientNotFound;

                    // Send error
                    Transport.SendTo(_buffer, 0, _buffer.Length, -1, senderEndpoint);
                }
            }
        }
    }
}
