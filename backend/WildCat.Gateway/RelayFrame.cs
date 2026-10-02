using WildCat.Protocol;

namespace WildCat.Gateway
{
    /// <summary>
    /// Builds the frames the gateway relays between the players of a match.
    /// </summary>
    public static class RelayFrame
    {
        /// <summary>
        /// Encodes a copy of a client's game message that is safe to pass on to the other players:
        /// the same message type, request id (flag and value), game target and payload bytes, but
        /// without the sender's session ticket. Relaying the ticket let any player in the match act
        /// as the sender.
        /// </summary>
        /// <remarks>
        /// The player target is dropped as well: the relay always goes to every other player in the
        /// match.
        /// </remarks>
        public static byte[] WithoutTicket(WildCatMessageBase message)
        {
            var relayed = WildCatMessageBase.Create(new EmptyMessage(), message.msgType,
                message.header & ~(WildCatMsgHeader.HasAuthData | WildCatMsgHeader.HasPlayerTarget));
            relayed.requestID = message.requestID;
            relayed.game = message.game;
            relayed.RawData = message.RawData;
            return WireCodec.Encode(relayed);
        }

        /// <summary>The same for a frame still in its encoded form.</summary>
        public static byte[] WithoutTicket(byte[] frame)
        {
            return WithoutTicket(WireCodec.Decode(frame));
        }
    }
}
