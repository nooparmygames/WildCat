using UnityEngine;
using System;
using System.Collections.Generic;
using UnityEngine.Networking;
using RealtimeServer;
using System.Linq;
using IdGen = RequestIdGenerator;

public class Client : WebSocketNetworkTransmitter
{
    public const int MATCHMAKING_TIMEOUT = 15;

    public event Action<int> OnConnected;
    public event Action<NetworkError> OnConnectionFailed;
    public event Action<int> OnDisconnected;
    public event Action<int, NetworkError> OnDisconnectionFailed;
    public event Action<int, int, int, byte[]> OnData;

    private bool shouldOnConnectedBeCalled;
    private bool shouldOnDisconnectedConnectedBeCalled;

    private int connectionId;
    private object lockObject = new object();

    /// <summary>
    /// This is the list of registered handlers.
    /// </summary>
	private Dictionary<ApadanaMsgType, List<Action<ApadanaMessageBase>>> handlers = new Dictionary<ApadanaMsgType, List<Action<ApadanaMessageBase>>>();

    /// <summary>
    /// This queue is used for the callbacks which need to be executed
    /// from update based on a receive message
    /// </summary>
	private Queue<MessageHandlerData> updateCallbackHandlersQueue = new Queue<MessageHandlerData>();

    public void RegisterHandler(ApadanaMsgType type, Action<ApadanaMessageBase> action)
    {
        if (!handlers.ContainsKey(type))
        {
            handlers.Add(type, new List<Action<ApadanaMessageBase>>());
        }
        handlers[type].Add(action);
    }

    public void UnregisterHandler(ApadanaMsgType type, Action<ApadanaMessageBase> action)
    {
        if (handlers.ContainsKey(type))
        {
            if (handlers[type].Contains(action))
            {
                handlers[type].Remove(action);
            }
        }
    }

    public void UnregisterHandlers(ApadanaMsgType type)
    {
        if (handlers.ContainsKey(type))
        {
            handlers[type].Clear();
        }
    }

    public void ClearAllHandlers()
    {
        handlers.Clear();
    }

    public void Connect(string serverIP, int serverPort)
    {
        var err = ConnectToServer(serverIP, serverPort);
        if (err != 0 && OnConnectionFailed != null)
            OnConnectionFailed((NetworkError)err);
    }

    public void Disconnect()
    {
        var err = DisconnectFromServer();
        if (err == 0 && OnDisconnected != null)
        {
            handlers.Clear();
            OnDisconnected(0);
        }
        if (err != 0 && OnDisconnectionFailed != null)
        {
            OnDisconnectionFailed(0, (NetworkError)err);
        }
    }

    public void SendGameMessage(string game, byte[] data, ApadanaMsgType type, string ticket, QosType qos)
    {
        RawMessage msg = new RawMessage { value = data };
        SendGameMessage(game, msg, type, ticket, qos);
    }

    public void SendGameMessage<T>(string game, T msg, ApadanaMsgType type, string ticket, QosType qos) where T : MessageBase, new()
    {
        var msgToSend = ApadanaMessageBase.CreateWithGameTarget(msg, type, ApadanaMsgHeader.normal, game, ticket);
        var data = msgToSend.ToByteArray();
        SendWithQOS(qos, data);
    }

    private void SendWithQOS(QosType qos, byte[] data)
    {
        switch (qos)
        {
            case QosType.Reliable:
                SendReliable(data);
                break;
            case QosType.ReliableFragmented:
                SendReliableLargeMessage(data);
                break;
            case QosType.Unreliable:
                SendUnreliable(data);
                break;
            default:
                throw new ArgumentException("QOS of type " + qos.ToString() + " is not supported");
        }
    }

    public void SendReliable(byte[] data)
    {
        Send(data, 0);
    }

    public void SendUnreliable(byte[] data)
    {
        Send(data, 2);
    }

    public void SendReliableLargeMessage(byte[] data)
    {
        Send(data, 1);
    }

    protected void Send(byte[] data, int channelId)
    {
        lastMessageSent = Time.time;
        Send(0, 0, 0, data);
    }

    #region UserMessages

