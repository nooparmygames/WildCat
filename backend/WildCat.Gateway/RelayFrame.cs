using WildCat.Protocol;

namespace WildCat.Gateway
{
    /// <summary>
    /// Builds the frames the gateway relays between the players of a match.
    /// </summary>
    public static class RelayFrame
    {
        /// <summary>
        /// Returns a copy of a client frame that is safe to pass on to the other players: the same
        /// message type, request id (flag and value), game target and payload bytes, but without
        /// the sender's session ticket. Relaying the ticket let any player in the match act as the
        /// sender.
        /// </summary>
        /// <remarks>
        /// The player target is dropped as well. The relay always goes to every other player in
        /// the match, and the player target count is written as int32 but read as int16 (a known
        /// protocol bug), so passing it on would only hand the receiver a frame it cannot parse.
        /// </remarks>
        /// <param name="frame">The frame exactly as the client sent it.</param>
        public static byte[] WithoutTicket(byte[] frame)
        {
            var relayed = new WildCatMessageBase(frame);
            relayed.header &= ~(WildCatMsgHeader.HasAuthData | WildCatMsgHeader.HasPlayerTarget);
            relayed.ticket = "";
            relayed.players = null;
            return relayed.ToByteArray();
        }
    }
}
