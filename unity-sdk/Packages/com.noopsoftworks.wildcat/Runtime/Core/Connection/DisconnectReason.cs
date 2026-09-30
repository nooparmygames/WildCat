namespace WildCat
{
    /// <summary>Why a connection closed or is not open.</summary>
    public enum DisconnectReason
    {
        /// <summary>It was never connected.</summary>
        NotConnected,

        /// <summary>The application closed it (DisconnectAsync or Dispose).</summary>
        ClientClosed,

        /// <summary>The server closed the socket with a close handshake.</summary>
        ServerClosed,

        /// <summary>The socket broke: network lost, server stopped or aborted the connection.</summary>
        ConnectionLost,

        /// <summary>The server did not answer a heartbeat in time, so the SDK dropped the socket.</summary>
        HeartbeatTimeout,

        /// <summary>A connection attempt failed.</summary>
        ConnectFailed,

        /// <summary>Automatic reconnection gave up after <see cref="WildCatClientOptions.MaxReconnectAttempts"/> attempts.</summary>
        ReconnectFailed,

        /// <summary>
        /// The connection came back but the server refused to resume the session (for example the
        /// game key is no longer valid or the password changed), so the SDK stopped reconnecting.
        /// </summary>
        ResumeFailed,

        /// <summary>The connection is between a dropped socket and the next reconnection attempt.</summary>
        Reconnecting,
    }
}