    public uint SendLoginRequest(LoginType type, string userId, int passwordHash, string provider, Guid sessionKey)
    {
        LoginOldRequest req = new LoginOldRequest
        {
            LoginType = type,
            UserId = userId,
            PasswordHash = passwordHash,
            ProviderName = provider,
            SessionKey = sessionKey.ToString()
        };
        uint requestId = IdGen.GetNextId();
        var msgToSend = ApadanaMessageBase.CreateWithRequestID(req, ApadanaMsgType.LoginRequest, ApadanaMsgHeader.normal, requestId);
        SendReliable(msgToSend.ToByteArray());
        return requestId;
    }

    public uint SendAddAuthToAccountRequest(LoginType loginType, string id, string password, Dictionary<string, string> data, string ticket)
    {
        var req = new AddAuthenticationToAccountRequest
        {
            LoginType = loginType,
            Id = id,
            PasswordHash = password,
            data = data
        };
        uint requestId = IdGen.GetNextId();
        var msgToSend = ApadanaMessageBase.CreateWithAuthDataAndRequestID(req, ApadanaMsgType.AddAuthMechanismToAccountRequest, ApadanaMsgHeader.normal, ticket, requestId);
        SendReliable(msgToSend.ToByteArray());
        return requestId;
    }

    public uint SendAuthenticateRequest(LoginType type, string userId, string passwordHash, Dictionary<string, string> data, bool autoCreate, Guid sessionKey)
    {
        AuthenticationRequest req = new AuthenticationRequest
        {
            LoginType = type,
            Id = userId,
            PasswordHash = passwordHash,
            data = data,
            AutoCreate = autoCreate,
            ticket = sessionKey.ToString()
        };
        if (req.AutoCreate && (req.LoginType != LoginType.Device && req.LoginType != LoginType.UserWithPassowrd))
            throw new ArgumentException("AutoCreate can only be true for user and device requests");
        uint requestId = IdGen.GetNextId();
        var msgToSend = ApadanaMessageBase.CreateWithRequestID(req, ApadanaMsgType.AuthenticateRequest, ApadanaMsgHeader.normal, requestId);
        SendReliable(msgToSend.ToByteArray());
        return requestId;
    }

    public uint SendHeartBeatIfNeeded(string sessionKey)
    {
        if (Time.time - lastMessageSent < 10)
            return 0;
        EmptyMessage req = new EmptyMessage();
        uint requestId = IdGen.GetNextId();
        var msgToSend = ApadanaMessageBase.CreateWithAuthDataAndRequestID(req, ApadanaMsgType.HeartBeatRequest, ApadanaMsgHeader.normal, sessionKey, requestId);
        SendReliable(msgToSend.ToByteArray());
        return requestId;
    }

    public uint SendInviteFriendRequest(InvitationType type, string value, byte[] data, string sessionKey)
    {
        if (data == null)
            data = new byte[0];
        InviteFriendRequest req = new InviteFriendRequest
        {
            type = type,
            value = value,
            data = data
        };

        uint requestId = IdGen.GetNextId();
        var msgToSend = ApadanaMessageBase.CreateWithAuthDataAndRequestID(req, ApadanaMsgType.InviteFriendRequest, ApadanaMsgHeader.normal, sessionKey, requestId);
        SendReliable(msgToSend.ToByteArray());
        return requestId;
    }

    public uint SendGetFriendsRequest(string[] keys, string sessionKey)
    {
        if (keys == null)
            keys = new string[0];

        GetFriendsRequestMessage req = new GetFriendsRequestMessage
        {
            publicKeys = keys
        };

        uint requestId = IdGen.GetNextId();
        var msgToSend = ApadanaMessageBase.CreateWithAuthDataAndRequestID(req, ApadanaMsgType.GetFriendsRequest, ApadanaMsgHeader.normal, sessionKey, requestId);
        SendReliable(msgToSend.ToByteArray());
        return requestId;
    }

    public uint SendAcceptFriendRequest(string playerId, byte[] data, string sessionKey)
    {
        if (data == null)
            data = new byte[0];
        StringAndByteArrayMessage req = new StringAndByteArrayMessage
        {
            stringValue = playerId,
            byteArrayValue = data
        };

        uint requestId = IdGen.GetNextId();
        var msgToSend = ApadanaMessageBase.CreateWithAuthDataAndRequestID(req, ApadanaMsgType.AcceptFriendRequest, ApadanaMsgHeader.normal, sessionKey, requestId);
        SendReliable(msgToSend.ToByteArray());
        return requestId;
    }

