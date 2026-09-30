using System;
using System.Collections.Generic;
using System.IO;

namespace WildCat.Protocol
{
    public class MessageBase
    {
        public virtual void Serialize(BinaryWriter writer)
        {

        }

        public virtual void Deserialize(BinaryReader reader)
        {

        }

    }
    /// <summary>
    /// Different kinds of login types, should
    /// be synced with WildCat.Interfaces.LoginType in IPlayer
    /// </summary>
    public enum LoginType : byte { Device, UserWithPassword, Email }

    /// <summary>
    /// Online status of a friend
    /// </summary>
    public enum OnlineStatus : byte { Online, Offline }

    /// <summary>
    /// Different types of event parameters
    /// Should be synced with IScriptEventRunner.ParamType
    /// </summary>
    public enum ParamType { BoolParam, ByteParam, ShortParam, IntParam, LongParam, FloatParam, DoubleParam, StringParam, ByteArrayParam }

    /// <summary>
    /// The type of friend invitaiton request 
    /// </summary>
    public enum InvitationType : byte { AccountId, playerName, Email, UserId }

    /// <summary>
    /// Different message types which can be sent.
    /// </summary>
    public enum WildCatMsgType : short
    {
        InitializeSessionRequest = 6,
        InitializeSessionResponse = 7,

        ErrorMsg = 8,

        GamePlayerAddedEvent = 9,
        GameFinishedEvent = 10,
        LeaveGameRequest = 13,
        LeaveGameResponse = 14,

        FindMatchRequest = 15,
        FindMatchResponse = 16,

        StartChallengeRequest = 17,
        StartChallengeResponse = 18,
        StartChallengeRequestedEvent = 19,
        AcceptChallengeRequest = 20,
        AcceptChallengeResponse = 21,
        ChallengeAcceptedEvent = 22,
        DenyChallengeRequest = 23,
        DenyChallengeResponse = 24,
        ChallengeDeniedEvent = 25,

        GetLeaderboardRequest = 26,
        GetLeaderboardResponse = 27,
        SetLeaderboardRequest = 28,
        SetLeaderboardResponse = 29,

        GetCloudDataRequest = 30,
        GetCloudDataResponse = 31,
        SetCloudDataRequest = 32,
        SetCloudDataResponse = 33,
        //GetCloudBinaryDataRequest,
        //GetCloudBinaryDataResponse,
        //SetCloudBinaryDataRequest,
        //SetCloudBinaryDataResponse,

        GetPublisherDataRequest = 34,
        GetPublisherDataResponse = 35,

        GetNameRequest = 36,
        GetNameResponse = 37,
        SetNameRequest = 38,
        SetNameResponse = 39,

        GetServerTimeRequest = 42,
        GetServerTimeResponse = 43,

        #region DeveloperMessages
        CreateEnvironmentRequest = 44,
        CreateEnvironmentResponse = 45,

        SetPublisherDataRequest_S = 62,
        SetPublisherDataResponse_S = 63,
        GetPublisherDataRequest_S = 64,
        GetPublisherDataResponse_S = 65,
        #endregion

        ExecuteScriptEventRequest = 66,
        ExecuteScriptEventResponse = 67,
        GetPlayerNameRequest = 68,
        GetPlayerNameResponse = 69,
        GetPlayerPublicCloudDataRequest = 70,
        GetPlayerPublicCloudDataResponse = 71,
        SetPublicCloudDataRequest = 72,
        SetPublicCloudDataResponse = 73,
        GetPublicCloudDataRequest = 74,
        GetPublicCloudDataResponse = 75,
        AuthenticateRequest = 76,
        AuthenticateResponse = 77,
        CreateNewAccountRequest = 78,
        CreateNewAccountResponse = 79,
        EmailActivationRequest = 80,
        EmailActivationResponse = 81,
        AddAuthMechanismToAccountRequest = 82,
        AddAuthMechanismToAccountResponse = 83,
        RecoverPasswordRequest = 84,
        RecoverPasswordResponse = 85,
        ResendActivationRequest = 86,
        ResendActivationResponse = 87,

        GetFriendsRequest = 88,
        GetFriendsResponse = 89,
        InviteFriendRequest = 90,
        InviteFriendResponse = 91,
        FriendshipInvitationEvent = 92,
        AcceptFriendRequest = 93,
        AcceptFriendResponse = 94,
        FriendshipAcceptedEvent = 95,
        DenyFriendRequest = 96,
        DenyFriendResponse = 97,
        FriendshipDeniedEvent = 98,
        FriendStatusChangedEvent = 99,
        HeartBeatRequest = 100,
        HeartBeatResponse = 101,

        GetDefinedItemsRequest = 107,
        GetDefinedItemsResponse = 108,
        GetInventoryRequest = 109,
        GetInventoryResponse = 110,
        AddItemToInventoryRequest = 111,
        AddItemToInventoryResponse = 112,
        SetItemToInventoryRequest = 113,
        SetItemToInventoryResponse = 114,
        RemoveInventoryItemRequest = 115,
        RemoveInventoryItemResponse = 116,
        BuyItemRequest = 117,
        BuyItemResponse = 118,
        UnlockInventoryRequest = 119,
        UnlockInventoryResponse = 120,
        GetInventoryUnlockTimeRequest = 121,
        GetInventoryUnlockTimeResponse = 122,
        ConsumeItemRequest = 123,
        ConsumeItemResponse = 124,
        GamePlayerRemovedEvent = 125,
        GamePlayerRejoinedEvent = 126,
        MatchRejoinSyncEvent = 127,
        MatchPropertyChangedEvent = 128,
        SetMatchPropertyRequest = 129,
        SetMatchPropertyResponse = 130,
        GetPlayerPendingMatchesRequest = 131,
        GetPlayerMatchesResponse = 132,
        RejoinMatchRequest = 133,
        RejoinMatchResponse = 134,
        GetPlayerChallengesRequest = 161,
        GetPlayerChallengesResponse = 162,

        //User messages should start from HighestInternal+1
        HighestInternal = 1023
    }

    /// <summary>
    /// Message header 8 bits-flags for different message properties
    /// </summary>
    /// <remarks>
    /// uNet's own header is used for properties like QOS channel to use and ack 
    /// </remarks>
    [Flags]
    public enum WildCatMsgHeader : byte
    {
        normal = 0,
        HasGameTarget = 1,
        HasPlayerTarget = 2,
        HasAuthData = 4,
        //Reserved 8         8,
        HasRequestID = 16,
        stored = 32,
        buffered = 64,
        hasTime = 128,
    }

    /// <summary>
    /// All other messages sent in the system are wrapped in this.
    /// This is the message protocol of WildCat.
    /// </summary>
    /// <remarks>
    /// The first two bytes are a short indicating message type.
    /// User messages should use values higher than MsgType.Highest
    /// The next byte is a header
    /// for describing message properties like
    /// Authentication Data's existence or lack there of
    /// The game/player which message is targeted at or
    /// being stored/buffered/... which are not used yet and will be used later on
    /// </remarks>
    public class WildCatMessageBase : MessageBase
    {
        /// <summary>
        /// This is type of the message
        /// </summary>
        public WildCatMsgType msgType;

        /// <summary>
        /// This is header of the message containing message attributes
        /// </summary>
        public WildCatMsgHeader header;

        /// <summary>
        /// Ticket of the loggedIn user if applicable
        /// </summary>
        /// <remarks>
        /// Only used for messages which are authenticated. The header should have the HasAuthData flag set
        /// </remarks>
        public string ticket = "";

        /// <summary>
        /// Game target if applicable.
        /// </summary>
        /// <remarks>
        /// Only applicable if a message is targeted at a game and the HasGameTarget flag is set in the header
        /// </remarks>
        public string game = "";

        /// <summary>
        /// Player targets if applicable.
        /// </summary>
        /// <remarks>
        /// This is only applicable if a message is targetted at a specific set of players and HasPlayerTarget flag is set at the header
        /// </remarks>
        public string[] players;

        /// <summary>
        /// The request ID of the message
        /// </summary>
        public uint requestID;

