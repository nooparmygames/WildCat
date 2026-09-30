using System;

namespace WildCat.Protocol
{
    /// <summary>
    /// One frame of the protocol: the message type, the header flags with the fields they
    /// switch on (request id, session ticket, game target, player targets) and the payload bytes.
    /// <see cref="WireCodec.Encode"/> and <see cref="WireCodec.Decode(byte[])"/> turn it into bytes
    /// and back; <see cref="GetPayload{T}"/> and <see cref="SetPayload{T}"/> do the same for the
    /// payload.
    /// </summary>
    /// <remarks>
    /// The payload has no type tag: the message type says which payload class it holds, and both
    /// sides have to agree on it. Game (user) messages use types above
    /// <see cref="WildCatMsgType.HighestInternal"/>.
    /// </remarks>
    public class WildCatMessageBase
    {
        private static readonly byte[] EmptyPayload = new byte[0];

        /// <summary>The type of the message, which also names its payload class.</summary>
        public WildCatMsgType msgType;

        /// <summary>The header flags; each one says whether its field is present.</summary>
        public WildCatMsgHeader header;

        /// <summary>The session ticket, sent when <see cref="WildCatMsgHeader.HasAuthData"/> is set.</summary>
        public string ticket = "";

        /// <summary>The match id, sent when <see cref="WildCatMsgHeader.HasGameTarget"/> is set.</summary>
        public string game = "";

        /// <summary>Player ids, sent when <see cref="WildCatMsgHeader.HasPlayerTarget"/> is set.</summary>
        public string[] players;

        /// <summary>The request id, sent when <see cref="WildCatMsgHeader.HasRequestID"/> is set; a response echoes it.</summary>
        public uint requestID;

        private byte[] payload = EmptyPayload;

        internal WildCatMessageBase()
        {
        }

        public bool HasGameTarget
        {
            get { return (header & WildCatMsgHeader.HasGameTarget) == WildCatMsgHeader.HasGameTarget; }
        }

        public bool hasPlayerTarget
        {
            get { return (header & WildCatMsgHeader.HasPlayerTarget) == WildCatMsgHeader.HasPlayerTarget; }
        }

        public bool hasAuthData
        {
            get { return (header & WildCatMsgHeader.HasAuthData) == WildCatMsgHeader.HasAuthData; }
        }

        public bool hasRequestID
        {
            get { return (header & WildCatMsgHeader.HasRequestID) == WildCatMsgHeader.HasRequestID; }
        }

        /// <summary>The encoded payload; never null.</summary>
        public byte[] RawData
        {
            get { return payload; }
            set { payload = value ?? EmptyPayload; }
        }

        /// <summary>Sets the request id and its header flag.</summary>
        public void SetRequestID(uint requestID)
        {
            header |= WildCatMsgHeader.HasRequestID;
            this.requestID = requestID;
        }

        /// <summary>Encodes <paramref name="message"/> into the payload.</summary>
        public void SetPayload<T>(T message) where T : MessageBase
        {
            payload = WireCodec.EncodePayload(message);
        }

        /// <summary>Decodes the payload as a <typeparamref name="T"/>.</summary>
        public T GetPayload<T>() where T : MessageBase, new()
        {
            return WireCodec.DecodePayload<T>(payload);
        }

        public static WildCatMessageBase Create<T>(T data, WildCatMsgType msgType, WildCatMsgHeader header) where T : MessageBase
        {
            WildCatMessageBase msg = new WildCatMessageBase();
            msg.SetPayload(data);
            msg.msgType = msgType;
            msg.header = header;
            return msg;
        }

        public static WildCatMessageBase CreateWithRequestID<T>(T data, WildCatMsgType msgType, WildCatMsgHeader header, uint requestID) where T : MessageBase
        {
            WildCatMessageBase msg = Create(data, msgType, header);
            msg.SetRequestID(requestID);
            return msg;
        }

        /// <summary>
        /// A response to <paramref name="request"/>: only the type, the request id (when the request
        /// had one) and the payload. It never copies the request's other header fields, so it
        /// carries no ticket, game or player targets.
        /// </summary>
        public static WildCatMessageBase CreateResponse<T>(T data, WildCatMsgType responseType, WildCatMessageBase request) where T : MessageBase
        {
            WildCatMessageBase msg = Create(data, responseType, WildCatMsgHeader.normal);
            if (request != null && request.hasRequestID)
                msg.SetRequestID(request.requestID);
            return msg;
        }

        public static WildCatMessageBase CreateWithAuthDataAndRequestID<T>(T data, WildCatMsgType msgType, WildCatMsgHeader header, string ticket, uint requestID) where T : MessageBase
        {
            AssertTicketIsNotNullOrEmpty(ticket);
            WildCatMessageBase msg = Create(data, msgType, header | WildCatMsgHeader.HasAuthData);
            msg.ticket = ticket;
            msg.SetRequestID(requestID);
            return msg;
        }

        public static WildCatMessageBase CreateWithAuthData<T>(T data, WildCatMsgType msgType, WildCatMsgHeader header, string ticket) where T : MessageBase
        {
            AssertTicketIsNotNullOrEmpty(ticket);
            WildCatMessageBase msg = Create(data, msgType, header | WildCatMsgHeader.HasAuthData);
            msg.ticket = ticket;
            return msg;
        }

        /// <summary>
        /// A message aimed at a match. Sets <see cref="WildCatMsgHeader.HasAuthData"/> and
        /// <see cref="WildCatMsgHeader.HasGameTarget"/> whatever <paramref name="header"/> says.
        /// </summary>
        public static WildCatMessageBase CreateWithGameTarget<T>(T data, WildCatMsgType msgType, WildCatMsgHeader header, string gameID, string ticket) where T : MessageBase
        {
            AssertTicketIsNotNullOrEmpty(ticket);
            if (string.IsNullOrEmpty(gameID))
                throw new ArgumentException("gameId should not be null or empty");
            WildCatMessageBase msg = Create(data, msgType, header | WildCatMsgHeader.HasGameTarget | WildCatMsgHeader.HasAuthData);
            msg.ticket = ticket;
            msg.game = gameID;
            return msg;
        }

        /// <summary>
        /// A message aimed at some players. Sets <see cref="WildCatMsgHeader.HasAuthData"/> and
        /// <see cref="WildCatMsgHeader.HasPlayerTarget"/> whatever <paramref name="header"/> says.
        /// </summary>
        public static WildCatMessageBase CreateWithPlayerTargets<T>(T data, WildCatMsgType msgType, WildCatMsgHeader header, string[] players, string ticket) where T : MessageBase
        {
            AssertTicketIsNotNullOrEmpty(ticket);
            if (players == null || players.Length == 0)
                throw new ArgumentException("players array should contain at least one element");
            for (int i = 0; i < players.Length; ++i)
            {
                if (string.IsNullOrEmpty(players[i]))
                    throw new ArgumentException("none of the players should be null or empty");
            }
            WildCatMessageBase msg = Create(data, msgType, header | WildCatMsgHeader.HasPlayerTarget | WildCatMsgHeader.HasAuthData);
            msg.ticket = ticket;
            msg.players = players;
            return msg;
        }

        private static void AssertTicketIsNotNullOrEmpty(string ticket)
        {
            if (string.IsNullOrEmpty(ticket))
                throw new ArgumentException("Ticket should not be null or empty");
        }
    }
}
