using System;

namespace WildCat.Gateway
{
    /// <summary>
    /// Limits and policies for the WebSocket gateway, bound from the "Gateway" configuration
    /// section by the host (see WildCat.Host/Program.cs and appsettings.json).
    /// </summary>
    public class GameGatewayOptions
    {
        /// <summary>The configuration section these options are read from.</summary>
        public const string SectionName = "Gateway";

        /// <summary>
        /// Largest message a client may send, after the gateway puts its WebSocket frames back
        /// together. A client that sends more has its socket closed with MessageTooBig.
        /// Defaults to 1 MiB.
        /// </summary>
        public int MaxMessageBytes { get; set; } = 1024 * 1024;

        /// <summary>
        /// Origins (for example "https://game.example.com") allowed to open a WebSocket. Empty
        /// accepts any origin. When the list is set, a WebSocket request whose Origin header is not
        /// in it gets 403. Requests without an Origin header, which is how native clients connect,
        /// are always accepted; the check only stops other web pages from connecting through a
        /// player's browser.
        /// </summary>
        public string[] AllowedOrigins { get; set; } = Array.Empty<string>();

        /// <summary>
        /// Sign-in attempts one connection may make back to back: 76 Authenticate,
        /// 78 CreateNewAccount, 80 EmailActivation, 82 AddAuthMechanismToAccount, 84 RecoverPassword,
        /// 86 ResendActivation and 163 ResetPasswordWithCode share the allowance. Each attempt checks
        /// a password or a code or sends an email, so this caps guessing and mail per socket.
        /// Defaults to 5.
        /// </summary>
        public int AuthAttemptBurst { get; set; } = 5;

        /// <summary>
        /// How fast a connection earns sign-in attempts back after using up
        /// <see cref="AuthAttemptBurst"/>. Defaults to 6 a minute, one every 10 seconds. An attempt
        /// over the limit is answered with error code 1 and nothing else happens.
        /// </summary>
        public double AuthAttemptsPerMinute { get; set; } = 6;

        /// <summary>
        /// Sign-in attempts one client address may make back to back, over all its connections
        /// to this gateway node (the same messages as <see cref="AuthAttemptBurst"/>). A new socket
        /// gets a full per-connection allowance, so without this a client could open sockets to
        /// keep guessing. Defaults to 30; 0 turns the per-address limit off. The address is the one
        /// the forwarded headers middleware reports, so list a reverse proxy in
        /// ForwardedHeaders:KnownProxies or KnownNetworks or all its clients share one allowance.
        /// IPv6 addresses count per /64; loopback addresses are not limited (see
        /// <see cref="AddressAttemptLimiter"/>). Raise it when many players share an address.
        /// </summary>
        public int AuthAttemptsPerAddressBurst { get; set; } = 30;

        /// <summary>
        /// How fast an address earns sign-in attempts back after using up
        /// <see cref="AuthAttemptsPerAddressBurst"/>. Defaults to 30 a minute.
        /// </summary>
        public double AuthAttemptsPerAddressPerMinute { get; set; } = 30;

        /// <summary>
        /// Messages one connection may have waiting to be written to its socket: responses, pushed
        /// events and relayed game messages. Nothing that produces a message waits for the socket;
        /// when a client reads so slowly that the queue fills up (in messages or in
        /// <see cref="OutboundQueueBytes"/>), the gateway logs a warning and closes the connection
        /// with PolicyViolation. Defaults to 1024.
        /// </summary>
        public int OutboundQueueCapacity { get; set; } = 1024;

        /// <summary>
        /// Bytes one connection may have waiting to be written to its socket, counting the message
        /// being written; past it the connection is closed like one whose queue is full. A single
        /// message larger than this still goes out when nothing else is waiting. Defaults to 8 MiB,
        /// so a stalled client holds at most that much (1024 messages of up to 1 MiB would be a
        /// gigabyte).
        /// </summary>
        public int OutboundQueueBytes { get; set; } = 8 * 1024 * 1024;

        /// <summary>
        /// Bytes of game messages one connection may have waiting to be relayed to the other
        /// players of its match (at most 256 messages either way). When the relay falls behind,
        /// the gateway stops reading that client's socket until there is room. A single message
        /// larger than this still goes through when nothing else is waiting. Defaults to 4 MiB.
        /// </summary>
        public int RelayQueueBytes { get; set; } = 4 * 1024 * 1024;

        /// <summary>
        /// Requests of one connection the gateway works on at the same time; the next one is read
        /// from the socket when one finishes. A slow request (FindMatch waits for opponents) does not
        /// hold up heartbeats this way. Relayed game messages do not count: they go through their own
        /// queue, one at a time, so they keep their order. Defaults to 8.
        /// </summary>
        public int MaxConcurrentRequests { get; set; } = 8;
    }
}
