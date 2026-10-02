using System;

namespace WildCat
{
    /// <summary>
    /// Settings of a <see cref="WildCatClient"/>. The client copies them when it is created, so
    /// changing an options object afterwards has no effect on it. In Unity the WildCatSettings asset
    /// fills one in.
    /// </summary>
    public sealed class WildCatClientOptions
    {
        /// <summary>The gateway's WebSocket URL, for example wss://game.example.com/ws. Must be ws:// or wss://.</summary>
        public Uri ServerUri { get; set; } = new Uri("ws://localhost:5000/ws");

        /// <summary>How long opening the socket may take.</summary>
        public TimeSpan ConnectTimeout { get; set; } = TimeSpan.FromSeconds(10);

        /// <summary>
        /// How long a request waits for its answer before it fails with a TimeoutException, unless
        /// the call says otherwise. FindMatch waits its matchmaking time on top of this.
        /// </summary>
        public TimeSpan RequestTimeout { get; set; } = TimeSpan.FromSeconds(15);

        /// <summary>
        /// How often the SDK sends a heartbeat. It keeps the player online on the server (the server
        /// marks a player offline after 20 seconds without one) and measures the round trip time.
        /// TimeSpan.Zero turns heartbeats off.
        /// </summary>
        public TimeSpan HeartbeatInterval { get; set; } = TimeSpan.FromSeconds(10);

        /// <summary>A heartbeat not answered within this time drops the socket, which starts a reconnection.</summary>
        public TimeSpan HeartbeatTimeout { get; set; } = TimeSpan.FromSeconds(15);

        /// <summary>Reconnect on its own when the socket drops after it was connected.</summary>
        public bool AutoReconnect { get; set; } = true;

        /// <summary>
        /// After reconnecting, initialize the session again with the same keys and log in again with
        /// the last credentials (device id, user name or email with password). Passwords stay in
        /// memory for this; turn it off to keep them only as long as the login call.
        /// </summary>
        public bool ResumeSession { get; set; } = true;

        /// <summary>The first reconnection delay; each later attempt doubles it up to <see cref="ReconnectMaxDelay"/>.</summary>
        public TimeSpan ReconnectMinDelay { get; set; } = TimeSpan.FromSeconds(1);

        /// <summary>The longest reconnection delay.</summary>
        public TimeSpan ReconnectMaxDelay { get; set; } = TimeSpan.FromSeconds(30);

        /// <summary>
        /// Random spread of each reconnection delay, as a fraction (0.2 is plus or minus 20 percent,
        /// kept between the min and max delay), so clients dropped together do not all come back at
        /// the same moment.
        /// </summary>
        public double ReconnectJitter { get; set; } = 0.2;

        /// <summary>Reconnection attempts before giving up; 0 means never give up.</summary>
        public int MaxReconnectAttempts { get; set; }

        /// <summary>
        /// Passwords go to the server as plain text (the server stores a salted hash), so the SDK
        /// refuses to send one over ws:// to anything but this machine. Set this only for a test
        /// server on a network you trust; every such call logs a warning.
        /// </summary>
        public bool AllowPlainTextPasswordsWithoutTls { get; set; }

        /// <summary>
        /// The largest message (one encoded frame) the SDK sends or accepts. Keep it equal to the
        /// gateway's Gateway:MaxMessageBytes (1 MiB by default): the gateway closes the socket on a
        /// bigger message, so the SDK refuses to send one (ArgumentException, nothing is sent), and
        /// a bigger message from the server drops the socket.
        /// </summary>
        public int MaxMessageBytes { get; set; } = 1024 * 1024;

        /// <summary>
        /// How many frames may wait to be written to the socket. When the queue is full,
        /// Relay.Send returns false and requests fail at once with a <see cref="WildCatException"/>
        /// whose code is <see cref="WildCatErrorCodes.SendQueueFull"/>, instead of piling up behind
        /// a socket that does not keep up. Heartbeats and the session resume are not limited.
        /// </summary>
        public int MaxQueuedMessages { get; set; } = 1024;

        /// <summary>
        /// How many bytes may wait to be written to the socket, with the same effect as
        /// <see cref="MaxQueuedMessages"/>. At least <see cref="MaxMessageBytes"/>.
        /// </summary>
        public long MaxQueuedBytes { get; set; } = 8 * 1024 * 1024;

        /// <summary>
        /// The delay before reconnection attempt <paramref name="attempt"/> (1 for the first):
        /// <see cref="ReconnectMinDelay"/> doubled per attempt, capped at <see cref="ReconnectMaxDelay"/>,
        /// spread by <see cref="ReconnectJitter"/> using <paramref name="random01"/> (a number from 0 to 1),
        /// and kept between the two limits.
        /// </summary>
        public TimeSpan GetReconnectDelay(int attempt, double random01)
        {
            if (attempt < 1)
                attempt = 1;
            double min = Math.Max(0, ReconnectMinDelay.TotalMilliseconds);
            double max = Math.Max(min, ReconnectMaxDelay.TotalMilliseconds);
            double exponential = min * Math.Pow(2, Math.Min(attempt - 1, 30));
            double capped = Math.Min(max, exponential);
            double jitter = Math.Max(0, Math.Min(1, ReconnectJitter));
            double spread = capped * (1 + jitter * (2 * random01 - 1));
            return TimeSpan.FromMilliseconds(Math.Max(min, Math.Min(max, spread)));
        }

        internal WildCatClientOptions Clone()
        {
            return (WildCatClientOptions)MemberwiseClone();
        }

        internal void Validate()
        {
            if (ServerUri == null)
                throw new ArgumentException("WildCatClientOptions.ServerUri is not set.");
            if (!ServerUri.IsAbsoluteUri || (ServerUri.Scheme != "ws" && ServerUri.Scheme != "wss"))
                throw new ArgumentException("WildCatClientOptions.ServerUri must be a ws:// or wss:// URL, not " + ServerUri + ".");
            if (ConnectTimeout <= TimeSpan.Zero)
                throw new ArgumentException("WildCatClientOptions.ConnectTimeout must be positive.");
            if (RequestTimeout <= TimeSpan.Zero)
                throw new ArgumentException("WildCatClientOptions.RequestTimeout must be positive.");
            if (HeartbeatInterval < TimeSpan.Zero || HeartbeatTimeout <= TimeSpan.Zero)
                throw new ArgumentException("WildCatClientOptions.HeartbeatInterval must not be negative and HeartbeatTimeout must be positive.");
            if (ReconnectMinDelay < TimeSpan.Zero || ReconnectMaxDelay < ReconnectMinDelay)
                throw new ArgumentException("WildCatClientOptions.ReconnectMinDelay must not be negative or above ReconnectMaxDelay.");
            if (MaxReconnectAttempts < 0)
                throw new ArgumentException("WildCatClientOptions.MaxReconnectAttempts must not be negative.");
            if (MaxMessageBytes < 1024)
                throw new ArgumentException("WildCatClientOptions.MaxMessageBytes must be at least 1024.");
            if (MaxQueuedMessages < 1)
                throw new ArgumentException("WildCatClientOptions.MaxQueuedMessages must be at least 1.");
            if (MaxQueuedBytes < MaxMessageBytes)
                throw new ArgumentException("WildCatClientOptions.MaxQueuedBytes must be at least MaxMessageBytes.");
        }
    }
}