        /// <summary>
        /// This is the actual message payload.
        /// </summary>
        /// <remarks>
        /// In theory it can be any byte array but it is preferred to use uNet's messages derived from MessageBase to send
        /// complex data in a message.
        /// There is no type information so the message type is the only indicator dictating which data type it should be and the
        /// application developer should honor it on his/her own.
        /// </remarks>
        private byte[] payload;

        public bool HasGameTarget
        {
            get
            {
                return (header & WildCatMsgHeader.HasGameTarget) == WildCatMsgHeader.HasGameTarget;
            }
        }

        public bool hasPlayerTarget
        {
            get
            {
                return (header & WildCatMsgHeader.HasPlayerTarget) == WildCatMsgHeader.HasPlayerTarget;
            }
        }

        public bool hasAuthData
        {
            get
            {
                return (header & WildCatMsgHeader.HasAuthData) == WildCatMsgHeader.HasAuthData;
            }
        }

        public bool hasRequestID
        {
            get
            {
                return (header & WildCatMsgHeader.HasRequestID) == WildCatMsgHeader.HasRequestID;
            }
        }

        public byte[] RawData
        {
            get
            {
                return payload;
            }
            set
            {
                payload = value;
            }
        }

        private WildCatMessageBase()
        {

        }

        /// <summary>
        /// Creates a message from a serialized byte array
        /// </summary>
        /// <param name="serializedMessage">A full WildCatMessageBase serialized as byte array</param>
        /// <remarks>
        /// This deserializes a full message and not only a payload, 
        /// The message should contain all header related
        /// info and the header itselfin addition of any payloads
        /// </remarks>
        public WildCatMessageBase(byte[] serializedMessage)
        {
            using (MemoryStream str = new MemoryStream(serializedMessage))
            {
                using (BinaryReader reader = new BinaryReader(str))
                {
                    this.Deserialize(reader);
                }
            }
        }

        /// <summary>
        /// Sets the request ID of the message and the appropreate flag in the header
        /// </summary>
        /// <param name="requestID">The request ID to be set</param>
        public void SetRequestID(uint requestID)
        {
            header |= WildCatMsgHeader.HasRequestID;
            this.requestID = requestID;
        }

        /// <summary>
        /// Creates a byte array from a message to be sent as payload
        /// </summary>
        /// <typeparam name="T"></typeparam>
        /// <param name="msg"></param>
        /// <returns></returns>
        public void SetData<T>(T msg) where T : MessageBase, new()
        {
            using (MemoryStream str = new MemoryStream())
            {
                using (BinaryWriter writer = new BinaryWriter(str))
                {
                    msg.Serialize(writer);
                }
                payload = str.ToArray();
            }
        }

        /// <summary>
        /// Converts the message payload to a message.
        /// </summary>
        /// <typeparam name="T">Type of the message</typeparam>
        /// <returns>The converted message</returns>
        public T GetData<T>() where T : MessageBase, new()
        {
            T t = new T();
            using (MemoryStream str = new MemoryStream(payload))
            {

                using (BinaryReader reader = new BinaryReader(str))
                {

                    t.Deserialize(reader);
                }
            }
            return t;
        }

        public override void Serialize(BinaryWriter writer)
        {
            writer.Write((short)msgType);
            writer.Write((byte)header);
            if (hasRequestID)
            {
                writer.Write(requestID);
            }
            if (hasAuthData)
            {
                writer.Write(ticket);
            }
            if (HasGameTarget)
            {
                writer.Write(game);
            }
            if (hasPlayerTarget)
            {
                writer.Write(players.Length);
                for (int i = 0; i < players.Length; ++i)
                    writer.Write(players[i]);
            }
            writer.WriteBytesFull(payload);
        }

        public override void Deserialize(BinaryReader reader)
        {
            msgType = (WildCatMsgType)reader.ReadInt16();
            header = (WildCatMsgHeader)reader.ReadByte();
            if (hasRequestID)
            {
                requestID = reader.ReadUInt32();
            }
            if (hasAuthData)
            {
                ticket = reader.ReadString();
            }
            if (HasGameTarget)
            {
                game = reader.ReadString();
            }
            if (hasPlayerTarget)
            {
                var playersSize = reader.ReadInt16();
                players = new string[playersSize];
                for (int i = 0; i < players.Length; ++i)
                {
                    players[i] = reader.ReadString();
                }
            }

            payload = reader.ReadBytesAndSize();
        }

        /// <summary>
        /// Converts the whole message to a byte array,
        /// The array can be used with the constructor which takes a byte array
        /// to create a new message.
        /// </summary>
        /// <returns>A message serialized as byte array</returns>
        public byte[] ToByteArray()
        {
            byte[] res;
            using (MemoryStream str = new MemoryStream())
            {
                using (BinaryWriter nw = new BinaryWriter(str))
                {
                    Serialize(nw);
                }
                res = str.ToArray();
            }
            return res;
        }

        public static WildCatMessageBase Create<T>(T data, WildCatMsgType msgType, WildCatMsgHeader header) where T : MessageBase, new()
        {
            WildCatMessageBase msg = new WildCatMessageBase();
            msg.SetData(data);
            msg.msgType = msgType;
            msg.header = header;
            return msg;
        }

        public static WildCatMessageBase CreateWithRequestID<T>(T data, WildCatMsgType msgType, WildCatMsgHeader header, uint requestID) where T : MessageBase, new()
        {

            WildCatMessageBase msg = new WildCatMessageBase();
            msg.SetData(data);
            msg.msgType = msgType;
            msg.header = header;
            msg.header |= WildCatMsgHeader.HasRequestID;
            msg.requestID = requestID;
            return msg;
        }

        public static WildCatMessageBase CreateWithAuthDataAndRequestID<T>(T data, WildCatMsgType msgType, WildCatMsgHeader header, string ticket, uint requestID) where T : MessageBase, new()
        {
            AssertTicketIsNotNullOrEmpty(ticket);
            WildCatMessageBase msg = new WildCatMessageBase();
            msg.SetData(data);
            msg.msgType = msgType;
            msg.header = header;
            msg.header |= WildCatMsgHeader.HasAuthData;
            msg.header |= WildCatMsgHeader.HasRequestID;
            msg.ticket = ticket;
            msg.requestID = requestID;
            return msg;
        }

        public static WildCatMessageBase CreateWithAuthData<T>(T data, WildCatMsgType msgType, WildCatMsgHeader header, string ticket) where T : MessageBase, new()
        {
            AssertTicketIsNotNullOrEmpty(ticket);
            WildCatMessageBase msg = new WildCatMessageBase();
            msg.SetData(data);
            msg.msgType = msgType;
            msg.header = header;
            msg.header |= WildCatMsgHeader.HasAuthData;
            msg.ticket = ticket;
            return msg;
        }

        private static void AssertTicketIsNotNullOrEmpty(string ticket)
        {
            if (string.IsNullOrEmpty(ticket))
                throw new ArgumentException("Ticket should not be null or empty");
        }

        /// <summary>
        /// 
        /// </summary>
        /// <typeparam name="T"></typeparam>
        /// <param name="data"></param>
        /// <param name="msgType"></param>
        /// <param name="header"></param>
        /// <param name="gameID"></param>
        /// <param name="ticket"></param>
        /// <returns></returns>
        /// <remarks>This will force HasAuthData and HasGameTarget headers regardless of your passed header</remarks>
        public static WildCatMessageBase CreateWithGameTarget<T>(T data, WildCatMsgType msgType, WildCatMsgHeader header, string gameID, string ticket) where T : MessageBase, new()
        {
            AssertTicketIsNotNullOrEmpty(ticket);
            AssertGameidIsNotNullOrEmpty(gameID);
            WildCatMessageBase msg = new WildCatMessageBase();
            msg.SetData(data);
            msg.msgType = msgType;
            msg.header = header;
            //Add game target to header
            msg.header |= WildCatMsgHeader.HasGameTarget;
            msg.header |= WildCatMsgHeader.HasAuthData;
            msg.ticket = ticket;
            msg.game = gameID;
            return msg;
        }

        private static void AssertGameidIsNotNullOrEmpty(string gameID)
        {
            if (string.IsNullOrEmpty(gameID))
                throw new ArgumentException("gameId should not be null or empty");
        }

