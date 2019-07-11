using System;
using System.Collections.Generic;
using System.Linq;
using System.Net;
using System.Net.Sockets;
using MLAPI.Puncher.Client.Exceptions;
using MLAPI.Puncher.Shared;

namespace MLAPI.Puncher.Client
{
    /// <summary>
    /// Delegate used on the listener to inform about a connector that punched our NAT.
    /// </summary>
    public delegate void OnConnectorPunchSuccessfulDelegate(IPEndPoint endpoint);

    /// <summary>
    /// A puncher client capable of pucnhing and being punched.
    /// </summary>
    public class PuncherClient : IDisposable
    {
        /// <summary>
        /// Gets or sets the transport used to communicate with puncher server.
        /// </summary>
        /// <value>The transport used to communcate with puncher server.</value>
        public IUDPTransport Transport { get; set; } = new SlimUDPTransport();
        /// <summary>
        /// Gets or sets the amount port predictions to attempt.
        /// </summary>
        /// <value>The amount of port predictions to attempt.</value>
        public int PortPredictions { get; set; } = 12;
        /// <summary>
        /// Gets or sets the max punch response wait time in milliseconds.
        /// </summary>
        /// <value>The max punch response wait time in milliseconds.</value>
        public int PunchResponseTimeout { get; set; } = 8000;
        /// <summary>
        /// Gets or sets the server register response timeout.
        /// If the Puncher server does not respond within this time the connection times out in milliseconds.
        /// </summary>
        /// <value>The server register response timeout in milliseconds.</value>
        public int ServerRegisterResponseTimeout { get; set; } = 8000;
        /// <summary>
        /// Gets or sets the interval of register requests sent to the server in milliseconds.
        /// </summary>
        /// <value>The server register interval in milliseconds.</value>
        public int ServerRegisterInterval { get; set; } = 60_000;
        /// <summary>
        /// Gets or sets the socket send timeout in milliseconds.
        /// </summary>
        /// <value>The socket send timeout in milliseconds.</value>
        public int SocketSendTimeout { get; set; } = 500;
        /// <summary>
        /// Gets or sets the socket receive timeout in milliseconds.
        /// </summary>
        /// <value>The socket receive timeout in milliseconds.</value>
        public int SocketReceiveTimeout { get; set; } = 500;
        /// <summary>
        /// Gets or sets a value indicating whether this <see cref="T:MLAPI.Puncher.Client.PuncherClient"/> should drop unknown addresses.
        /// That is, ignore addresses we have not explicitly connected to. This setting only affect connectors.
        /// </summary>
        /// <value><c>true</c> if we should drop unknown addresses; otherwise, <c>false</c>.</value>
        public bool DropUnknownAddresses { get; set; } = true;
        /// <summary>
        /// Occurs on the listener when a connector punches our NAT.
        /// </summary>
        public event OnConnectorPunchSuccessfulDelegate OnConnectorPunchSuccessful;

        private readonly IPEndPoint[] _puncherServerEndpoints;
        private bool _isRunning = false;

        // Buffers
        private readonly byte[] _buffer = new byte[Constants.BUFFER_SIZE];
        private readonly byte[] _tokenBuffer = new byte[Constants.TOKEN_BUFFER_SIZE];

        /// <summary>
        /// Initializes a new instance of the <see cref="T:MLAPI.Puncher.Client.PuncherClient"/> class with a specified server endpoint.
        /// </summary>
        /// <param name="puncherServerEndpoint">Puncher server endpoint.</param>
        public PuncherClient(IPEndPoint puncherServerEndpoint)
        {
            _puncherServerEndpoints = new IPEndPoint[1] { puncherServerEndpoint };
        }

        /// <summary>
        /// Initializes a new instance of the <see cref="T:MLAPI.Puncher.Client.PuncherClient"/> class with a specified server endpoints.
        /// </summary>
        /// <param name="puncherServerEndpoint">Puncher server endpoints.</param>
        public PuncherClient(IPEndPoint[] puncherServerEndpoint)
        {
            _puncherServerEndpoints = puncherServerEndpoint;
        }

