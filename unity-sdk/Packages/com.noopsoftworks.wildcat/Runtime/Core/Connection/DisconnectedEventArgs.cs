using System;

namespace WildCat
{
    /// <summary>What <see cref="WildCatConnection.Disconnected"/> reports.</summary>
    public sealed class DisconnectedEventArgs
    {
        public DisconnectedEventArgs(DisconnectReason reason, Exception error)
        {
            Reason = reason;
            Error = error;
        }

        /// <summary>Why the connection closed.</summary>
        public DisconnectReason Reason { get; }

        /// <summary>The error behind it, if there was one.</summary>
        public Exception Error { get; }

        public override string ToString()
        {
            return Error == null ? Reason.ToString() : Reason + ": " + Error.Message;
        }
    }
}
