namespace WildCat
{
    /// <summary>The state of a <see cref="WildCatConnection"/>.</summary>
    public enum ConnectionState
    {
        /// <summary>Not connected; ConnectAsync opens the connection.</summary>
        Disconnected,

        /// <summary>ConnectAsync is opening the socket.</summary>
        Connecting,

        /// <summary>Open: requests and relayed messages can be sent.</summary>
        Connected,

        /// <summary>
        /// The socket dropped and the SDK is opening a new one and resuming the session. Requests
        /// fail with <see cref="WildCatDisconnectedException"/> until <see cref="WildCatConnection.Reconnected"/>.
        /// </summary>
        Reconnecting,
    }
}