        /// <summary>
        /// Initializes a new instance of the <see cref="T:MLAPI.Puncher.Client.PuncherClient"/> class with a specified server host and port.
        /// </summary>
        /// <param name="puncherServerHost">Puncher server host.</param>
        /// <param name="puncherServerPort">Puncher server port.</param>
        public PuncherClient(string puncherServerHost, ushort puncherServerPort)
        {
            // Send the DNS query
            IPHostEntry hostEntry = Dns.GetHostEntry(puncherServerHost);

            // Sort only IPv4 addresses
            _puncherServerEndpoints = hostEntry.AddressList.Where(x => x.AddressFamily == AddressFamily.InterNetwork).Select(x => new IPEndPoint(x, puncherServerPort)).ToArray();
        }

        /// <summary>
        /// Listens for incoming punch requests.
        /// </summary>
        /// <param name="listenEndpoint">The endpoint where new players should join.</param>
        public void ListenForPunches(IPEndPoint listenEndpoint)
        {
            // Bind the socket
            Transport.Bind(listenEndpoint);

            _isRunning = true;

            RunListenerJob(false, false);

            _isRunning = false;
        }

        /// <summary>
        /// Listens for a single punch and returns when the punch is successful.
        /// </summary>
        /// <returns>The address of the connector that punched through our NAT.</returns>
        /// <param name="listenEndpoint">The endpoint where new players should join.</param>
        public IPEndPoint ListenForSinglePunch(IPEndPoint listenEndpoint)
        {
            // Bind the socket
            Transport.Bind(listenEndpoint);

            _isRunning = true;

            IPEndPoint endpoint = RunListenerJob(true, false);

            _isRunning = false;

            return endpoint;
        }

        /// <summary>
        /// Starts punching the requested peer.
        /// </summary>
        /// <returns>The remote peer connectable address.</returns>
        /// <param name="connectAddress">The peer connect address.</param>
        public bool TryPunch(IPAddress connectAddress, out IPEndPoint punchResult)
        {
            if (connectAddress.AddressFamily != AddressFamily.InterNetwork)
            {
                throw new ArgumentException("Only IPv4 addresses can be punched. IPv6 addresses does not have to be punched as they dont use NAT.");
            }

            // Bind the socket
            Transport.Bind(new IPEndPoint(IPAddress.Any, 0));

            // Set running state
            _isRunning = true;

            // Generate random token
            byte[] token = new byte[32];
            Random rnd = new Random();
            rnd.NextBytes(token);

            // Default punch result
            punchResult = null;

            // Register with NAT server
            SendRegisterRequest(connectAddress, token);

            // Waits for response from the puncher server.
            if (TryWaitForConnectorRegisterResponse(token, out IPEndPoint punchEndPoint, false))
            {
                if (DropUnknownAddresses && !punchEndPoint.Address.Equals(connectAddress))
                {
                    // The address we were asked to punch was not the same as the one we connected to.
                    // This might mean either a proxy, or a malicious interception.
                    return false;
                }

                // Sends punches
                SendPunches(punchEndPoint, new ArraySegment<byte>(token, 0, token.Length));

                // Waits for PunchSuccess
                if (TryCompleteConnectorPunch(punchEndPoint, token, out punchResult) && punchResult != null)
                {
                    _isRunning = false;

                    return true;
                }
            }


            _isRunning = false;
            return false;
        }

        #region CLIENT & SERVER

        // Sends a register and waits for a response.
        private void SendRegisterRequest(IPAddress connectAddress, byte[] token)
        {
            // Prevent info leaks
            Array.Clear(_buffer, 0, Constants.BUFFER_SIZE);

            // Write message type
            _buffer[0] = (byte)MessageType.Register;


            // Flag byte (1 = (isConnector true && isListener false))
            //           (2 = (isConnector false && isListener true))

            bool isConnector = connectAddress != null;

            if (isConnector)
            {
                // Set flag byte
                _buffer[1] = 1;

                // Write target address
                byte[] addressBytes = connectAddress.GetAddressBytes();

                // Write IPv4 Address
                Buffer.BlockCopy(addressBytes, 0, _buffer, 2, 4);

                // Calculate token lenght. Max is 32
                byte tokenLength = (byte)Math.Min(token.Length, 32);

                // Write token length
                _buffer[6] = tokenLength;

                // Write the token
                Buffer.BlockCopy(token, 0, _buffer, 7, token.Length);
            }
            else
            {
                // Set flag byte
                _buffer[1] = 2;
            }

            for (int i = 0; i < _puncherServerEndpoints.Length; i++)
            {
                // Send register
                int size = Transport.SendTo(_buffer, 0, Constants.BUFFER_SIZE, SocketSendTimeout, _puncherServerEndpoints[i]);

                if (size != Constants.BUFFER_SIZE)
                {
                    throw new SocketSendException("Could not send Register packet on socket");
                }
            }
        }

