using UnityEngine;
using System;
using System.Collections.Generic;
using UnityEngine.Networking;
using WildCat.Protocol;
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
	private Dictionary<WildCatMsgType, List<Action<WildCatMessageBase>>> handlers = new Dictionary<WildCatMsgType, List<Action<WildCatMessageBase>>>();

    /// <summary>
    /// This queue is used for the callbacks which need to be executed
    /// from update based on a receive message
    /// </summary>
	private Queue<MessageHandlerData> updateCallbackHandlersQueue = new Queue<MessageHandlerData>();

    public void RegisterHandler(WildCatMsgType type, Action<WildCatMessageBase> action)
    {
        if (!handlers.ContainsKey(type))
        {
            handlers.Add(type, new List<Action<WildCatMessageBase>>());
        }
        handlers[type].Add(action);
    }

    public void UnregisterHandler(WildCatMsgType type, Action<WildCatMessageBase> action)
    {
        if (handlers.ContainsKey(type))
        {
            if (handlers[type].Contains(action))
            {
                handlers[type].Remove(action);
            }
        }
    }

    public void UnregisterHandlers(WildCatMsgType type)
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

    public void SendGameMessage(string game, byte[] data, WildCatMsgType type, string ticket, QosType qos)
    {
        RawMessage msg = new RawMessage { value = data };
        SendGameMessage(game, msg, type, ticket, qos);
    }

    public void SendGameMessage<T>(string game, T msg, WildCatMsgType type, string ticket, QosType qos) where T : MessageBase, new()
    {
        var msgToSend = WildCatMessageBase.CreateWithGameTarget(msg, type, WildCatMsgHeader.normal, game, ticket);
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
        var msgToSend = WildCatMessageBase.CreateWithAuthDataAndRequestID(req, WildCatMsgType.AddAuthMechanismToAccountRequest, WildCatMsgHeader.normal, ticket, requestId);
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
        var msgToSend = WildCatMessageBase.CreateWithRequestID(req, WildCatMsgType.AuthenticateRequest, WildCatMsgHeader.normal, requestId);
        SendReliable(msgToSend.ToByteArray());
        return requestId;
    }

    public uint SendHeartBeatIfNeeded(string sessionKey)
    {
        if (Time.time - lastMessageSent < 10)
            return 0;
        EmptyMessage req = new EmptyMessage();
        uint requestId = IdGen.GetNextId();
        var msgToSend = WildCatMessageBase.CreateWithAuthDataAndRequestID(req, WildCatMsgType.HeartBeatRequest, WildCatMsgHeader.normal, sessionKey, requestId);
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
        var msgToSend = WildCatMessageBase.CreateWithAuthDataAndRequestID(req, WildCatMsgType.InviteFriendRequest, WildCatMsgHeader.normal, sessionKey, requestId);
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
        var msgToSend = WildCatMessageBase.CreateWithAuthDataAndRequestID(req, WildCatMsgType.GetFriendsRequest, WildCatMsgHeader.normal, sessionKey, requestId);
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
        var msgToSend = WildCatMessageBase.CreateWithAuthDataAndRequestID(req, WildCatMsgType.AcceptFriendRequest, WildCatMsgHeader.normal, sessionKey, requestId);
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
        var msgToSend = WildCatMessageBase.CreateWithAuthDataAndRequestID(req, WildCatMsgType.DenyFriendRequest, WildCatMsgHeader.normal, sessionKey, requestId);
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
        var msgToSend = WildCatMessageBase.CreateWithRequestID(req, WildCatMsgType.CreateNewAccountRequest, WildCatMsgHeader.normal, requestId);
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
        var msgToSend = WildCatMessageBase.CreateWithRequestID(req, WildCatMsgType.EmailActivationRequest, WildCatMsgHeader.normal, requestId);
        SendReliable(msgToSend.ToByteArray());
        return requestId;
    }

    public uint SendInitializeSessionRequest(Guid developerKey, Guid gameKey)
    {
        InitializeSessionRequest req = new WildCat.Protocol.InitializeSessionRequest
        {
            DeveloperKey = developerKey,
            GameKey = gameKey
        };
        uint requestId = IdGen.GetNextId();
        var msgToSend = WildCatMessageBase.CreateWithRequestID(req, WildCatMsgType.InitializeSessionRequest, WildCatMsgHeader.normal, requestId);
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
        var msgToSend = WildCatMessageBase.CreateWithAuthDataAndRequestID(req, WildCatMsgType.FindMatchRequest, WildCatMsgHeader.normal, ticket, requestId);
        SendReliable(msgToSend.ToByteArray());
        return requestId;
    }

    public uint SendLeaveGameRequest(string game, string ticket)
    {
        EmptyMessage req = new EmptyMessage();
        var msgToSend = WildCatMessageBase.CreateWithGameTarget(req, WildCatMsgType.LeaveGameRequest, WildCatMsgHeader.normal, game, ticket);
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
        var msgToSend = WildCatMessageBase.CreateWithAuthDataAndRequestID(req, WildCatMsgType.SetMatchPropertyRequest, WildCatMsgHeader.normal, ticket, id);
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
        var msgToSend = WildCatMessageBase.CreateWithAuthDataAndRequestID(req, WildCatMsgType.RejoinMatchRequest, WildCatMsgHeader.normal, ticket, requestId);
        SendReliable(msgToSend.ToByteArray());
        return requestId;
    }

    public uint SendGetPendingMatchesRequest(string ticket)
    {
        EmptyMessage req = new EmptyMessage();
        uint requestId = IdGen.GetNextId();
        var msgToSend = WildCatMessageBase.CreateWithAuthDataAndRequestID(req, WildCatMsgType.GetPlayerPendingMatchesRequest, WildCatMsgHeader.normal, ticket, requestId);
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
        var msgToSend = WildCatMessageBase.CreateWithAuthDataAndRequestID(req, WildCatMsgType.StartChallengeRequest, WildCatMsgHeader.normal, ticket, requestId);
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
        var msgToSend = WildCatMessageBase.CreateWithAuthDataAndRequestID(req, WildCatMsgType.AcceptChallengeRequest, WildCatMsgHeader.normal, ticket, requestId);
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
        var msgToSend = WildCatMessageBase.CreateWithAuthDataAndRequestID(req, WildCatMsgType.DenyChallengeRequest, WildCatMsgHeader.normal, ticket, requestId);
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
        var msgToSend = WildCatMessageBase.CreateWithAuthDataAndRequestID(req, WildCatMsgType.SetCloudDataRequest, WildCatMsgHeader.normal, ticket, requestId);
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
        var msgToSend = WildCatMessageBase.CreateWithAuthDataAndRequestID(req, WildCatMsgType.SetPublicCloudDataRequest, WildCatMsgHeader.normal, ticket, requestId);
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
        var msgToSend = WildCatMessageBase.CreateWithAuthDataAndRequestID(req, WildCatMsgType.ExecuteScriptEventRequest, WildCatMsgHeader.normal, ticket, requestId);
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
        var msgToSend = WildCatMessageBase.CreateWithAuthDataAndRequestID(req, WildCatMsgType.GetCloudDataRequest, WildCatMsgHeader.normal, ticket, requestId);
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
        var msgToSend = WildCatMessageBase.CreateWithAuthDataAndRequestID(req, WildCatMsgType.GetPublicCloudDataRequest, WildCatMsgHeader.normal, ticket, requestId);
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
        var msgToSend = WildCatMessageBase.CreateWithAuthDataAndRequestID(req, WildCatMsgType.GetPlayerPublicCloudDataRequest, WildCatMsgHeader.normal, ticket, requestId);
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
        var msgToSend = WildCatMessageBase.CreateWithAuthDataAndRequestID(req, WildCatMsgType.GetPublisherDataRequest, WildCatMsgHeader.normal, ticket, requestId);
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
        var msgToSend = WildCatMessageBase.CreateWithAuthDataAndRequestID(req, WildCatMsgType.SetNameRequest, WildCatMsgHeader.normal, ticket, requestId);
        SendReliable(msgToSend.ToByteArray());
        return requestId;
    }

    public uint SendGetServerTimeRequest(string ticket)
    {
        var req = new EmptyMessage();
        uint requestId = IdGen.GetNextId();
        var msgToSend = WildCatMessageBase.CreateWithAuthDataAndRequestID(req, WildCatMsgType.GetServerTimeRequest, WildCatMsgHeader.normal, ticket, requestId);
        SendReliable(msgToSend.ToByteArray());
        return requestId;
    }

    public uint SendGetNameRequest(string ticket)
    {
        var req = new EmptyMessage();
        uint requestId = IdGen.GetNextId();
        var msgToSend = WildCatMessageBase.CreateWithAuthDataAndRequestID(req, WildCatMsgType.GetNameRequest, WildCatMsgHeader.normal, ticket, requestId);
        SendReliable(msgToSend.ToByteArray());
        return requestId;
    }

    public uint SendGetChallengesRequest(string ticket)
    {
        var req = new EmptyMessage();
        uint requestId = IdGen.GetNextId();
        var msgToSend = WildCatMessageBase.CreateWithAuthDataAndRequestID(req, WildCatMsgType.GetPlayerChallengesRequest, WildCatMsgHeader.normal, ticket, requestId);
        SendReliable(msgToSend.ToByteArray());
        return requestId;
    }

    public uint SendGetInventoryRequest(string ticket)
    {
        var req = new EmptyMessage();
        uint requestId = IdGen.GetNextId();
        var msgToSend = WildCatMessageBase.CreateWithAuthDataAndRequestID(req, WildCatMsgType.GetInventoryRequest, WildCatMsgHeader.normal, ticket, requestId);
        SendReliable(msgToSend.ToByteArray());
        return requestId;
    }

    public uint SendAddInventoryItemRequest(BuyItemInventoryRequestMessage addReq,string ticket)
    {
        var req = addReq;
        uint requestId = IdGen.GetNextId();
        var msgToSend = WildCatMessageBase.CreateWithAuthDataAndRequestID(req, WildCatMsgType.AddItemToInventoryRequest, WildCatMsgHeader.normal, ticket, requestId);
        SendReliable(msgToSend.ToByteArray());
        return requestId;
    }

    public uint SendRemoveInventoryItemRequest(RemoveItemInventoryRequestMessage removeReq, string ticket)
    {
        var req = removeReq;
        uint requestId = IdGen.GetNextId();
        var msgToSend = WildCatMessageBase.CreateWithAuthDataAndRequestID(req, WildCatMsgType.RemoveInventoryItemRequest, WildCatMsgHeader.normal, ticket, requestId);
        SendReliable(msgToSend.ToByteArray());
        return requestId;
    }

    public uint SendGetPlayerNameRequest(string player, string ticket)
    {
        var req = new StringMessage { value = player };
        uint requestId = IdGen.GetNextId();
        var msgToSend = WildCatMessageBase.CreateWithAuthDataAndRequestID(req, WildCatMsgType.GetPlayerNameRequest, WildCatMsgHeader.normal, ticket, requestId);
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
        var msgToSend = WildCatMessageBase.CreateWithAuthDataAndRequestID(req, WildCatMsgType.GetLeaderboardRequest, WildCatMsgHeader.normal, ticket, requestId);
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
        var msgToSend = WildCatMessageBase.CreateWithAuthDataAndRequestID(req, WildCatMsgType.SetLeaderboardRequest, WildCatMsgHeader.normal, ticket, requestId);
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

        var msg = WildCatMessageBase.CreateMessageFromByteArray(copy);
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
    public List<Action<WildCatMessageBase>> callbacks;
    public WildCatMessageBase data;
}