    public uint SendDenyFriendRequest(string playerId, byte[] data, string sessionKey)
    {
        if (data == null)
            data = new byte[0];
        StringAndByteArrayMessage req = new StringAndByteArrayMessage
        {
            stringValue = playerId,
            byteArrayValue = data
        };

        uint requestId = IdGen.GetNextId();
        var msgToSend = ApadanaMessageBase.CreateWithAuthDataAndRequestID(req, ApadanaMsgType.DenyFriendRequest, ApadanaMsgHeader.normal, sessionKey, requestId);
        SendReliable(msgToSend.ToByteArray());
        return requestId;
    }

    public uint SendCreateAccountRequest(LoginType type, string userId, string passwordHash, Dictionary<string, string> data, Guid sessionKey)
    {
        CreateAccountRequest req = new CreateAccountRequest
        {
            LoginType = type,
            Id = userId,
            PasswordHash = passwordHash,
            data = data,
            ticket = sessionKey.ToString()
        };
        uint requestId = IdGen.GetNextId();
        var msgToSend = ApadanaMessageBase.CreateWithRequestID(req, ApadanaMsgType.CreateNewAccountRequest, ApadanaMsgHeader.normal, requestId);
        SendReliable(msgToSend.ToByteArray());
        return requestId;
    }

    public uint SendActivationRequest(string email, string code, Guid sessionKey)
    {
        EmailActivationRequest req = new EmailActivationRequest
        {
            code = code,
            email = email,
            ticket = sessionKey.ToString()
        };
        uint requestId = IdGen.GetNextId();
        var msgToSend = ApadanaMessageBase.CreateWithRequestID(req, ApadanaMsgType.EmailActivationRequest, ApadanaMsgHeader.normal, requestId);
        SendReliable(msgToSend.ToByteArray());
        return requestId;
    }

    public uint SendInitializeSessionRequest(Guid developerKey, Guid gameKey)
    {
        InitializeSessionRequest req = new RealtimeServer.InitializeSessionRequest
        {
            DeveloperKey = developerKey,
            GameKey = gameKey
        };
        uint requestId = IdGen.GetNextId();
        var msgToSend = ApadanaMessageBase.CreateWithRequestID(req, ApadanaMsgType.InitializeSessionRequest, ApadanaMsgHeader.normal, requestId);
        SendReliable(msgToSend.ToByteArray());
        return requestId;
    }

    public uint SendFindMatchRequest(string matchType, uint skill, string ticket)
    {
        FindMatchRequest req = new FindMatchRequest
        {
            matchType = matchType,
            skill = skill,
            waitTime = MATCHMAKING_TIMEOUT
        };
        uint requestId = IdGen.GetNextId();
        var msgToSend = ApadanaMessageBase.CreateWithAuthDataAndRequestID(req, ApadanaMsgType.FindMatchRequest, ApadanaMsgHeader.normal, ticket, requestId);
        SendReliable(msgToSend.ToByteArray());
        return requestId;
    }

    public uint SendLeaveGameRequest(string game, string ticket)
    {
        EmptyMessage req = new EmptyMessage();
        var msgToSend = ApadanaMessageBase.CreateWithGameTarget(req, ApadanaMsgType.LeaveGameRequest, ApadanaMsgHeader.normal, game, ticket);
        uint requestId = IdGen.GetNextId();
        msgToSend.SetRequestID(requestId);
        SendReliable(msgToSend.ToByteArray());
        return requestId;
    }

    public uint SendSetMatchPropertyRequest(Guid game, bool isPlayerProperty, string key, byte[] data, string ticket)
    {
        SetMatchPropertyData req = new SetMatchPropertyData
        {
            Game = game,
            isPlayerProperty = isPlayerProperty,
            key = key,
            value = data
        };
        uint id = IdGen.GetNextId();
        var msgToSend = ApadanaMessageBase.CreateWithAuthDataAndRequestID(req, ApadanaMsgType.SetMatchPropertyRequest, ApadanaMsgHeader.normal, ticket, id);
        SendReliable(msgToSend.ToByteArray());
        return id;
    }