        // Sends punches and punch predictions
        private void SendPunches(IPEndPoint punchEndpoint, ArraySegment<byte> token)
        {
            // Write punch
            _buffer[0] = (byte)MessageType.Punch;

            // Write token length
            _buffer[1] = (byte)token.Count;

            // Write token
            Buffer.BlockCopy(token.Array, token.Offset, _buffer, 2, (byte)token.Count);

            for (int i = 0; i < PortPredictions; i++)
            {
                // Send all punches
                int size = Transport.SendTo(_buffer, 0, Constants.BUFFER_SIZE, SocketSendTimeout, new IPEndPoint(punchEndpoint.Address, punchEndpoint.Port + i));

                if (size != Constants.BUFFER_SIZE)
                {
                    throw new SocketSendException("Could not send Punch packet on socket");
                }
            }
        }

        #endregion

        #region LISTENER

        internal struct ListenerResponseStatus
        {
            public IPEndPoint EndPoint;
            public bool IsWaitingForResponse;
            public DateTime LastRegisterTime;
        }

        private IPEndPoint RunListenerJob(bool exitOnSuccessfulPunch, bool timeoutException)
        {
            // Register and punch loop
            SendRegisterRequest(null, null);

            // Create listener status array
            ListenerResponseStatus[] statuses = new ListenerResponseStatus[_puncherServerEndpoints.Length];
            for (int i = 0; i < statuses.Length; i++)
            {
                // Set defaults
                statuses[i] = new ListenerResponseStatus()
                {
                    EndPoint = _puncherServerEndpoints[i],
                    IsWaitingForResponse = false,
                    LastRegisterTime = DateTime.Now
                };
            }

            while (_isRunning)
            {
                int size = Transport.ReceiveFrom(_buffer, 0, Constants.BUFFER_SIZE, SocketReceiveTimeout, out IPEndPoint remoteEndPoint);

                int indexOfEndPointStatus = -1;

                for (int i = 0; i < statuses.Length; i++)
                {
                    if (statuses[i].EndPoint.Equals(remoteEndPoint))
                    {
                        indexOfEndPointStatus = i;
                        break;
                    }
                }

                if (size == Constants.BUFFER_SIZE)
                {
                    if (_buffer[0] == (byte)MessageType.Registered && remoteEndPoint != null && _puncherServerEndpoints.Contains(remoteEndPoint))
                    {
                        // Registered response.

                        statuses[indexOfEndPointStatus].IsWaitingForResponse = false;
                    }
                    else if (remoteEndPoint != null)
                    {
                        // If the message is not a registration confirmation, we try to parse it as a packet for the listener
                        if (TryParseListenerPacket(remoteEndPoint, out IPEndPoint successEndPoint))
                        {
                            if (successEndPoint != null && exitOnSuccessfulPunch)
                            {
                                return successEndPoint;
                            }
                            else if (successEndPoint != null && OnConnectorPunchSuccessful != null)
                            {
                                OnConnectorPunchSuccessful(successEndPoint);
                            }
                        }
                    }
                }

                // Resend and timeout loop
                for (int i = 0; i < statuses.Length; i++)
                {
                    if (!statuses[i].IsWaitingForResponse && (DateTime.Now - statuses[i].LastRegisterTime).TotalMilliseconds > ServerRegisterInterval)
                    {
                        // Sends new registration request
                        SendRegisterRequest(null, null);

                        // Update last register time
                        statuses[i].LastRegisterTime = DateTime.Now;
                        statuses[i].IsWaitingForResponse = true;
                    }

                    // No registration response received within timeout
                    if (statuses[i].IsWaitingForResponse && (DateTime.Now - statuses[i].LastRegisterTime).TotalMilliseconds > ServerRegisterResponseTimeout && timeoutException)
                    {
                        // We got no response to our register request.
                        throw new ServerNotReachableException("The connection to the PuncherServer \"" + _puncherServerEndpoints + "\" timed out.");
                    }
                }
            }


            return null;
        }

