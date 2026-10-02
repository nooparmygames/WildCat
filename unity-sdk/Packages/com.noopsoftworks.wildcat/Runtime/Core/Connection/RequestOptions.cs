using System;

namespace WildCat
{
    /// <summary>How <see cref="WildCatConnection.RequestAsync{TResponse}"/> frames and waits for a request.</summary>
    public sealed class RequestOptions
    {
        /// <summary>
        /// Put the session ticket in the frame header (the default). Session and sign-in requests
        /// that carry the ticket in their payload, and GetServerTime, go without it.
        /// </summary>
        public bool SendTicket { get; set; } = true;

        /// <summary>A match id for the frame's game target (LeaveGame uses it), or null.</summary>
        public string GameTarget { get; set; }

        /// <summary>How long to wait for the answer; null uses <see cref="WildCatClientOptions.RequestTimeout"/>.</summary>
        public TimeSpan? Timeout { get; set; }
    }
}