        /// <summary>
        /// 
        /// </summary>
        /// <typeparam name="T"></typeparam>
        /// <param name="data"></param>
        /// <param name="msgType"></param>
        /// <param name="header"></param>
        /// <param name="players"></param>
        /// <param name="ticket"></param>
        /// <returns></returns>
        /// <remarks>This will force HasAuthData and HasPlayerTarget header flags regardless of your passed header</remarks>
        public static WildCatMessageBase CreateWithPlayerTargets<T>(T data, WildCatMsgType msgType, WildCatMsgHeader header, string[] players, string ticket) where T : MessageBase, new()
        {
            AssertTicketIsNotNullOrEmpty(ticket);
            AssertPlayersAreNotNullOrEmpty(players);
            WildCatMessageBase msg = new WildCatMessageBase();
            msg.SetData(data);
            msg.msgType = msgType;
            msg.header = header;
            //Add player target to header
            msg.header |= WildCatMsgHeader.HasPlayerTarget;
            msg.header |= WildCatMsgHeader.HasAuthData;
            msg.ticket = ticket;
            msg.players = players;
            return msg;
        }

        private static void AssertPlayersAreNotNullOrEmpty(string[] players)
        {
            if (players.Length == 0)
                throw new ArgumentException("players array should contain at least one element");
            for (int i = 0; i < players.Length; ++i)
            {
                if (string.IsNullOrEmpty(players[i]))
                    throw new ArgumentException("none of the players should be null or empty");
            }
        }

        public static WildCatMessageBase CreateMessageFromByteArray(byte[] data)
        {
            WildCatMessageBase msg = new WildCatMessageBase();
            using (MemoryStream str = new MemoryStream(data))
            {
                using (BinaryReader nr = new BinaryReader(str))
                {

                    msg.Deserialize(nr);
                }
            }
            return msg;
        }

    }

    #region InternalMessages

    public class CreateEnvironmentRequest : MessageBase
    {
        public string FirstName = "";
        public string LastName = "";
        public string PasswordHash = "";
        public string Email = "";
        public string PhoneNumber = "";
        public string Address = "";
        public string Description = "";

        public string GameName = "";

        public override void Serialize(BinaryWriter writer)
        {
            writer.Write(FirstName);
            writer.Write(LastName);
            writer.Write(PasswordHash);
            writer.Write(Email);
            writer.Write(PhoneNumber);
            writer.Write(Address);
            writer.Write(Description);
            writer.Write(GameName);
        }

        public override void Deserialize(BinaryReader reader)
        {
            FirstName = reader.ReadString();
            LastName = reader.ReadString();
            PasswordHash = reader.ReadString();
            Email = reader.ReadString();
            PhoneNumber = reader.ReadString();
            Address = reader.ReadString();
            Description = reader.ReadString();
            GameName = reader.ReadString();
        }
    }

    public class CreateEnvironmentResponse : MessageBase
    {
        public Guid DeveloperKey;
        public Guid GameKey;

        public override void Serialize(BinaryWriter writer)
        {
            writer.WriteBytesFull(DeveloperKey.ToByteArray());
            writer.WriteBytesFull(GameKey.ToByteArray());
        }

        public override void Deserialize(BinaryReader reader)
        {
            DeveloperKey = new Guid(reader.ReadBytesAndSize());
            GameKey = new Guid(reader.ReadBytesAndSize());
        }
    }


    //add tests
    public class InitializeSessionRequest : MessageBase
    {
        public Guid DeveloperKey;
        public Guid GameKey;

        public override void Serialize(BinaryWriter writer)
        {
            writer.WriteBytesFull(DeveloperKey.ToByteArray());
            writer.WriteBytesFull(GameKey.ToByteArray());
        }

        public override void Deserialize(BinaryReader reader)
        {
            DeveloperKey = new Guid(reader.ReadBytesAndSize());
            GameKey = new Guid(reader.ReadBytesAndSize());
        }
    }

    public class ChallengeRequestsMessage : MessageBase
    {
        public class Request
        {
            public enum Status { Pending, Started, Finished }
            public Status status;
            public string Player;
            public string PlayerName;
            public Guid gameId;
            public byte[] data;
        }

        public List<Request> sent;
        public List<Request> received;

        public ChallengeRequestsMessage()
        {
            sent = new List<Request>();
            received = new List<Request>();
        }

        public override void Serialize(BinaryWriter writer)
        {
            writer.Write(sent.Count);
            for (int i = 0; i < sent.Count; ++i)
                WriteRequest(writer, sent[i]);
            writer.Write(received.Count);
            for (int i = 0; i < received.Count; ++i)
                WriteRequest(writer, received[i]);
        }

        private static void WriteRequest(BinaryWriter writer, Request r)
        {
            writer.Write((byte)r.status);
            writer.WriteBytesFull(r.gameId.ToByteArray());
            writer.Write(r.Player);
            writer.Write(r.PlayerName);
            writer.Write(r.data != null && r.data.Length != 0);
            if (r.data != null && r.data.Length != 0)
                writer.WriteBytesFull(r.data);

        }

        public override void Deserialize(BinaryReader reader)
        {
            int sentCount = reader.ReadInt32();
            for (int i = 0; i < sentCount; ++i)
            {
                sent.Add(ReadRequest(reader));
            }
            int receivedCount = reader.ReadInt32();
            for (int i = 0; i < receivedCount; ++i)
            {
                received.Add(ReadRequest(reader));
            }
        }

        private Request ReadRequest(BinaryReader reader)
        {
            Request r = new Request();
            r.status = (ChallengeRequestsMessage.Request.Status)reader.ReadByte();
            r.gameId = new Guid(reader.ReadBytesAndSize());
            r.Player = reader.ReadString();
            r.PlayerName = reader.ReadString();
            if (reader.ReadBoolean())
                r.data = reader.ReadBytesAndSize();
            else
                r.data = new byte[0];
            return r;
        }
    }

    public class ChallengeMessage : MessageBase
    {
        public string Player;
        public string PlayerName;
        public byte[] data;
        public Guid GameKey;
        public uint TimeOut = 15;

        public override void Serialize(BinaryWriter writer)
        {
            writer.Write(TimeOut);
            writer.Write(Player);
            writer.Write(PlayerName);
            writer.Write(data != null && data.Length != 0);
            if (data != null && data.Length != 0)
                writer.WriteBytesFull(data);
            writer.WriteBytesFull(GameKey.ToByteArray());
        }

        public override void Deserialize(BinaryReader reader)
        {
            TimeOut = reader.ReadUInt32();
            Player = reader.ReadString();
            PlayerName = reader.ReadString();
            if (reader.ReadBoolean())
                data = reader.ReadBytesAndSize();
            else
                data = new byte[0];
            GameKey = new Guid(reader.ReadBytesAndSize());

        }
    }

    public class FriendlistMessage : MessageBase
    {
        public string friend;
        public string friendName;
        public byte[] data;
        public OnlineStatus status;

        public FriendlistMessage()
        {
            friend = "";
            friendName = "";
            data = new byte[0];
        }

        public override void Serialize(BinaryWriter writer)
        {
            writer.Write((byte)status);
            writer.Write(friend);
            writer.Write(friendName);
            writer.Write(data != null && data.Length != 0);
            if (data != null && data.Length != 0)
                writer.WriteBytesFull(data);
        }

        public override void Deserialize(BinaryReader reader)
        {
            status = (OnlineStatus)reader.ReadByte();
            friend = reader.ReadString();
            friendName = reader.ReadString();
            if (reader.ReadBoolean())
                data = reader.ReadBytesAndSize();
            else
                data = new byte[0];
        }
    }

    //add tests
    public class GameFinishedEvent : MessageBase
    {
        public Guid Game;
        public bool FinishedWithDisconnect;

        public override void Serialize(BinaryWriter writer)
        {
            writer.Write(FinishedWithDisconnect);
            writer.WriteBytesFull(Game.ToByteArray());
        }

        public override void Deserialize(BinaryReader reader)
        {
            FinishedWithDisconnect = reader.ReadBoolean();
            Game = new Guid(reader.ReadBytesAndSize());
        }
    }

    //add tests
    public class PlayerAddedToGameEvent : MessageBase
    {
        public Guid Game;
        public string Player;