        // Handles punch messages
        private bool TryParseListenerPacket(IPEndPoint remoteEndPoint, out IPEndPoint punchSuccessEndpoint)
        {
            // Default endpoint
            punchSuccessEndpoint = null;

            if (_buffer[0] == (byte)MessageType.ConnectTo && remoteEndPoint != null && _puncherServerEndpoints.Contains(remoteEndPoint))
            {
                // Read incoming target address, port, token length and token
                IPAddress connectToAddress = new IPAddress(new byte[4] { _buffer[1], _buffer[2], _buffer[3], _buffer[4] });
                ushort port = (ushort)((ushort)_buffer[5] | (ushort)_buffer[6] << 8);
                byte tokenSize = _buffer[7];

                if (tokenSize > Constants.BUFFER_SIZE - 6)
                {
                    // Invalid token size
                    return false;
                }

                // Copy token
                Buffer.BlockCopy(_buffer, 8, _tokenBuffer, 0, tokenSize);

                // Clear incoming data
                Array.Clear(_buffer, 0, Constants.BUFFER_SIZE);

                // Send punches
                SendPunches(new IPEndPoint(connectToAddress, port), new ArraySegment<byte>(_tokenBuffer, 0, tokenSize));

                return true;
            }
            else if (_buffer[0] == (byte)MessageType.Punch && remoteEndPoint != null)
            {
                // Change message type, leave the body the same (token length and token)
                _buffer[0] = (byte)MessageType.PunchSuccess;

                // Send punch success
                int size = Transport.SendTo(_buffer, 0, Constants.BUFFER_SIZE, SocketSendTimeout, remoteEndPoint);

                if (size != Constants.BUFFER_SIZE)
                {
                    throw new SocketSendException("Could not send PunchSuccess packet on socket");
                }

                // Return the connector address that punched through our NAT.
                punchSuccessEndpoint = remoteEndPoint;

                return true;
            }

            return false;
        }

        #endregion

        #region CONNECTOR

        private bool TryWaitForConnectorRegisterResponse(byte[] token, out IPEndPoint connectToEndpoint, bool timeoutException)
        {
            DateTime responseWaitTimeStart = DateTime.Now;
            connectToEndpoint = null;

            do
            {
                int size = Transport.ReceiveFrom(_buffer, 0, Constants.BUFFER_SIZE, SocketReceiveTimeout, out IPEndPoint remoteEndPoint);

                if (size == Constants.BUFFER_SIZE && remoteEndPoint != null && _puncherServerEndpoints.Contains(remoteEndPoint))
                {
                    if (_buffer[0] == (byte)MessageType.Error)
                    {
                        connectToEndpoint = null;
                        return false;
                    }
                    else if (_buffer[0] == (byte)MessageType.ConnectTo)
                    {
                        IPAddress connectToAddress = new IPAddress(new byte[4] { _buffer[1], _buffer[2], _buffer[3], _buffer[4] });
                        ushort port = (ushort)((ushort)_buffer[5] | (ushort)_buffer[6] << 8);
                        byte tokenSize = _buffer[7];

                        if (tokenSize > Constants.BUFFER_SIZE - 6)
                        {
                            // Invalid token size
                            continue;
                        }

                        // Copy token
                        Buffer.BlockCopy(_buffer, 8, _tokenBuffer, 0, tokenSize);

                        // Validate the request is correct.
                        bool correct = true;

                        for (int i = 0; i < tokenSize; i++)
                        {
                            if (_tokenBuffer[i] != token[i])
                            {
                                correct = false;
                                break;
                            }
                        }

                        if (!correct)
                        {
                            // The token was incorrect. Dont return yet.
                            // Instead go for further iterations to ensure that there are no clogged messages
                            continue;
                        }

                        connectToEndpoint = new IPEndPoint(connectToAddress, port);
                        return true;
                    }
                }
            } while ((DateTime.Now - responseWaitTimeStart).TotalMilliseconds < ServerRegisterResponseTimeout && _isRunning);

            if (timeoutException)
            {
                // We got no response to our register request.
                throw new ServerNotReachableException("The connection to the PuncherServer \"" + _puncherServerEndpoints + "\" timed out.");
            }
            else
            {
                return false;
            }
        }