    public uint SendRejoinMatchRequest(Guid game, string ticket)
    {
        GuidMessage req = new GuidMessage
        {
            value = game
        };
        uint requestId = IdGen.GetNextId();
        var msgToSend = ApadanaMessageBase.CreateWithAuthDataAndRequestID(req, ApadanaMsgType.RejoinMatchRequest, ApadanaMsgHeader.normal, ticket, requestId);
        SendReliable(msgToSend.ToByteArray());
        return requestId;
    }

    public uint SendGetPendingMatchesRequest(string ticket)
    {
        EmptyMessage req = new EmptyMessage();
        uint requestId = IdGen.GetNextId();
        var msgToSend = ApadanaMessageBase.CreateWithAuthDataAndRequestID(req, ApadanaMsgType.GetPlayerPendingMatchesRequest, ApadanaMsgHeader.normal, ticket, requestId);
        SendReliable(msgToSend.ToByteArray());
        return requestId;
    }

    public uint SendChallengeRequest(string player, uint timeout, byte[] data, string ticket)
    {
        ChallengeMessage req = new ChallengeMessage
        {
            Player = player,
            TimeOut = timeout,
            data = data
        };
        uint requestId = IdGen.GetNextId();
        var msgToSend = ApadanaMessageBase.CreateWithAuthDataAndRequestID(req, ApadanaMsgType.StartChallengeRequest, ApadanaMsgHeader.normal, ticket, requestId);
        SendReliable(msgToSend.ToByteArray());
        return requestId;
    }

    public uint SendAcceptChallengeRequest(string player, byte[] data, string ticket)
    {
        ChallengeMessage req = new ChallengeMessage
        {
            Player = player,
            data = data
        };
        uint requestId = IdGen.GetNextId();
        var msgToSend = ApadanaMessageBase.CreateWithAuthDataAndRequestID(req, ApadanaMsgType.AcceptChallengeRequest, ApadanaMsgHeader.normal, ticket, requestId);
        SendReliable(msgToSend.ToByteArray());
        return requestId;
    }

    public uint SendDenyChallengeRequest(string player, byte[] data, string ticket)
    {
        ChallengeMessage req = new ChallengeMessage
        {
            Player = player,
            data = data
        };
        uint requestId = IdGen.GetNextId();
        var msgToSend = ApadanaMessageBase.CreateWithAuthDataAndRequestID(req, ApadanaMsgType.DenyChallengeRequest, ApadanaMsgHeader.normal, ticket, requestId);
        SendReliable(msgToSend.ToByteArray());
        return requestId;
    }

    public uint SendDevGetPublisherDataRequest(string key, Guid developerKey, Guid developerSecret)
    {
        GetPublisherDataRequest req = new GetPublisherDataRequest
        {
            key = key,
            developerKey = developerKey,
            developerSecret = developerSecret
        };
        uint requestId = IdGen.GetNextId();
        var msgToSend = ApadanaMessageBase.CreateWithRequestID(req, ApadanaMsgType.GetPublisherDataRequest_S, ApadanaMsgHeader.normal, requestId);
        SendReliable(msgToSend.ToByteArray());
        return requestId;
    }

    public uint SendDevSetPublisherDataRequest(string key, string value, Guid developerKey, Guid developerSecret)
    {
        SetPublisherDataRequest req = new SetPublisherDataRequest
        {
            key = key,
            value = value,
            developerKey = developerKey,
            developerSecret = developerSecret
        };
        uint requestId = IdGen.GetNextId();
        var msgToSend = ApadanaMessageBase.CreateWithRequestID(req, ApadanaMsgType.SetPublisherDataRequest_S, ApadanaMsgHeader.normal, requestId);
        SendReliable(msgToSend.ToByteArray());
        return requestId;
    }

