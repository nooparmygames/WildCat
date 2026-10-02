using System;

namespace WildCat
{
    /// <summary>What <see cref="WildCatConnection.Reconnecting"/> reports before each attempt.</summary>
    public sealed class ReconnectingEventArgs
    {
        public ReconnectingEventArgs(int attempt, TimeSpan delay, DisconnectReason reason, Exception lastError)
        {
            Attempt = attempt;
            Delay = delay;
            Reason = reason;
            LastError = lastError;
        }

        /// <summary>The attempt about to be made, starting at 1.</summary>
        public int Attempt { get; }

        /// <summary>How long the SDK waits before this attempt.</summary>
        public TimeSpan Delay { get; }

        /// <summary>Why the connection dropped in the first place.</summary>
        public DisconnectReason Reason { get; }

        /// <summary>The error of the previous attempt (or of the drop, before the first attempt).</summary>
        public Exception LastError { get; }
    }
}