        // Waits for a punch response
        private bool TryCompleteConnectorPunch(IPEndPoint punchEndpoint, byte[] token, out IPEndPoint punchedEndpoint)
        {
            punchedEndpoint = null;
            DateTime receiveStart = DateTime.Now;

            do
            {
                // Receive punch success
                int size = Transport.ReceiveFrom(_buffer, 0, Constants.BUFFER_SIZE, SocketReceiveTimeout, out IPEndPoint remoteEndPoint);

                // Santy checks
                if (size == Constants.BUFFER_SIZE && remoteEndPoint != null && remoteEndPoint.Address.Equals(punchEndpoint.Address))
                {
                    if ((MessageType)_buffer[0] == MessageType.Punch)
                    {
                        // We got the listeners punch. If they punch us from a port we have not yet punched. We want to punch their new port.
                        // This improves symmetric NAT succeess rates.

                        // Make sure token size is the same
                        if (_buffer[1] == (byte)token.Length)
                        {
                            bool correct = true;

                            for (int x = 0; x < (byte)token.Length; x++)
                            {
                                if (_buffer[2 + x] != token[x])
                                {
                                    correct = false;
                                    break;
                                }
                            }

                            if (correct)
                            {
                                // Token was correct. 

                                // If the port we got the punch on is a port we have not yet punched. Use the new port to send new punches (Improves symmetric success)
                                bool hasPingedPort = false;
                                for (int x = 0; x < PortPredictions; x++)
                                {
                                    if (punchEndpoint.Port + x == remoteEndPoint.Port)
                                    {
                                        hasPingedPort = true;
                                        break;
                                    }
                                }

                                if (!hasPingedPort)
                                {
                                    // They got a totally new port that we have not seen before.
                                    // Lets punch it. We dont need to port predict these new punches
                                    int sendSize = Transport.SendTo(_buffer, 0, Constants.BUFFER_SIZE, SocketSendTimeout, new IPEndPoint(punchEndpoint.Address, remoteEndPoint.Port));

                                    if (sendSize != Constants.BUFFER_SIZE)
                                    {
                                        throw new SocketSendException("Could not send Punch packet on socket");
                                    }
                                }
                            }
                        }
                    }
                    else if (((MessageType)_buffer[0]) == MessageType.PunchSuccess)
                    {
                        // We got a punch success.

                        // Make sure token size is the same
                        if (_buffer[1] == (byte)token.Length)
                        {
                            bool correct = true;

                            for (int x = 0; x < (byte)token.Length; x++)
                            {
                                if (_buffer[2 + x] != _tokenBuffer[x])
                                {
                                    correct = false;
                                    break;
                                }
                            }

                            if (correct)
                            {
                                // Success
                                punchedEndpoint = remoteEndPoint;
                                return true;
                            }
                        }
                    }
                }

            } while ((DateTime.Now - receiveStart).TotalMilliseconds < PunchResponseTimeout && _isRunning);

            // Timeout
            return false;
        }

        #endregion

        /// <summary>
        /// Releases all resource used by the <see cref="T:MLAPI.Puncher.Client.PuncherClient"/> object.
        /// </summary>
        /// <remarks>Call <see cref="Dispose"/> when you are finished using the
        /// <see cref="T:MLAPI.Puncher.Client.PuncherClient"/>. The <see cref="Dispose"/> method leaves the
        /// <see cref="T:MLAPI.Puncher.Client.PuncherClient"/> in an unusable state. After calling
        /// <see cref="Dispose"/>, you must release all references to the
        /// <see cref="T:MLAPI.Puncher.Client.PuncherClient"/> so the garbage collector can reclaim the memory that the
        /// <see cref="T:MLAPI.Puncher.Client.PuncherClient"/> was occupying.</remarks>
        public void Dispose()
        {
            _isRunning = false;
            Transport.Close();
        }

        /// <summary>
        /// Closes the instance and releases resources.
        /// </summary>
        public void Close()
        {
            _isRunning = false;
            Transport.Close();
        }
    }
}
