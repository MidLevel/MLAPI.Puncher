using System;
using System.Collections.Generic;
using System.Net;
using MLAPI.Puncher.Shared;
using System.Threading;

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

        private readonly ReaderWriterLockSlim _listenerClientsLock = new ReaderWriterLockSlim(LockRecursionPolicy.SupportsRecursion);
        private readonly Dictionary<IPAddress, Client> _listenerClients = new Dictionary<IPAddress, Client>();
        private Thread _cleanupThread;

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

            _cleanupThread = new Thread(() =>
            {
                while (true)
                {
                    _listenerClientsLock.EnterUpgradeableReadLock();

                    try
                    {
                        foreach (Client client in _listenerClients.Values)
                        {
                            // Make them expire after 120 seconds
                            if ((DateTime.Now - client.LastRegisterTime).TotalSeconds > 120)
                            {
                                _listenerClientsLock.EnterWriteLock();

                                try
                                {
                                    _listenerClients.Remove(client.EndPoint.Address);
                                }
                                finally
                                {
                                    _listenerClientsLock.ExitWriteLock();
                                }
                            }
                        }
                    }
                    finally
                    {
                        _listenerClientsLock.ExitUpgradeableReadLock();
                    }

                    // No point in cleaning more than once every 10 seconds
                    Thread.Sleep(10_000);
                }
            });

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
                _listenerClientsLock.EnterUpgradeableReadLock();

                try
                {
                    if (_listenerClients.TryGetValue(senderAddress, out Client client))
                    {
                        _listenerClientsLock.EnterWriteLock();

                        try
                        {
                            client.EndPoint = senderEndpoint;
                            client.IsConnector = isConnector;
                            client.IsListener = isListener;
                            client.LastRegisterTime = DateTime.Now;
                        }
                        finally
                        {
                            _listenerClientsLock.ExitWriteLock();
                        }
                    }
                    else
                    {
                        _listenerClientsLock.EnterWriteLock();

                        try
                        {
                            _listenerClients.Add(senderAddress, new Client()
                            {
                                EndPoint = senderEndpoint,
                                IsConnector = isConnector,
                                IsListener = isListener,
                                LastRegisterTime = DateTime.Now
                            });
                        }
                        finally
                        {
                            _listenerClientsLock.ExitWriteLock();
                        }
                    }
                }
                finally
                {
                    _listenerClientsLock.ExitUpgradeableReadLock();
                }
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

                _listenerClientsLock.EnterReadLock();

                try
                {
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
                finally
                {
                    _listenerClientsLock.ExitReadLock();
                }
            }
        }
    }
}
