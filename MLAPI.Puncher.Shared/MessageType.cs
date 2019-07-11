namespace MLAPI.Puncher.Shared
{
    /// <summary>
    /// Represents the different network message types.
    /// </summary>
    public enum MessageType
    {
        /// <summary>
        /// Sent by client to register a listen or a connection request.
        /// </summary>
        Register,
        Registered,
        /// <summary>
        /// Sent by server to inform the listening and connecting client to knock on each others NAT.
        /// </summary>
        ConnectTo,
        /// <summary>
        /// Sent by server to explain errors
        /// </summary>
        Error,
        /// <summary>
        /// Sent by listener and connecting client to knock on the other clients NAT.
        /// </summary>
        Punch,
        /// <summary>
        /// Sent by listener client to inform the connecting client that the connecting clients messages got through his NAT.
        /// </summary>
        PunchSuccess
    }
}