        public override void Serialize(BinaryWriter writer)
        {
            writer.Write(Player);
            writer.WriteBytesFull(Game.ToByteArray());
        }

        public override void Deserialize(BinaryReader reader)
        {
            Player = reader.ReadString();
            Game = new Guid(reader.ReadBytesAndSize());
        }
    }

    /// <summary>
    /// This is used to broadcast a property change in match to players
    /// </summary>
    public class MatchPropertyChangeData : MessageBase
    {
        public Guid Game;
        public string Player;
        public string key;
        public byte[] value;
        public bool isGameProperty { get { return string.IsNullOrEmpty(Player); } }

        public MatchPropertyChangeData()
        {
            value = new byte[0];
        }

        public override void Serialize(BinaryWriter writer)
        {
            writer.WriteBytesFull(Game.ToByteArray());
            writer.Write(!string.IsNullOrEmpty(Player));
            if (!string.IsNullOrEmpty(Player))
                writer.Write(Player);
            writer.Write(key);
            writer.WriteBytesFull(value);
        }

        public override void Deserialize(BinaryReader reader)
        {
            Game = new Guid(reader.ReadBytesAndSize());
            if (reader.ReadBoolean())
                Player = reader.ReadString();
            key = reader.ReadString();
            value = reader.ReadBytesAndSize();
        }
    }

    public class PendingMatchesCollectionData : MessageBase
    {
        public class PendingMatchInfo : MessageBase
        {
            public Guid matchId;
            public DateTime startTime;
            public string[] players;

            public PendingMatchInfo()
            {
                players = new string[0];
            }

            public override void Serialize(BinaryWriter writer)
            {
                writer.WriteBytesFull(matchId.ToByteArray());
                writer.Write(startTime.ToString());
                writer.Write(players.Length);
                for (int i = 0; i < players.Length; ++i)
                    writer.Write(players[i]);
            }

            public override void Deserialize(BinaryReader reader)
            {
                matchId = new Guid(reader.ReadBytesAndSize());
                startTime = DateTime.Parse(reader.ReadString());
                int count = reader.ReadInt32();
                players = new string[count];
                for (int i = 0; i < count; ++i)
                {
                    players[i] = reader.ReadString();
                }
            }
        }

        public PendingMatchInfo[] matches;

        public PendingMatchesCollectionData()
        {
            matches = new PendingMatchInfo[0];
        }

        public override void Serialize(BinaryWriter writer)
        {
            writer.Write(matches.Length);
            for (int i = 0; i < matches.Length; ++i)
                writer.Write(matches[i]);
        }

        public override void Deserialize(BinaryReader reader)
        {
            int count = reader.ReadInt32();
            matches = new PendingMatchInfo[count];
            for (int i = 0; i < count; ++i)
            {
                PendingMatchInfo f = new PendingMatchInfo();
                f.Deserialize(reader);
                matches[i] = f;
            }
        }

    }

    public class SetMatchPropertyData : MessageBase
    {
        public Guid Game;
        public bool isPlayerProperty;
        public string key;
        public byte[] value;

        public override void Serialize(BinaryWriter writer)
        {
            writer.WriteBytesFull(Game.ToByteArray());
            writer.Write(isPlayerProperty);
            writer.Write(key);
            writer.WriteBytesFull(value);
        }

        public override void Deserialize(BinaryReader reader)
        {
            Game = new Guid(reader.ReadBytesAndSize());
            isPlayerProperty = reader.ReadBoolean();
            key = reader.ReadString();
            value = reader.ReadBytesAndSize();
        }
    }


    //Add test
    public class MatchPlayerEventData : MessageBase
    {
        public Guid Game;
        public string Player;

        public override void Serialize(BinaryWriter writer)
        {
            writer.Write(Player);
            writer.WriteBytesFull(Game.ToByteArray());
        }

        public override void Deserialize(BinaryReader reader)
        {
            Player = reader.ReadString();
            Game = new Guid(reader.ReadBytesAndSize());
        }
    }

    public class MatchSyncEventData : MessageBase
    {
        public Guid Game;
        public Dictionary<string, byte[]> gameProperties;
        public Dictionary<string, Dictionary<string, byte[]>> playerProperties;

        public MatchSyncEventData()
        {
            gameProperties = new Dictionary<string, byte[]>();
            playerProperties = new Dictionary<string, Dictionary<string, byte[]>>();
        }

        public override void Serialize(BinaryWriter writer)
        {
            writer.WriteBytesFull(Game.ToByteArray());
            writer.Write(gameProperties.Count);
            foreach (var gameProperty in gameProperties)
            {
                writer.Write(gameProperty.Key);
                writer.WriteBytesFull(gameProperty.Value);
            }
            writer.Write(playerProperties.Count);
            foreach (var playerPropertyBag in playerProperties)
            {
                if (playerPropertyBag.Value.Count > 0)
                {
                    writer.Write(playerPropertyBag.Key);
                    writer.Write(playerPropertyBag.Value.Count);
                    foreach (var kvp in playerPropertyBag.Value)
                    {
                        writer.Write(kvp.Key);
                        writer.WriteBytesFull(kvp.Value);
                    }
                }
            }
        }

        public override void Deserialize(BinaryReader reader)
        {
            Game = new Guid(reader.ReadBytesAndSize());
            int gamePropertyCount = reader.ReadInt32();
            for (int i = 0; i < gamePropertyCount; ++i)
            {
                string key = reader.ReadString();
                byte[] value = reader.ReadBytesAndSize();
                gameProperties.Add(key, value);
            }
            int playerCountInPropertyBag = reader.ReadInt32();
            for (int i = 0; i < playerCountInPropertyBag; ++i)
            {
                string playerKey = reader.ReadString();
                int playerPropCount = reader.ReadInt32();
                playerProperties.Add(playerKey, new Dictionary<string, byte[]>());
                for (int j = 0; j < playerPropCount; ++j)
                {
                    string key = reader.ReadString();
                    byte[] value = reader.ReadBytesAndSize();
                    playerProperties[playerKey][key] = value;
                }
            }
        }
    }

    public class SetLeaderBoardRequest : MessageBase
    {
        public string leaderBoardName;
        public int score;

        public override void Serialize(BinaryWriter writer)
        {
            writer.Write(leaderBoardName);
            writer.Write(score);
        }

        public override void Deserialize(BinaryReader reader)
        {
            leaderBoardName = reader.ReadString();
            score = reader.ReadInt32();
        }
    }

    public class GetLeaderBoardRequest : MessageBase
    {
        public enum RequestType : byte
        {
            Top,
            Around,
            Me
        }

        public string leaderBoardName;
        public RequestType type;
        public int count;
        public int offset;

        public override void Serialize(BinaryWriter writer)
        {
            writer.Write((byte)type);
            writer.Write(leaderBoardName);
            if (type != RequestType.Me)
                writer.Write(count);
            if (type == RequestType.Top)
                writer.Write(offset);
        }

        public override void Deserialize(BinaryReader reader)
        {
            type = (RequestType)reader.ReadByte();
            leaderBoardName = reader.ReadString();
            if (type != RequestType.Me)
                count = reader.ReadInt32();
            if (type == RequestType.Top)
                offset = reader.ReadInt32();
        }
    }

    public class SetPublisherDataRequest : MessageBase
    {
        public string key;
        public string value;
        public Guid developerKey;
        public Guid developerSecret;

        public SetPublisherDataRequest()
        {
            developerKey = Guid.Empty;
            developerSecret = Guid.Empty;
        }

        public override void Serialize(BinaryWriter writer)
        {
            writer.WriteBytesFull(developerKey.ToByteArray());
            writer.WriteBytesFull(developerSecret.ToByteArray());
            writer.Write(key);
            writer.Write(value);

        }

        public override void Deserialize(BinaryReader reader)
        {
            developerKey = new Guid(reader.ReadBytesAndSize());
            developerSecret = new Guid(reader.ReadBytesAndSize());
            key = reader.ReadString();
            value = reader.ReadString();
        }
    }

    public class GetInventoryResponseMessage : MessageBase
    {
        public (string, int)[] items;

        public override void Serialize(BinaryWriter writer)
        {
            base.Serialize(writer);
            writer.Write(items.Length);
            for (int i = 0; i < items.Length; ++i)
            {
                writer.Write(items[i].Item1);
                writer.Write(items[i].Item2);
            }
        }