    public uint SendSetAchievementProgressRequest(string achievement, int progress, string playerId)
    {
        SetAchievementProgressByGameServerRequest req = new SetAchievementProgressByGameServerRequest
        {
            achievement = achievement,
            progress = progress,
            player = playerId,
            game = ""
        };
        uint requestId = IdGen.GetNextId();
        var msgToSend = ApadanaMessageBase.CreateWithRequestID(req, ApadanaMsgType.SetAchievementProgressRequest, ApadanaMsgHeader.normal, requestId);
        SendReliable(msgToSend.ToByteArray());
        return requestId;
    }

    public uint SendSetQuestProgressRequest(string quest, int progress, string playerId)
    {
        SetQuestProgressByGameServerRequest req = new SetQuestProgressByGameServerRequest
        {
            quest = quest,
            progress = progress,
            player = playerId,
            game = ""
        };
        uint requestId = IdGen.GetNextId();
        var msgToSend = ApadanaMessageBase.CreateWithRequestID(req, ApadanaMsgType.SetQuestProgressRequest, ApadanaMsgHeader.normal, requestId);
        SendReliable(msgToSend.ToByteArray());
        return requestId;
    }

    public uint SendSetMatchResultRequest(int skill, bool isWon, bool isDraw, int court, string playerId,string tournament,int value)
    {
        SetMatchResultByGameServerRequestMessage req = new SetMatchResultByGameServerRequestMessage
        {
            skill = skill,
            isWon = isWon,
            isDraw = isDraw,
            court = court,
            player = playerId,
            game = "",
            tournament=tournament,
            value=value
        };
        uint requestId = IdGen.GetNextId();
        var msgToSend = ApadanaMessageBase.CreateWithRequestID(req, ApadanaMsgType.SetUBCMatchResultRequest, ApadanaMsgHeader.normal, requestId);
        SendReliable(msgToSend.ToByteArray());
        return requestId;
    }

    public uint SendSetCloudDataRequest(string key, string value, string ticket)
    {
        var req = new SetCloudDataRequest
        {
            key = key,
            value = value
        };
        uint requestId = IdGen.GetNextId();
        var msgToSend = ApadanaMessageBase.CreateWithAuthDataAndRequestID(req, ApadanaMsgType.SetCloudDataRequest, ApadanaMsgHeader.normal, ticket, requestId);
        SendReliable(msgToSend.ToByteArray());
        return requestId;
    }

    public uint SendSetPublicCloudDataRequest(string key, string value, string ticket)
    {
        var req = new SetCloudDataRequest
        {
            key = key,
            value = value
        };
        uint requestId = IdGen.GetNextId();
        var msgToSend = ApadanaMessageBase.CreateWithAuthDataAndRequestID(req, ApadanaMsgType.SetPublicCloudDataRequest, ApadanaMsgHeader.normal, ticket, requestId);
        SendReliable(msgToSend.ToByteArray());
        return requestId;
    }

    public uint SendExecuteScriptRequest(string eventName, string ticket, params object[] parameters)
    {
        var req = new ScriptEventExecutionRequestMessage
        {
            eventName = eventName,
        };
        req.SetParametersAndTypes(parameters);
        uint requestId = IdGen.GetNextId();
        var msgToSend = ApadanaMessageBase.CreateWithAuthDataAndRequestID(req, ApadanaMsgType.ExecuteScriptEventRequest, ApadanaMsgHeader.normal, ticket, requestId);
        SendReliable(msgToSend.ToByteArray());
        return requestId;
    }

    public uint SendGetCloudDataRequest(string key, string ticket)
    {
        var req = new StringMessage
        {
            value = key
        };
        uint requestId = IdGen.GetNextId();
        var msgToSend = ApadanaMessageBase.CreateWithAuthDataAndRequestID(req, ApadanaMsgType.GetCloudDataRequest, ApadanaMsgHeader.normal, ticket, requestId);
        SendReliable(msgToSend.ToByteArray());
        return requestId;
    }

    public uint SendGetPublicCloudDataRequest(string key, string ticket)
    {
        var req = new StringMessage
        {
            value = key
        };
        uint requestId = IdGen.GetNextId();
        var msgToSend = ApadanaMessageBase.CreateWithAuthDataAndRequestID(req, ApadanaMsgType.GetPublicCloudDataRequest, ApadanaMsgHeader.normal, ticket, requestId);
        SendReliable(msgToSend.ToByteArray());
        return requestId;
    }

