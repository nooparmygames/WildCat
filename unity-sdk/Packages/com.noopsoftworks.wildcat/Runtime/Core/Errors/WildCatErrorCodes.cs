namespace WildCat
{
    /// <summary>The error codes the server uses in its answers (see docs/wire-protocol.md).</summary>
    public static class WildCatErrorCodes
    {
        /// <summary>The request was refused or failed: a client error or an expected outcome such as a wrong password.</summary>
        public const int Refused = 1;

        /// <summary>The thing to create exists already, or the other player challenged you as well.</summary>
        public const int AlreadyExists = 2;

        /// <summary>The server failed; the details are in the server log, the message is generic.</summary>
        public const int Internal = -1;

        /// <summary>
        /// Not from the server: the answer had a different type than the request expects. Seen only
        /// when the SDK and the server do not speak the same protocol version.
        /// </summary>
        public const int UnexpectedResponse = -1000;

        /// <summary>
        /// Not from the server: the request was not sent because the send queue was full (see
        /// <see cref="WildCatClientOptions.MaxQueuedMessages"/>). The connection stays open; try
        /// again once it has caught up.
        /// </summary>
        public const int SendQueueFull = -1001;
    }
}