        public override void Deserialize(BinaryReader reader)
        {
            base.Deserialize(reader);
            int length = reader.ReadInt32();
            items = new (string, int)[length];
            for (int i = 0; i < length; ++i)
            {
                items[i].Item1 = reader.ReadString();
                items[i].Item2 = reader.ReadInt32();
            }
        }
    }

    public class GetInventoryUnlockTimeResponseMessage : MessageBase
    {
        public TimeSpan time;

        public override void Serialize(BinaryWriter writer)
        {
            base.Serialize(writer);
            writer.Write(time.TotalMilliseconds);
        }

        public override void Deserialize(BinaryReader reader)
        {
            base.Deserialize(reader);
            var mil = reader.ReadDouble();
            time = TimeSpan.FromMilliseconds(mil);
        }
    }

    public class BuyItemInventoryRequestMessage : MessageBase
    {
        public string inventoryName;
        public (string, int)[] items;

        public override void Serialize(BinaryWriter writer)
        {
            base.Serialize(writer);
            writer.Write(inventoryName);
            writer.Write(items.Length);
            for (int i = 0; i < items.Length; ++i)
            {
                writer.Write(items[i].Item1);
                writer.Write(items[i].Item2);
            }
        }

        public override void Deserialize(BinaryReader reader)
        {
            base.Deserialize(reader);
            inventoryName = reader.ReadString();
            int length = reader.ReadInt32();
            items = new (string, int)[length];
            for (int i = 0; i < length; ++i)
            {
                items[i].Item1 = reader.ReadString();
                items[i].Item2 = reader.ReadInt32();
            }
        }
    }

    public class RemoveItemInventoryRequestMessage : MessageBase
    {
        public string inventoryName;
        public (string, int)[] items;

        public override void Serialize(BinaryWriter writer)
        {
            base.Serialize(writer);
            writer.Write(inventoryName);
            writer.Write(items.Length);
            for (int i = 0; i < items.Length; ++i)
            {
                writer.Write(items[i].Item1);
                writer.Write(items[i].Item2);
            }
        }

        public override void Deserialize(BinaryReader reader)
        {
            base.Deserialize(reader);
            inventoryName = reader.ReadString();
            int length = reader.ReadInt32();
            items = new (string, int)[length];
            for (int i = 0; i < length; ++i)
            {
                items[i].Item1 = reader.ReadString();
                items[i].Item2 = reader.ReadInt32();
            }
        }
    }



    public class GetPublisherDataRequest : MessageBase
    {
        public string key;
        public Guid developerKey;
        public Guid developerSecret;

        public GetPublisherDataRequest()
        {
            developerKey = Guid.Empty;
            developerSecret = Guid.Empty;
        }

        public override void Serialize(BinaryWriter writer)
        {
            writer.WriteBytesFull(developerKey.ToByteArray());
            writer.WriteBytesFull(developerSecret.ToByteArray());
            writer.Write(key);

        }

        public override void Deserialize(BinaryReader reader)
        {
            developerKey = new Guid(reader.ReadBytesAndSize());
            developerSecret = new Guid(reader.ReadBytesAndSize());
            key = reader.ReadString();
        }
    }

    public class SetCloudDataRequest : MessageBase
    {
        public string key;
        public string value;

        public override void Serialize(BinaryWriter writer)
        {
            writer.Write(key);
            writer.Write(value);
        }

        public override void Deserialize(BinaryReader reader)
        {
            key = reader.ReadString();
            value = reader.ReadString();
        }
    }

    //no test
    public class GetPlayerPublicCloudDataRequest : MessageBase
    {
        public string player;
        public string key;

        public override void Serialize(BinaryWriter writer)
        {
            writer.Write(player);
            writer.Write(key);
        }

        public override void Deserialize(BinaryReader reader)
        {
            player = reader.ReadString();
            key = reader.ReadString();
        }
    }

    //no test
    public class GetPlayerPublicCloudDataResponse : MessageBase
    {
        public bool hasValue;
        public string value;

        public override void Serialize(BinaryWriter writer)
        {
            writer.Write(hasValue);
            writer.Write(value);
        }

        public override void Deserialize(BinaryReader reader)
        {
            hasValue = reader.ReadBoolean();
            if (hasValue)
                value = reader.ReadString();
            else
                value = "";
        }
    }

    //no test
    public class GetPublicCloudDataResponse : MessageBase
    {
        public bool hasValue;
        public string value;

        public override void Serialize(BinaryWriter writer)
        {
            writer.Write(hasValue);
            writer.Write(value);
        }

        public override void Deserialize(BinaryReader reader)
        {
            hasValue = reader.ReadBoolean();
            if (hasValue)
                value = reader.ReadString();
            else
                value = "";
        }
    }

    public class LeaderBoardEntry : MessageBase
    {
        public string playerId;
        public string userName;
        public int score;
        public DateTime entryTime;

        //Not serialized on the network
        public int Rank;

        public override void Serialize(BinaryWriter writer)
        {
            writer.Write(playerId);
            writer.Write(userName);
            writer.Write(score);
            writer.Write(entryTime.ToString());
        }

        public override void Deserialize(BinaryReader reader)
        {
            playerId = reader.ReadString();
            userName = reader.ReadString();
            score = reader.ReadInt32();
            entryTime = DateTime.Parse(reader.ReadString());
        }
    }

    public class GetLeaderBoardResponse : MessageBase
    {
        public List<LeaderBoardEntry> entries;
        public int playerRank = -1;
        public int count = -1;

        public bool IsPlayerRank { get { return entries.Count == 0 && playerRank != -1; } }
        public bool IsAroundMeResult { get { return entries.Count > 0 && playerRank != -1; } }
        public bool IsTopResult { get { return entries.Count > 0 && playerRank == -1; } }

        public override void Serialize(BinaryWriter writer)
        {
            writer.Write(playerRank);
            writer.Write(count);
            writer.Write(entries.Count);
            for (int i = 0; i < entries.Count; ++i)
            {
                writer.Write(entries[i]);
            }
        }

        public override void Deserialize(BinaryReader reader)
        {
            playerRank = reader.ReadInt32();
            count = reader.ReadInt32();
            int entryCount = reader.ReadInt32();
            entries = new List<LeaderBoardEntry>(entryCount);
            for (int i = 0; i < entryCount; ++i)
            {
                LeaderBoardEntry ent = new LeaderBoardEntry();
                ent.Deserialize(reader);
                entries.Add(ent);
            }
        }

        public GetLeaderBoardResponse()
        {
            entries = new List<LeaderBoardEntry>();
        }
    }

    public class GetFriendsRequestMessage : MessageBase
    {
        public string[] publicKeys;

        public GetFriendsRequestMessage()
        {
            publicKeys = new string[0];
        }

        public override void Serialize(BinaryWriter writer)
        {
            writer.Write(publicKeys.Length);
            for (int i = 0; i < publicKeys.Length; ++i)
            {
                writer.Write(publicKeys[i]);
            }
        }

        public override void Deserialize(BinaryReader reader)
        {
            int count = reader.ReadInt32();
            for (int i = 0; i < count; ++i)
            {
                publicKeys[i] = reader.ReadString();
            }
        }
    }

    public class FriendlistEntry : MessageBase
    {
        public string userId = "";
        public string name = "";
        public OnlineStatus status;
        public Dictionary<string, string> data;

        public FriendlistEntry()
        {
            data = new Dictionary<string, string>();
        }

        public override void Serialize(BinaryWriter writer)
        {
            writer.Write(userId);
            writer.Write(name);
            writer.Write((byte)status);
            writer.Write(data.Count);
            foreach (var kvp in data)
            {
                writer.Write(kvp.Key);
                writer.Write(kvp.Value);
            }
        }

        public override void Deserialize(BinaryReader reader)
        {
            userId = reader.ReadString();
            name = reader.ReadString();
            status = (OnlineStatus)reader.ReadByte();
            int cout = reader.ReadInt32();
            for (int i = 0; i < cout; ++i)
            {
                string key = reader.ReadString();
                string value = reader.ReadString();
                data.Add(key, value);
            }
        }
    }