    public uint SendGetPlayerPublicCloudDataRequest(string player, string key, string ticket)
    {
        var req = new GetPlayerPublicCloudDataRequest
        {
            key = key,
            player = player
        };
        uint requestId = IdGen.GetNextId();
        var msgToSend = ApadanaMessageBase.CreateWithAuthDataAndRequestID(req, ApadanaMsgType.GetPlayerPublicCloudDataRequest, ApadanaMsgHeader.normal, ticket, requestId);
        SendReliable(msgToSend.ToByteArray());
        return requestId;
    }

    public uint SendGetPublisherDataRequest(string key, string ticket)
    {
        var req = new StringMessage
        {
            value = key
        };
        uint requestId = IdGen.GetNextId();
        var msgToSend = ApadanaMessageBase.CreateWithAuthDataAndRequestID(req, ApadanaMsgType.GetPublisherDataRequest, ApadanaMsgHeader.normal, ticket, requestId);
        SendReliable(msgToSend.ToByteArray());
        return requestId;
    }

    public uint SendSetNameRequest(string name, string ticket)
    {
        var req = new StringMessage
        {
            value = name
        };
        uint requestId = IdGen.GetNextId();
        var msgToSend = ApadanaMessageBase.CreateWithAuthDataAndRequestID(req, ApadanaMsgType.SetNameRequest, ApadanaMsgHeader.normal, ticket, requestId);
        SendReliable(msgToSend.ToByteArray());
        return requestId;
    }

    public uint SendGetServerTimeRequest(string ticket)
    {
        var req = new EmptyMessage();
        uint requestId = IdGen.GetNextId();
        var msgToSend = ApadanaMessageBase.CreateWithAuthDataAndRequestID(req, ApadanaMsgType.GetServerTimeRequest, ApadanaMsgHeader.normal, ticket, requestId);
        SendReliable(msgToSend.ToByteArray());
        return requestId;
    }

    public uint SendGetNameRequest(string ticket)
    {
        var req = new EmptyMessage();
        uint requestId = IdGen.GetNextId();
        var msgToSend = ApadanaMessageBase.CreateWithAuthDataAndRequestID(req, ApadanaMsgType.GetNameRequest, ApadanaMsgHeader.normal, ticket, requestId);
        SendReliable(msgToSend.ToByteArray());
        return requestId;
    }

    public uint SendGetMainDataRequest(string player, string ticket)
    {
        var req = new GetMainDataRequestMessage
        {
            game = "",
            player = player
        };
        uint requestId = IdGen.GetNextId();
        var msgToSend = (string.IsNullOrEmpty(player)) ? ApadanaMessageBase.CreateWithAuthDataAndRequestID(req, ApadanaMsgType.GetMainDataRequest, ApadanaMsgHeader.normal, ticket, requestId) :
            ApadanaMessageBase.CreateWithRequestID(req, ApadanaMsgType.GetMainDataRequest, ApadanaMsgHeader.normal, requestId);
        SendReliable(msgToSend.ToByteArray());
        return requestId;
    }

    public uint SendRegisterTournamentRequest(string name, string ticket)
    {
        var req = new StringMessage
        {
            value = name
        };
        uint requestId = IdGen.GetNextId();
        var msgToSend = ApadanaMessageBase.CreateWithAuthDataAndRequestID(req, ApadanaMsgType.RegisterInTournamentRequst, ApadanaMsgHeader.normal, ticket, requestId);
        SendReliable(msgToSend.ToByteArray());
        return requestId;
    }

    public uint SendGetChallengesRequest(string ticket)
    {
        var req = new EmptyMessage();
        uint requestId = IdGen.GetNextId();
        var msgToSend = ApadanaMessageBase.CreateWithAuthDataAndRequestID(req, ApadanaMsgType.GetPlayerChallengesRequest, ApadanaMsgHeader.normal, ticket, requestId);
        SendReliable(msgToSend.ToByteArray());
        return requestId;
    }

