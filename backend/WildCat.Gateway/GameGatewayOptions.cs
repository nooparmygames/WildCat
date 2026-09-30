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
    }
}