    public class FriendInvitationCollection : MessageBase
    {
        public List<FriendInvitation> invitations;

        public FriendInvitationCollection()
        {
            invitations = new List<FriendInvitation>();
        }

        public override void Serialize(BinaryWriter writer)
        {
            writer.Write(invitations.Count);
            for (int i = 0; i < invitations.Count; ++i)
                writer.Write(invitations[i]);
        }

        public override void Deserialize(BinaryReader reader)
        {
            int count = reader.ReadInt32();
            for (int i = 0; i < count; ++i)
            {
                FriendInvitation f = new FriendInvitation();
                f.Deserialize(reader);
                invitations.Add(f);
            }
        }
    }

    public class FriendInvitation : MessageBase
    {
        public DateTime creationTime;
        public string otherPlayerName = "";
        public string OtherPlayerId = "";
        public byte[] data = new byte[0];


        public override void Serialize(BinaryWriter writer)
        {
            writer.Write(OtherPlayerId);
            writer.Write(otherPlayerName);
            writer.Write(creationTime.ToString());
            if (data == null)
                data = new byte[] { };
            writer.WriteBytesFull(data);
        }

        public override void Deserialize(BinaryReader reader)
        {
            OtherPlayerId = reader.ReadString();
            otherPlayerName = reader.ReadString();
            creationTime = DateTime.Parse(reader.ReadString());
            data = reader.ReadBytesAndSize();
        }

        /// <summary>
        /// Reads a message from the data sent
        /// </summary>
        /// <typeparam name="T"></typeparam>
        /// <returns></returns>
        public T GetMessage<T>() where T : MessageBase, new()
        {
            T t = new T();
            using (MemoryStream str = new MemoryStream(data))
            {
                using (BinaryReader reader = new BinaryReader(str))
                {
                    t.Deserialize(reader);
                }
            }
            return t;
        }
    }

    public class FriendlistCollection : MessageBase
    {
        public List<FriendlistEntry> friends;
        public FriendInvitationCollection sentInvitation;
        public FriendInvitationCollection receivedInvitations;

        public FriendlistCollection()
        {
            friends = new List<FriendlistEntry>();
            sentInvitation = new FriendInvitationCollection();
            receivedInvitations = new FriendInvitationCollection();
        }

        public override void Serialize(BinaryWriter writer)
        {
            writer.Write(friends.Count);
            for (int i = 0; i < friends.Count; ++i)
            {
                writer.Write(friends[i]);
            }
            writer.Write(sentInvitation);
            writer.Write(receivedInvitations);
        }

        public override void Deserialize(BinaryReader reader)
        {
            int count = reader.ReadInt32();
            for (int i = 0; i < count; ++i)
            {
                FriendlistEntry f = new FriendlistEntry();
                f.Deserialize(reader);
                friends.Add(f);
            }
            sentInvitation.Deserialize(reader);
            receivedInvitations.Deserialize(reader);
        }

    }

    public class InviteFriendRequest : MessageBase
    {
        public InvitationType type;
        public string value;
        public byte[] data;

        public override void Serialize(BinaryWriter writer)
        {
            writer.Write(value);
            writer.Write((byte)type);
            writer.WriteBytesFull(data);
        }

        public override void Deserialize(BinaryReader reader)
        {
            value = reader.ReadString();
            type = (InvitationType)reader.ReadByte();
            data = reader.ReadBytesAndSize();
        }
    }

    public class FindMatchRequest : MessageBase
    {
        public string matchType;
        public uint skill;
        public uint waitTime;

        public FindMatchRequest()
        {
            matchType = "";
        }

        public FindMatchRequest(string matchType, uint skill, uint timeout)
        {
            this.matchType = matchType;
            this.skill = skill;
            this.waitTime = timeout;
        }

        public override void Serialize(BinaryWriter writer)
        {
            writer.Write(matchType);
            writer.Write(skill);
            writer.Write(waitTime);
        }

        public override void Deserialize(BinaryReader reader)
        {
            matchType = reader.ReadString();
            skill = reader.ReadUInt32();
            waitTime = reader.ReadUInt32();
        }

    }

    public class FindMatchResponse : MessageBase
    {
        public Guid gameId;
        public string data;
        public string[] players;

        public FindMatchResponse()
        {
            gameId = Guid.Empty;
            data = "";
            players = new string[0];
        }

        public FindMatchResponse(string gameId, string data, string[] players)
        {
            this.gameId = Guid.Empty;
            this.data = data;
            this.players = players;

        }

        public override void Serialize(BinaryWriter writer)
        {
            var gid = gameId.ToByteArray();
            writer.WriteBytesFull(gid);
            writer.Write(data);
            writer.Write(players.Length);
            for (int i = 0; i < players.Length; ++i)
                writer.Write(players[i]);
        }

        public override void Deserialize(BinaryReader reader)
        {
            gameId = new Guid(reader.ReadBytesAndSize());
            data = reader.ReadString();
            int size = reader.ReadInt32();
            players = new string[size];
            for (int i = 0; i < size; ++i)
            {
                players[i] = reader.ReadString();
            }
        }

    }

    /// <summary>
    /// This is an email activation request
    /// </summary>
    public class EmailActivationRequest : MessageBase
    {
        /// <summary>
        /// Ticket of the current session
        /// </summary>
        public string ticket;

        /// <summary>
        /// email of the user to be activated
        /// </summary>
        public string email;

        /// <summary>
        /// The activation code
        /// </summary>
        public string code;

        public override void Serialize(BinaryWriter writer)
        {
            writer.Write(ticket);
            writer.Write(email);
            writer.Write(code);
        }

        public override void Deserialize(BinaryReader reader)
        {
            ticket = reader.ReadString();
            email = reader.ReadString();
            code = reader.ReadString();
        }
    }

    /// <summary>
    /// This is a request message for authenticating /logging in a user
    /// </summary>
    public class AuthenticationRequest : MessageBase
    {
        /// <summary>
        /// The current session's ticket
        /// </summary>
        public string ticket = "";

        /// <summary>
        /// The type of login
        /// </summary>
        public LoginType LoginType;

        /// <summary>
        /// The Id based on LoginType which can be email, user ID, device ID
        /// or anyting else for custom type
        /// </summary>
        public string Id = "";

        /// <summary>
        /// MD5 hash of the password
        /// </summary>
        public string PasswordHash = "";

        /// <summary>
        /// Should we automatically create an account if one doesn't exist, only applicable to User and Password and 
        /// device authentication
        /// </summary>
        public bool AutoCreate;

        /// <summary>
        /// A set of arbitrary data for the custom login types
        /// </summary>
        public Dictionary<string, string> data = new Dictionary<string, string>();


        public override void Serialize(BinaryWriter writer)
        {
            writer.Write(ticket);
            writer.Write(((byte)LoginType));
            writer.Write(Id);
            writer.Write(PasswordHash);
            writer.Write(AutoCreate);
            if (data == null)
                data = new Dictionary<string, string>();
            writer.Write((byte)data.Count);
            foreach (var kvp in data)
            {
                writer.Write(kvp.Key);
                writer.Write(kvp.Value);
            }
        }

        public override void Deserialize(BinaryReader reader)
        {
            ticket = reader.ReadString();
            LoginType = (LoginType)reader.ReadByte();
            Id = reader.ReadString();
            PasswordHash = reader.ReadString();
            AutoCreate = reader.ReadBoolean();
            int count = reader.ReadByte();
            for (int i = 0; i < count; ++i)
            {
                string key = reader.ReadString();
                string value = reader.ReadString();
                data.Add(key, value);
            }
        }

        public AuthenticationRequest()
        {
            data = new Dictionary<string, string>();
        }
    }

    /// <summary>
    /// This is the request for adding authentication to the account
    /// </summary>
    public class AddAuthenticationToAccountRequest : MessageBase
    {
        /// <summary>
        /// The type of login
        /// </summary>
        public LoginType LoginType;

        /// <summary>
        /// The Id based on LoginType which can be email, user ID, device ID
        /// or anyting else for custom type
        /// </summary>
        public string Id;

        /// <summary>
        /// MD5 hash of the password
        /// </summary>
        public string PasswordHash;

        /// <summary>
        /// A set of arbitrary data for the custom login types
        /// </summary>
        public Dictionary<string, string> data;