    public uint SendGetAchievementsRequest(string ticket)
    {
        var req = new EmptyMessage();
        uint requestId = IdGen.GetNextId();
        var msgToSend = ApadanaMessageBase.CreateWithAuthDataAndRequestID(req, ApadanaMsgType.GetAchievementsRequest, ApadanaMsgHeader.normal, ticket, requestId);
        SendReliable(msgToSend.ToByteArray());
        return requestId;
    }

    public uint SendGetInventoryRequest(string ticket)
    {
        var req = new EmptyMessage();
        uint requestId = IdGen.GetNextId();
        var msgToSend = ApadanaMessageBase.CreateWithAuthDataAndRequestID(req, ApadanaMsgType.GetInventoryRequest, ApadanaMsgHeader.normal, ticket, requestId);
        SendReliable(msgToSend.ToByteArray());
        return requestId;
    }

    public uint SendAddInventoryItemRequest(BuyItemInventoryRequestMessage addReq,string ticket)
    {
        var req = addReq;
        uint requestId = IdGen.GetNextId();
        var msgToSend = ApadanaMessageBase.CreateWithAuthDataAndRequestID(req, ApadanaMsgType.AddItemToInventoryRequest, ApadanaMsgHeader.normal, ticket, requestId);
        SendReliable(msgToSend.ToByteArray());
        return requestId;
    }

    public uint SendRemoveInventoryItemRequest(RemoveItemInventoryRequestMessage removeReq, string ticket)
    {
        var req = removeReq;
        uint requestId = IdGen.GetNextId();
        var msgToSend = ApadanaMessageBase.CreateWithAuthDataAndRequestID(req, ApadanaMsgType.RemoveInventoryItemRequest, ApadanaMsgHeader.normal, ticket, requestId);
        SendReliable(msgToSend.ToByteArray());
        return requestId;
    }

    public uint SendGetquestsRequest(string ticket)
    {
        var req = new EmptyMessage();
        uint requestId = IdGen.GetNextId();
        var msgToSend = ApadanaMessageBase.CreateWithAuthDataAndRequestID(req, ApadanaMsgType.GetCurrentQuestsRequest, ApadanaMsgHeader.normal, ticket, requestId);
        SendReliable(msgToSend.ToByteArray());
        return requestId;
    }

    public uint SendMulliganquestsRequest(int index, string ticket)
    {
        var req = new MulliganQuestRequestMessage();
        req.index = index;
        uint requestId = IdGen.GetNextId();
        var msgToSend = ApadanaMessageBase.CreateWithAuthDataAndRequestID(req, ApadanaMsgType.MulliganQuestRequest, ApadanaMsgHeader.normal, ticket, requestId);
        SendReliable(msgToSend.ToByteArray());
        return requestId;
    }

    public uint SendClaimQuestsRequest(string quest, string ticket)
    {
        var req = new ClaimQuestRewardRequestMessage();
        req.questId = quest;
        uint requestId = IdGen.GetNextId();
        var msgToSend = ApadanaMessageBase.CreateWithAuthDataAndRequestID(req, ApadanaMsgType.ClaimQuestRewardRequest, ApadanaMsgHeader.normal, ticket, requestId);
        SendReliable(msgToSend.ToByteArray());
        return requestId;
    }

    public uint SendSetDeckRequest(List<string> deck, int index, string ticket)
    {
        var req = new SetDeckRequestMessage();
        req.index = index;
        req.items = deck;
        uint requestId = IdGen.GetNextId();
        var msgToSend = ApadanaMessageBase.CreateWithAuthDataAndRequestID(req, ApadanaMsgType.SetDeckRequest, ApadanaMsgHeader.normal, ticket, requestId);
        SendReliable(msgToSend.ToByteArray());
        return requestId;
    }

    public uint SendPurchaseUBCItemRequest(string item, UBCPurchaseRequestMessage.PurchaseType type, string ticket)
    {
        var req = new UBCPurchaseRequestMessage();
        req.type = type;
        req.item = item;
        uint requestId = IdGen.GetNextId();
        var msgToSend = ApadanaMessageBase.CreateWithAuthDataAndRequestID(req, ApadanaMsgType.BuyUBCItemRequest, ApadanaMsgHeader.normal, ticket, requestId);
        SendReliable(msgToSend.ToByteArray());
        return requestId;
    }

