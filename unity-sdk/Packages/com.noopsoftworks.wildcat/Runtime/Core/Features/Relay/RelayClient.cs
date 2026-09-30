using System;
using WildCat.Protocol;

namespace WildCat
{
    /// <summary>
    /// Game messages between the players of a match. The server does not read them: it forwards
    /// each one, in the order sent, to every other player of the match. Message types are the
    /// game's own, above <see cref="WildCatMsgType.HighestInternal"/> (use <see cref="MessageType"/>
    /// or cast your own enum), and payloads are MessageBase classes that write their fields in
    /// Serialize(WireWriter) and read them back in Deserialize(WireReader).
    /// </summary>
    /// <example>
    /// <code>
    /// static readonly WildCatMsgType Position = RelayClient.MessageType(1);
    /// client.Relay.On&lt;PositionMessage&gt;(Position, (message, matchId) =&gt; Move(message.X, message.Y));
    /// client.Relay.Send(match.Id, Position, new PositionMessage { X = 1, Y = 2 });
    /// </code>
    /// </example>
    public sealed class RelayClient : FeatureClient
    {
        internal RelayClient(WildCatConnection connection)
            : base(connection)
        {
        }

        /// <summary>The game message type <paramref name="offset"/> places above the internal range (1 is the first).</summary>
        public static WildCatMsgType MessageType(int offset)
        {
            if (offset < 1 || offset > short.MaxValue - (int)WildCatMsgType.HighestInternal)
                throw new ArgumentOutOfRangeException(nameof(offset), "Game message offsets run from 1 to " + (short.MaxValue - (int)WildCatMsgType.HighestInternal) + ".");
            return (WildCatMsgType)((int)WildCatMsgType.HighestInternal + offset);
        }

        /// <summary>True for game message types, the ones the server relays.</summary>
        public static bool IsGameMessage(WildCatMsgType type)
        {
            return type > WildCatMsgType.HighestInternal;
        }

        /// <summary>
        /// Queues <paramref name="message"/> for the other players of <paramref name="matchId"/>.
        /// Returns false, sending nothing, when the connection is not open (for example while it
        /// reconnects) or when the send queue is full because the socket does not keep up (see
        /// <see cref="WildCatClientOptions.MaxQueuedMessages"/>). Throws ArgumentException for a
        /// message larger than <see cref="WildCatClientOptions.MaxMessageBytes"/>. Nothing comes
        /// back: the server drops messages for a match that is not running, and refuses them
        /// (logged as a warning) when nobody is logged in.
        /// </summary>
        public bool Send<T>(Guid matchId, WildCatMsgType type, T message) where T : MessageBase
        {
            if (message == null)
                throw new ArgumentNullException(nameof(message));
            RequireGameMessage(type);
            if (matchId == Guid.Empty)
                throw new ArgumentException("The match id must be set.", nameof(matchId));
            string ticket = Connection.Ticket;
            if (string.IsNullOrEmpty(ticket))
                return false;
            return Connection.Send(WildCatMessageBase.CreateWithGameTarget(message, type, WildCatMsgHeader.normal, matchId.ToString(), ticket));
        }

        /// <summary>Queues raw bytes; the other players read them with On&lt;RawMessage&gt;.</summary>
        public bool SendRaw(Guid matchId, WildCatMsgType type, byte[] data)
        {
            return Send(matchId, type, new RawMessage { value = data ?? new byte[0] });
        }

        /// <summary>
        /// Calls <paramref name="handler"/> with every relayed message of <paramref name="type"/>,
        /// decoded as <typeparamref name="T"/>, through the dispatcher. Dispose the result to stop.
        /// </summary>
        public IDisposable On<T>(WildCatMsgType type, Action<T> handler) where T : MessageBase, new()
        {
            if (handler == null)
                throw new ArgumentNullException(nameof(handler));
            return On<T>(type, (message, matchId) => handler(message));
        }

        /// <summary>As <see cref="On{T}(WildCatMsgType, Action{T})"/>, with the match id the message was sent to.</summary>
        public IDisposable On<T>(WildCatMsgType type, Action<T, Guid> handler) where T : MessageBase, new()
        {
            if (handler == null)
                throw new ArgumentNullException(nameof(handler));
            RequireGameMessage(type);
            return Connection.RegisterHandler(type, frame =>
            {
                Guid matchId;
                Guid.TryParse(frame.game, out matchId);
                T message;
                try
                {
                    message = frame.GetPayload<T>();
                }
                catch (Exception ex)
                {
                    Connection.Log(WildCatLogLevel.Warning, "A relayed " + type + " could not be read as " + typeof(T).Name + ": " + ex.Message, null);
                    return;
                }
                handler(message, matchId);
            });
        }

        private static void RequireGameMessage(WildCatMsgType type)
        {
            if (!IsGameMessage(type))
            {
                throw new ArgumentOutOfRangeException(nameof(type),
                    "Game message types must be above WildCatMsgType.HighestInternal (" + (int)WildCatMsgType.HighestInternal + "); use RelayClient.MessageType(n).");
            }
        }
    }
}