        public override void Serialize(BinaryWriter writer)
        {
            writer.Write(((byte)LoginType));
            writer.Write(Id);
            writer.Write(PasswordHash);

            writer.Write((byte)data.Count);
            foreach (var kvp in data)
            {
                writer.Write(kvp.Key);
                writer.Write(kvp.Value);
            }
        }

        public override void Deserialize(BinaryReader reader)
        {
            LoginType = (LoginType)reader.ReadByte();
            Id = reader.ReadString();
            PasswordHash = reader.ReadString();
            int count = reader.ReadByte();
            for (int i = 0; i < count; ++i)
            {
                string key = reader.ReadString();
                string value = reader.ReadString();
                data.Add(key, value);
            }
        }

        public AddAuthenticationToAccountRequest()
        {
            data = new Dictionary<string, string>();
        }
    }

    /// <summary>
    /// This is a request to create a new account
    /// </summary>
    public class CreateAccountRequest : MessageBase
    {
        /// <summary>
        /// Session ticket
        /// </summary>
        public string ticket;

        /// <summary>
        /// The type of account which you want to create
        /// </summary>
        public LoginType LoginType;

        /// <summary>
        /// Relevant Id based on account type (LoginType). For example it is an email address when creating
        /// an email login or a user ID when making user and password account
        /// </summary>
        public string Id;

        /// <summary>
        /// The MD5 hash of the password if applicable to the login type
        /// </summary>
        public string PasswordHash;

        /// <summary>
        /// A dictionary of arbitrary data for custom accounts
        /// </summary>
        public Dictionary<string, string> data;

        public override void Serialize(BinaryWriter writer)
        {
            writer.Write(ticket);
            writer.Write(((byte)LoginType));
            writer.Write(Id);
            writer.Write(PasswordHash);

            writer.Write((byte)data.Count);
            foreach (var kvp in data)
            {
                writer.Write(kvp.Key);
                writer.Write(kvp.Value);
            }
        }

        public override void Deserialize(BinaryReader reader)
        {
            ticket = reader.ReadString();
            LoginType = (LoginType)reader.ReadByte();
            Id = reader.ReadString();
            PasswordHash = reader.ReadString();

            int count = reader.ReadByte();
            for (int i = 0; i < count; ++i)
            {
                string key = reader.ReadString();
                string value = reader.ReadString();
                data.Add(key, value);
            }
        }

        public CreateAccountRequest()
        {
            data = new Dictionary<string, string>();
        }
    }

    public class AuthenticationResponse : MessageBase
    {
        public string name;
        public string userId;
        public string ticket;
        public bool isNewlyCreated;

        public AuthenticationResponse()
        {
            name = userId = ticket = "";
        }

        public override void Serialize(BinaryWriter writer)
        {
            writer.Write(userId);
            writer.Write(name);
            writer.Write(ticket);
            writer.Write(isNewlyCreated);
        }

        public override void Deserialize(BinaryReader reader)
        {
            userId = reader.ReadString();
            name = reader.ReadString();
            ticket = reader.ReadString();
            isNewlyCreated = reader.ReadBoolean();
        }
    }

    public class ScriptEventExecutionRequestMessage : MessageBase
    {
        public string eventName;
        public ParamType[] parameterTypess;
        public object[] parameters;

        /// <summary>
        /// None-typeSafe serialization will be supported later on, added to serialization format for
        /// compatiblity purposes
        /// For now this should always be true
        /// </summary>
        private bool isTypeSafe = true;

        public ScriptEventExecutionRequestMessage(string eventName, int parameterCount)
        {
            this.eventName = eventName;
            parameters = new object[parameterCount];
            parameterTypess = new ParamType[parameterCount];
        }

        public void SetParameter<T>(int index, T value)
        {
            if (index < 0 || index > (parameters.Length - 1))
                throw new ArgumentException("Index should be between 0 and paramterCount - 1");
            parameters[index] = value;
        }

        public void SetParameterType(int index, ParamType value)
        {
            if (index < 0 || index > (parameters.Length - 1))
                throw new ArgumentException("Index should be between 0 and paramterCount - 1");
            parameterTypess[index] = value;
        }

        public void SetParametersAndTypes(params object[] args)
        {
            this.parameters = new object[args.Length];
            this.parameterTypess = new ParamType[args.Length];
            for (int i = 0; i < args.Length; ++i)
            {
                var tp = GetTypeBasedOnParameter(args[i]);
                parameters[i] = args[i];
                parameterTypess[i] = tp;
            }
        }

        private ParamType GetTypeBasedOnParameter(object returnValue)
        {
            Type t = returnValue.GetType();
            if (t == typeof(int))
                return ParamType.IntParam;
            if (t == typeof(short))
                return ParamType.ShortParam;
            if (t == typeof(long))
                return ParamType.LongParam;
            if (t == typeof(bool))
                return ParamType.BoolParam;
            if (t == typeof(byte[]))
                return ParamType.ByteArrayParam;
            if (t == typeof(byte))
                return ParamType.ByteParam;
            if (t == typeof(float))
                return ParamType.FloatParam;
            if (t == typeof(double))
                return ParamType.DoubleParam;
            if (t == typeof(string))
                return ParamType.StringParam;
            throw new ArgumentException("Invalid argument type");
        }

        public ScriptEventExecutionRequestMessage()
        {
            parameters = new object[0];
            parameterTypess = new ParamType[0];
            eventName = "";
        }

        public override void Serialize(BinaryWriter writer)
        {
            if (isTypeSafe)
            {
                writer.Write(isTypeSafe);
                writer.Write(eventName);
                writer.Write(parameterTypess.Length);
                for (int i = 0; i < parameterTypess.Length; ++i)
                {
                    writer.Write((byte)parameterTypess[i]);
                    WriteValueBasedOnType(writer, parameterTypess[i], parameters[i]);
                }
            }
        }

        private void WriteValueBasedOnType(BinaryWriter writer, ParamType type, object value)
        {
            switch (type)
            {
                case ParamType.BoolParam:
                    writer.Write((bool)value);
                    break;
                case ParamType.ByteArrayParam:
                    writer.WriteBytesFull((byte[])value);
                    break;
                case ParamType.ByteParam:
                    writer.Write((byte)value);
                    break;
                case ParamType.DoubleParam:
                    writer.Write((double)value);
                    break;
                case ParamType.FloatParam:
                    writer.Write((float)value);
                    break;
                case ParamType.IntParam:
                    writer.Write((int)value);
                    break;
                case ParamType.LongParam:
                    writer.Write((long)value);
                    break;
                case ParamType.ShortParam:
                    writer.Write((short)value);
                    break;
                case ParamType.StringParam:
                    writer.Write((string)value);
                    break;
            }
        }

        public override void Deserialize(BinaryReader reader)
        {
            isTypeSafe = reader.ReadBoolean();
            if (isTypeSafe)
            {
                eventName = reader.ReadString();
                int paramCount = reader.ReadInt32();
                parameterTypess = new ParamType[paramCount];
                parameters = new object[paramCount];
                for (int i = 0; i < paramCount; ++i)
                {
                    parameterTypess[i] = (ParamType)reader.ReadByte();
                    parameters[i] = ReadValueBasedOnParamType(reader, parameterTypess[i]);
                }
            }
        }

        private object ReadValueBasedOnParamType(BinaryReader reader, ParamType type)
        {
            object readValue = null;
            switch (type)
            {

                case ParamType.BoolParam:
                    readValue = reader.ReadBoolean();
                    break;
                case ParamType.ByteArrayParam:
                    readValue = reader.ReadBytesAndSize();
                    break;
                case ParamType.ByteParam:
                    readValue = reader.ReadByte();
                    break;
                case ParamType.DoubleParam:
                    readValue = reader.ReadDouble();
                    break;
                case ParamType.FloatParam:
                    readValue = reader.ReadSingle();
                    break;
                case ParamType.IntParam:
                    readValue = reader.ReadInt32();
                    break;
                case ParamType.LongParam:
                    readValue = reader.ReadInt64();
                    break;
                case ParamType.ShortParam:
                    readValue = reader.ReadInt16();
                    break;
                case ParamType.StringParam:
                    readValue = reader.ReadString();
                    break;
            }
            return readValue;
        }

    }