    public uint SendGetPlayerNameRequest(string player, string ticket)
    {
        var req = new StringMessage { value = player };
        uint requestId = IdGen.GetNextId();
        var msgToSend = ApadanaMessageBase.CreateWithAuthDataAndRequestID(req, ApadanaMsgType.GetPlayerNameRequest, ApadanaMsgHeader.normal, ticket, requestId);
        SendReliable(msgToSend.ToByteArray());
        return requestId;
    }

    public uint SendVerifyCafeReceiptRequestRequest(string product, string receipt, string ticket)
    {
        var req = new VerifyCafeReceiptRequest
        {
            Product = product,
            Receipt = receipt
        };
        uint requestId = IdGen.GetNextId();
        var msgToSend = ApadanaMessageBase.CreateWithAuthDataAndRequestID(req, ApadanaMsgType.VerifyStoreReceiptRequest, ApadanaMsgHeader.normal, ticket, requestId);
        SendReliable(msgToSend.ToByteArray());
        return requestId;
    }

    public uint SendGetLeaderBoardRequest(string leaderBoardName, GetLeaderBoardRequest.RequestType type, int offset, int count, string ticket)
    {
        var req = new GetLeaderBoardRequest
        {
            type = type,
            leaderBoardName = leaderBoardName,
            offset = offset,
            count = count
        };
        uint requestId = IdGen.GetNextId();
        var msgToSend = ApadanaMessageBase.CreateWithAuthDataAndRequestID(req, ApadanaMsgType.GetLeaderboardRequest, ApadanaMsgHeader.normal, ticket, requestId);
        SendReliable(msgToSend.ToByteArray());
        return requestId;
    }

    public uint SendSetLeaderBoardRequest(string leaderBoardName, int score, string ticket)
    {
        var req = new SetLeaderBoardRequest
        {
            leaderBoardName = leaderBoardName,
            score = score
        };
        uint requestId = IdGen.GetNextId();
        var msgToSend = ApadanaMessageBase.CreateWithAuthDataAndRequestID(req, ApadanaMsgType.SetLeaderboardRequest, ApadanaMsgHeader.normal, ticket, requestId);
        SendReliable(msgToSend.ToByteArray());
        return requestId;
    }

    #endregion

    private float lastMessageSent;

    void Update()
    {
        if (shouldOnDisconnectedConnectedBeCalled)
        {
            lock (lockObject)
            {
                shouldOnDisconnectedConnectedBeCalled = false;
            }
            if (OnDisconnected != null)
                OnDisconnected(connectionId);
        }
        if (shouldOnConnectedBeCalled)
        {
            lock (lockObject)
            {
                shouldOnConnectedBeCalled = false;
            }
            if (OnConnected != null)
                OnConnected(connectionId);
        }
        while (updateCallbackHandlersQueue.Count > 0)
        {
            var item = updateCallbackHandlersQueue.Dequeue();
            foreach (var callback in item.callbacks)
                callback(item.data);
        }

    }

    protected override void DataEvent(int host, int connection, int channel, int size, byte[] data)
    {
        if (OnData != null)
            OnData(host, connection, channel, data.Take(size).ToArray());
        byte[] copy = new byte[size];
        System.Buffer.BlockCopy(data, 0, copy, 0, size);

        var msg = ApadanaMessageBase.CreateMessageFromByteArray(copy);
        if (handlers.ContainsKey(msg.msgType))
        {
            var list = handlers[msg.msgType];
            if (list != null)
            {
                updateCallbackHandlersQueue.Enqueue(new MessageHandlerData { data = msg, callbacks = list });
            }
        }
    }

    protected override void ConnectEvent(int host, int connection)
    {
        connected = true;
        lock (lockObject)
        {
            shouldOnConnectedBeCalled = true;

            this.connectionId = connection;
        }
    }

    protected override void DisconnectEvent(int host, int connection)
    {
        connected = false;
        lock (lockObject)
        {
            shouldOnDisconnectedConnectedBeCalled = true;
            this.connectionId = connection;
        }
    }
}

public struct MessageHandlerData
{
    public List<Action<ApadanaMessageBase>> callbacks;
    public ApadanaMessageBase data;
}