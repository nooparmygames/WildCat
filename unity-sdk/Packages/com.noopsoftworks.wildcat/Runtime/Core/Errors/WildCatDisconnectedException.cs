using System;

namespace WildCat
{
    /// <summary>
    /// A request could not be answered because the connection is not open: it was never
    /// connected, it dropped while the request was waiting, it is reconnecting, or it was closed.
    /// After a reconnection (<see cref="WildCatConnection.Reconnected"/>) new requests work again.
    /// </summary>
    public class WildCatDisconnectedException : Exception
    {
        public WildCatDisconnectedException(string message, DisconnectReason reason)
            : base(message)
        {
            Reason = reason;
        }

        public WildCatDisconnectedException(string message, DisconnectReason reason, Exception innerException)
            : base(message, innerException)
        {
            Reason = reason;
        }

        /// <summary>Why the connection is not open.</summary>
        public DisconnectReason Reason { get; }
    }
}