    public class ScriptEventExecutionResponseMessage : MessageBase
    {
        public ParamType returnType;
        public bool hasReturnValue;
        public object returnValue;

        public override void Serialize(BinaryWriter writer)
        {
            writer.Write(hasReturnValue);
            if (hasReturnValue)
            {
                writer.Write((byte)returnType);
                WriteReturnValueBasedOnType(writer, returnType, returnValue);
            }

        }

        private void WriteReturnValueBasedOnType(BinaryWriter writer, ParamType type, object value)
        {
            switch (type)
            {
                case ParamType.BoolParam:
                    writer.Write((bool)value);
                    break;
                case ParamType.ByteArrayParam:
                    writer.WriteBytesFull((byte[])value);
                    break;
                case ParamType.ByteParam:
                    writer.Write((byte)value);
                    break;
                case ParamType.DoubleParam:
                    writer.Write((double)value);
                    break;
                case ParamType.FloatParam:
                    writer.Write((float)value);
                    break;
                case ParamType.IntParam:
                    writer.Write((int)value);
                    break;
                case ParamType.LongParam:
                    writer.Write((long)value);
                    break;
                case ParamType.ShortParam:
                    writer.Write((short)value);
                    break;
                case ParamType.StringParam:
                    writer.Write((string)value);
                    break;
            }
        }

        public override void Deserialize(BinaryReader reader)
        {
            hasReturnValue = reader.ReadBoolean();
            if (hasReturnValue)
            {
                returnType = (ParamType)reader.ReadByte();
                returnValue = ReadReturnTypeBasedOnParamType(reader, returnType);
            }

        }

        private object ReadReturnTypeBasedOnParamType(BinaryReader reader, ParamType type)
        {
            object readValue = null;
            switch (type)
            {

                case ParamType.BoolParam:
                    readValue = reader.ReadBoolean();
                    break;
                case ParamType.ByteArrayParam:
                    readValue = reader.ReadBytesAndSize();
                    break;
                case ParamType.ByteParam:
                    readValue = reader.ReadByte();
                    break;
                case ParamType.DoubleParam:
                    readValue = reader.ReadDouble();
                    break;
                case ParamType.FloatParam:
                    readValue = reader.ReadSingle();
                    break;
                case ParamType.IntParam:
                    readValue = reader.ReadInt32();
                    break;
                case ParamType.LongParam:
                    readValue = reader.ReadInt64();
                    break;
                case ParamType.ShortParam:
                    readValue = reader.ReadInt16();
                    break;
                case ParamType.StringParam:
                    readValue = reader.ReadString();
                    break;
            }
            return readValue;
        }

        public T GetValue<T>()
        {
            if (!hasReturnValue)
                throw new InvalidOperationException("The script response has no return type and you are trying to get the value of it");
            CheckTypeValidity(typeof(T));
            return (T)returnValue;
        }

        private void CheckTypeValidity(System.Type type)
        {
            switch (returnType)
            {
                case ParamType.BoolParam:
                    AssertCorrectType(type, typeof(bool));
                    break;
                case ParamType.ByteArrayParam:
                    AssertCorrectType(type, typeof(byte[]));
                    break;
                case ParamType.ByteParam:
                    AssertCorrectType(type, typeof(byte));
                    break;
                case ParamType.DoubleParam:
                    AssertCorrectType(type, typeof(double));
                    break;
                case ParamType.FloatParam:
                    AssertCorrectType(type, typeof(float));
                    break;
                case ParamType.IntParam:
                    AssertCorrectType(type, typeof(int));
                    break;
                case ParamType.LongParam:
                    AssertCorrectType(type, typeof(long));
                    break;
                case ParamType.ShortParam:
                    AssertCorrectType(type, typeof(short));
                    break;
                case ParamType.StringParam:
                    AssertCorrectType(type, typeof(string));
                    break;
            }
        }

        private static void AssertCorrectType(Type type1, Type type2)
        {
            if (type1 != type2)
                throw new ArgumentException("The generic type doesn't match the type of the value");
        }
    }

    public class OptionalResponse<T> : MessageBase where T : MessageBase, new()
    {
        public bool isSuccesful;
        public string errorMsg;
        public int errorCode;
        public T msg;

        public override void Serialize(BinaryWriter writer)
        {
            writer.Write(isSuccesful);
            if (!isSuccesful)
            {
                writer.Write(errorCode);
                writer.Write(errorMsg);
            }
            else
            {
                msg.Serialize(writer);
            }
        }

        public override void Deserialize(BinaryReader reader)
        {
            isSuccesful = reader.ReadBoolean();
            if (!isSuccesful)
            {
                errorCode = reader.ReadInt32();
                errorMsg = reader.ReadString();
            }
            else
            {
                msg = new T();
                msg.Deserialize(reader);
            }
        }

        public void Succeed(T msg)
        {
            isSuccesful = true;
            this.msg = msg;
        }

        public void Fail(string errorMsg, int errorCode)
        {
            isSuccesful = false;
            this.errorCode = errorCode;
            this.errorMsg = errorMsg;
        }

        public static OptionalResponse<T> CreateSuccessful(T msg)
        {
            var resp = new OptionalResponse<T>();
            resp.Succeed(msg);
            return resp;
        }

        public static OptionalResponse<T> CreateFailed(string errorMsg, sbyte errorCode)
        {
            var resp = new OptionalResponse<T>();
            resp.Fail(errorMsg, errorCode);
            return resp;
        }

    }

    #endregion

    #region GenericMessages

    public class StringAndByteArrayMessage : MessageBase
    {
        public string stringValue;
        public byte[] byteArrayValue;

        public StringAndByteArrayMessage()
        {
            byteArrayValue = new byte[0];
            stringValue = "";
        }

        public override void Serialize(BinaryWriter writer)
        {
            writer.Write(stringValue);
            writer.WriteBytesFull(byteArrayValue);
        }

        public override void Deserialize(BinaryReader reader)
        {
            stringValue = reader.ReadString();
            byteArrayValue = reader.ReadBytesAndSize();
        }
    }

    public class EmptyMessage : MessageBase
    {
        public EmptyMessage()
        {

        }

        public override void Serialize(BinaryWriter writer)
        {
            writer.Write(true);
        }

        public override void Deserialize(BinaryReader reader)
        {
            reader.ReadBoolean();
        }
    }

    public class StringMessage : MessageBase
    {
        public string value = "";

        public StringMessage()
        {
            value = "";
        }

        public override void Serialize(BinaryWriter writer)
        {
            writer.Write(value);
        }

        public override void Deserialize(BinaryReader reader)
        {
            value = reader.ReadString();
        }
    }

    public class IntegerMessage : MessageBase
    {
        public int value;

        public override void Serialize(BinaryWriter writer)
        {
            writer.Write(value);
        }

        public override void Deserialize(BinaryReader reader)
        {
            value = reader.ReadInt32();
        }
    }

    public class GuidMessage : MessageBase
    {
        public Guid value;

        public GuidMessage()
        {
            value = Guid.Empty;
        }

        public override void Serialize(BinaryWriter writer)
        {
            writer.WriteBytesFull(value.ToByteArray());
        }

        public override void Deserialize(BinaryReader reader)
        {
            value = new Guid(reader.ReadBytesAndSize());
        }
    }

    public class RawMessage : MessageBase
    {
        public byte[] value;

        public RawMessage()
        {
            value = new byte[0];
        }

        public override void Serialize(BinaryWriter writer)
        {
            writer.WriteBytesFull(value);
        }

        public override void Deserialize(BinaryReader reader)
        {

            value = reader.ReadBytesAndSize();
        }
    }

    #endregion

    public static class BinaryReaderWriterExtensions
    {
        public static byte[] ReadBytesAndSize(this BinaryReader reader)
        {
            var count = reader.ReadInt32();
            return reader.ReadBytes(count);
        }

        public static void WriteBytesFull(this BinaryWriter writer, byte[] array)
        {
            writer.Write(array.Length);
            writer.Write(array);
        }

        public static void Write<T>(this BinaryWriter writer, T t) where T : MessageBase, new()
        {
            t.Serialize(writer);
        }
    }
}
