using Interfaces;
using Orleans;
using Orleans.Streams;
using Orleans.TestingHost;
using RealtimeServer;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Tests;
using Xunit;
using LeaderBoardEntry = RealtimeServer.LeaderBoardEntry;
using OnlineStatus = RealtimeServer.OnlineStatus;

namespace RealTimeServerTests
{
    [Collection(ClusterCollection.Name)]
    public class UNetMsgSerializationTests
    {
        [Fact]
        public void FindMatchRequestSerialization()
        {
            FindMatchRequest req = new FindMatchRequest("game1", 200, 3);
            ApadanaMessageBase msg = ApadanaMessageBase.Create(req, ApadanaMsgType.FindMatchRequest, ApadanaMsgHeader.normal);
            var data = msg.GetData<FindMatchRequest>();
            Assert.Equal(data.matchType, req.matchType);
            Assert.Equal(data.skill, req.skill);
            Assert.Equal(data.waitTime, req.waitTime);
        }

        [Fact]
        public void ApadanaMessageBaseSerialization()
        {
            FindMatchRequest req = new FindMatchRequest("game1", 200, 3);
            ApadanaMessageBase msg = ApadanaMessageBase.Create(req, ApadanaMsgType.FindMatchRequest, ApadanaMsgHeader.buffered);
            var bytes = msg.ToByteArray();
            ApadanaMessageBase deserializedMessage = new ApadanaMessageBase(bytes);
            Assert.Equal(msg.msgType, deserializedMessage.msgType);
            Assert.Equal(msg.header, deserializedMessage.header);
            Assert.Equal(msg.hasAuthData, deserializedMessage.hasAuthData);
            Assert.Equal(msg.ticket, deserializedMessage.ticket);
            Assert.True(string.IsNullOrEmpty(deserializedMessage.ticket));
            Assert.False(deserializedMessage.hasAuthData);
            var d1 = msg.GetData<FindMatchRequest>();
            var d2 = deserializedMessage.GetData<FindMatchRequest>();
            Assert.Equal(d1.matchType, d2.matchType);
            Assert.Equal(d1.skill, d2.skill);
            Assert.Equal(d1.waitTime, d2.waitTime);
        }

        [Fact]
        public void ApadanaMessageBaseWithAuthSerialization()
        {
            FindMatchRequest req = new FindMatchRequest("game1", 200, 3);
            ApadanaMessageBase msg = ApadanaMessageBase.CreateWithAuthData(req, ApadanaMsgType.FindMatchRequest, ApadanaMsgHeader.buffered, "random ticket");
            var bytes = msg.ToByteArray();
            ApadanaMessageBase deserializedMessage = new ApadanaMessageBase(bytes);
            Assert.Equal(msg.msgType, deserializedMessage.msgType);
            Assert.Equal(msg.header, deserializedMessage.header);
            Assert.Equal(msg.hasAuthData, deserializedMessage.hasAuthData);
            Assert.Equal(msg.ticket, deserializedMessage.ticket);
            Assert.True(deserializedMessage.hasAuthData);
            Assert.False(string.IsNullOrEmpty(deserializedMessage.ticket));
            var d1 = msg.GetData<FindMatchRequest>();
            var d2 = deserializedMessage.GetData<FindMatchRequest>();
            Assert.Equal(d1.matchType, d2.matchType);
            Assert.Equal(d1.skill, d2.skill);
            Assert.Equal(d1.waitTime, d2.waitTime);
            Assert.True((msg.header & ApadanaMsgHeader.HasAuthData) == ApadanaMsgHeader.HasAuthData);
            Assert.True((deserializedMessage.header & ApadanaMsgHeader.HasAuthData) == ApadanaMsgHeader.HasAuthData);
        }

        [Fact]
        public void ApadanaMessageBaseWithGameSerialization()
        {
            FindMatchRequest req = new FindMatchRequest("game1", 200, 3);
            ApadanaMessageBase msg = ApadanaMessageBase.CreateWithGameTarget(req, ApadanaMsgType.FindMatchRequest, ApadanaMsgHeader.buffered, "game1", "random ticket");
            var bytes = msg.ToByteArray();
            ApadanaMessageBase deserializedMessage = new ApadanaMessageBase(bytes);
            Assert.Equal(msg.msgType, deserializedMessage.msgType);
            Assert.Equal(msg.header, deserializedMessage.header);
            Assert.Equal(msg.hasAuthData, deserializedMessage.hasAuthData);
            Assert.Equal(msg.ticket, deserializedMessage.ticket);
            Assert.Equal(msg.game, deserializedMessage.game);
            Assert.False(string.IsNullOrEmpty(msg.game));
            Assert.True(deserializedMessage.hasAuthData);
            Assert.False(string.IsNullOrEmpty(deserializedMessage.ticket));
            var d1 = msg.GetData<FindMatchRequest>();
            var d2 = deserializedMessage.GetData<FindMatchRequest>();
            Assert.Equal(d1.matchType, d2.matchType);
            Assert.Equal(d1.skill, d2.skill);
            Assert.Equal(d1.waitTime, d2.waitTime);
            Assert.True((msg.header & ApadanaMsgHeader.HasAuthData) == ApadanaMsgHeader.HasAuthData);
            Assert.True((deserializedMessage.header & ApadanaMsgHeader.HasAuthData) == ApadanaMsgHeader.HasAuthData);
            Assert.True((msg.header & ApadanaMsgHeader.HasGameTarget) == ApadanaMsgHeader.HasGameTarget);
            Assert.True((deserializedMessage.header & ApadanaMsgHeader.HasGameTarget) == ApadanaMsgHeader.HasGameTarget);
        }

        #region RequestResponseMessages

        [Fact]
        public void CreateDeveloperRequestTest()
        {
            CreateEnvironmentRequest resp =
                new CreateEnvironmentRequest
                {
                    FirstName = "Bill",
                    LastName = "Gates",
                    PasswordHash = "test",
                    Email = "bill@gates.com",
                    PhoneNumber = "123000",
                    Address = "somewhere in WA",
                    Description = "should i? really?!?"
                };
            ApadanaMessageBase msg = ApadanaMessageBase.Create(resp, ApadanaMsgType.FindMatchResponse, ApadanaMsgHeader.normal);
            var data = msg.GetData<CreateEnvironmentRequest>();
            Assert.Equal(data.FirstName, resp.FirstName);
            Assert.Equal(data.LastName, resp.LastName);
            Assert.Equal(data.PasswordHash, resp.PasswordHash);
            Assert.Equal(data.Email, resp.Email);
            Assert.Equal(data.PhoneNumber, resp.PhoneNumber);
            Assert.Equal(data.Address, resp.Address);
            Assert.Equal(data.Description, resp.Description);
        }

        [Fact]
        public void CreateEnvironmentResponseTest()
        {
            Guid dk = Guid.NewGuid();
            Guid gk = Guid.NewGuid();
            var resp = new CreateEnvironmentResponse
            {
                DeveloperKey = dk,
                GameKey = gk
            };


            ApadanaMessageBase msg = ApadanaMessageBase.Create(resp, ApadanaMsgType.FindMatchResponse, ApadanaMsgHeader.normal);
            var data = msg.GetData<CreateEnvironmentResponse>();
            Assert.Equal(data.GameKey, gk);
            Assert.Equal(data.DeveloperKey, dk);
        }

        [Fact]
        public void FindMatchResponseSerialization()
        {
            FindMatchResponse resp =
                new FindMatchResponse("game1", "nothing", new string[] { "first", "second" });
            ApadanaMessageBase msg = ApadanaMessageBase.Create(resp, ApadanaMsgType.FindMatchResponse, ApadanaMsgHeader.normal);
            var data = msg.GetData<FindMatchResponse>();
            Assert.Equal(data.gameId, resp.gameId);
            Assert.Equal(data.data, resp.data);
            Assert.Equal(data.players, resp.players);
        }

        //[Fact]
        //public void LoginTypeEnumTest()
        //{
        //    List<byte> valuesList = new List<byte>();
        //    List<byte> interfacesValuesList = new List<byte>();
        //    var interfacesValues = Enum.GetValues(typeof(Interfaces.LoginType));
        //    var values = Enum.GetValues(typeof(RealtimeServer.LoginType));

        //    Assert.Equal(values.Length, interfacesValues.Length);
        //    CollectionAssert.Equal(Enum.GetNames(typeof(RealtimeServer.LoginType)), Enum.GetNames(typeof(Interfaces.LoginType)));

        //    foreach (var v in values)
        //        valuesList.Add((byte)v);

        //    foreach (var v in interfacesValues)
        //        interfacesValuesList.Add((byte)v);
        //    CollectionAssert.Equal(valuesList, interfacesValuesList);
        //    //This doesn't work due to items being object
        //    //so compared using reference equality
        //    //CollectionAssert.Equal(values, interfacesValues);
        //}

        [Fact]
        public void SetCloudDataSerialization()
        {
            SetCloudDataRequest req =
                new SetCloudDataRequest { key = "k", value = "v" };
            ApadanaMessageBase msg = ApadanaMessageBase.Create(req, ApadanaMsgType.SetCloudDataRequest, ApadanaMsgHeader.normal);

            var data = msg.GetData<SetCloudDataRequest>();
            Assert.Equal(data.key, req.key);
            Assert.Equal(data.value, req.value);
        }



        [Fact]
        public void LeaderBoardEntrySerialization()
        {
            LeaderBoardEntry req =
                new LeaderBoardEntry { playerId = "p", userName = "n", score = 13, entryTime = DateTime.Now };
            ApadanaMessageBase msg = ApadanaMessageBase.Create(req, ApadanaMsgType.SetCloudDataRequest, ApadanaMsgHeader.normal);

            var data = msg.GetData<LeaderBoardEntry>();
            Assert.Equal(data.playerId, req.playerId);
            Assert.Equal(data.userName, req.userName);
            Assert.Equal(data.entryTime.Second, req.entryTime.Second);
            Assert.Equal(data.score, req.score);
        }

        [Fact]
        public void MatchSyncEventDataSerialization()
        {
            Guid guid = Guid.NewGuid();
            MatchSyncEventData req =
                new MatchSyncEventData
                {
                    Game = guid,
                    gameProperties = new Dictionary<string, byte[]> {{ "g1", new byte[] { 1, 2, 3 } } },
                };
            req.playerProperties.Add("p2", new Dictionary<string, byte[]> { { "pl", new byte[] { 1, 2 } } });
            ApadanaMessageBase msg = ApadanaMessageBase.Create(req, ApadanaMsgType.MatchRejoinSyncEvent, ApadanaMsgHeader.normal);

            var data = msg.GetData<MatchSyncEventData>();
            Assert.Equal(data.Game, req.Game);
            Assert.Equal(data.playerProperties.Count, req.playerProperties.Count);
            Assert.Equal(data.playerProperties["p2"]["pl"][1], req.playerProperties["p2"]["pl"][1]);
            Assert.Equal(data.gameProperties.Count, req.gameProperties.Count);
            Assert.Equal(data.gameProperties["g1"][0], req.gameProperties["g1"][0]);
        }

        [Fact]
        public void FriendlistEntrySerialization()
        {
            FriendlistEntry req =
                new FriendlistEntry { userId = "u1", name = "king", status = OnlineStatus.Online, data = new Dictionary<string, string> { { "k", "v" } } };
            ApadanaMessageBase msg = ApadanaMessageBase.Create(req, ApadanaMsgType.SetCloudDataRequest, ApadanaMsgHeader.normal);

            var data = msg.GetData<FriendlistEntry>();
            Assert.Equal(data.userId, req.userId);
            Assert.Equal(data.name, req.name);
            Assert.Equal(data.status, req.status);
            Assert.Equal(data.data.Count, req.data.Count);
            foreach (var kvp in data.data)
                Assert.Equal(req.data[kvp.Key], kvp.Value);
        }

        [Fact]
        public void FriendlistInvitationSerialization()
        {
            FriendInvitation req =
                new FriendInvitation { OtherPlayerId = "u1", otherPlayerName = "king", creationTime = DateTime.Now, data = new byte[] { 1, 2, 3 } };
            ApadanaMessageBase msg = ApadanaMessageBase.Create(req, ApadanaMsgType.SetCloudDataRequest, ApadanaMsgHeader.normal);

            var data = msg.GetData<FriendInvitation>();
            Assert.Equal(data.OtherPlayerId, req.OtherPlayerId);
            Assert.Equal(data.otherPlayerName, req.otherPlayerName);
            Assert.Equal(data.creationTime.Second, req.creationTime.Second);
            Assert.Equal(data.data.Length, req.data.Length);
            for (int i = 0; i < data.data.Length; ++i)
                Assert.Equal(req.data[i], data.data[i]);
        }

        [Fact]
        public void FriendlistInvitationCollectionSerialization()
        {
            FriendInvitation inv =
                new FriendInvitation { OtherPlayerId = "u1", otherPlayerName = "king", creationTime = DateTime.Now, data = new byte[] { 1, 2, 3 } };
            FriendInvitationCollection req = new FriendInvitationCollection();
            req.invitations.Add(inv);
            req.invitations.Add(inv);
            ApadanaMessageBase msg = ApadanaMessageBase.Create(req, ApadanaMsgType.SetCloudDataRequest, ApadanaMsgHeader.normal);

            var data = msg.GetData<FriendInvitationCollection>();
            Assert.Equal(data.invitations[0].OtherPlayerId, req.invitations[0].OtherPlayerId);
            Assert.Equal(data.invitations[0].otherPlayerName, req.invitations[0].otherPlayerName);
            Assert.Equal(data.invitations[0].creationTime.Second, req.invitations[0].creationTime.Second);
            Assert.Equal(data.invitations[0].data.Length, req.invitations[0].data.Length);
        }

        [Fact]
        public void FriendlistCollectionSerialization()
        {
            FriendlistEntry entry =
                new FriendlistEntry { userId = "u1", name = "king", status = OnlineStatus.Online, data = new Dictionary<string, string> { { "k", "v" } } };
            FriendlistCollection list = new FriendlistCollection();
            list.friends.Add(entry);
            list.friends.Add(entry);
            list.receivedInvitations.invitations.Add(new FriendInvitation());
            ApadanaMessageBase msg = ApadanaMessageBase.Create(list, ApadanaMsgType.SetCloudDataRequest, ApadanaMsgHeader.normal);

            var data = msg.GetData<FriendlistCollection>();
            Assert.Equal(2, data.friends.Count);
            Assert.True(data.sentInvitation.invitations.Count == 0 && data.receivedInvitations.invitations.Count == 1);

            for (int i = 0; i < 2; ++i)
            {
                Assert.Equal(data.friends[i].userId, list.friends[i].userId);
                Assert.Equal(data.friends[i].name, list.friends[i].name);
                Assert.Equal(data.friends[i].status, list.friends[i].status);
                Assert.Equal(data.friends[i].data.Count, list.friends[i].data.Count);
                foreach (var kvp in data.friends[i].data)
                    Assert.Equal(list.friends[i].data[kvp.Key], kvp.Value);
            }
        }

        [Fact]
        public void SetLeaderBoardSerialization()
        {
            SetLeaderBoardRequest req =
                new SetLeaderBoardRequest { leaderBoardName = "global", score = 13 };
            ApadanaMessageBase msg = ApadanaMessageBase.Create(req, ApadanaMsgType.SetLeaderboardRequest, ApadanaMsgHeader.normal);

            var data = msg.GetData<SetLeaderBoardRequest>();
            Assert.Equal(data.leaderBoardName, req.leaderBoardName);
            Assert.Equal(data.score, req.score);
        }

        [Fact]
        public void GetLeaderBoardSerialization()
        {
            GetLeaderBoardRequest req =
                new GetLeaderBoardRequest { leaderBoardName = "l1", type = GetLeaderBoardRequest.RequestType.Top, count = 10, offset = 2 };
            ApadanaMessageBase msg = ApadanaMessageBase.Create(req, ApadanaMsgType.SetLeaderboardRequest, ApadanaMsgHeader.normal);

            var data = msg.GetData<GetLeaderBoardRequest>();
            Assert.Equal(data.leaderBoardName, req.leaderBoardName);
            Assert.Equal(data.count, req.count);
            Assert.Equal(data.offset, req.offset);
            Assert.Equal(data.type, req.type);
        }

        [Fact]
        public void AuthenticationRequestSerialization()
        {
            AuthenticationRequest req =
                new AuthenticationRequest
                { LoginType = LoginType.Device, Id = "AshkanDevice", AutoCreate = true, PasswordHash = "pass", data = new Dictionary<string, string>() };
            ApadanaMessageBase msg = ApadanaMessageBase.Create(req, ApadanaMsgType.AuthenticateRequest, ApadanaMsgHeader.normal);

            var data = msg.GetData<AuthenticationRequest>();
            Assert.Equal(data.Id, req.Id);
            Assert.Equal(data.LoginType, req.LoginType);
            Assert.Equal(data.PasswordHash, req.PasswordHash);
            Assert.Equal(data.AutoCreate, req.AutoCreate);
        }

        [Fact]
        public void AddAuthenticationToAccountRequestSerialization()
        {
            AddAuthenticationToAccountRequest req =
                new AddAuthenticationToAccountRequest
                { LoginType = LoginType.Device, Id = "AshkanDevice", PasswordHash = "pass", data = new Dictionary<string, string>() };
            ApadanaMessageBase msg = ApadanaMessageBase.Create(req, ApadanaMsgType.AuthenticateRequest, ApadanaMsgHeader.normal);

            var data = msg.GetData<AddAuthenticationToAccountRequest>();
            Assert.Equal(data.Id, req.Id);
            Assert.Equal(data.LoginType, req.LoginType);
            Assert.Equal(data.PasswordHash, req.PasswordHash);
        }

        [Fact]
        public void EmailActivationRequestSerialization()
        {
            EmailActivationRequest req =
                new EmailActivationRequest
                { email = "ac", ticket = "ticket", code = "1234" };
            ApadanaMessageBase msg = ApadanaMessageBase.Create(req, ApadanaMsgType.EmailActivationRequest, ApadanaMsgHeader.normal);

            var data = msg.GetData<EmailActivationRequest>();
            Assert.Equal(data.email, req.email);
            Assert.Equal(data.code, req.code);
            Assert.Equal(data.ticket, req.ticket);
        }

        [Fact]
        public void ScriptEventExecutionResponseSerialization()
        {
            ScriptEventExecutionResponseMessage req =
                new ScriptEventExecutionResponseMessage
                {
                    hasReturnValue = true,
                    returnType = ParamType.IntParam,
                    returnValue = 12
                };
            ApadanaMessageBase msg = ApadanaMessageBase.Create(req, ApadanaMsgType.ExecuteScriptEventResponse, ApadanaMsgHeader.normal);

            var data = msg.GetData<ScriptEventExecutionResponseMessage>();
            Assert.True(data.hasReturnValue);
            Assert.Equal(ParamType.IntParam, data.returnType);
            Assert.Equal(12, data.returnValue);
        }

        [Fact]
        public void ScriptEventExecutionRequestSerialization()
        {
            ScriptEventExecutionRequestMessage req =
                new ScriptEventExecutionRequestMessage
                {
                    eventName = "s1",
                    parameterTypess = new ParamType[]
                    { ParamType.IntParam,ParamType.StringParam,ParamType.ByteArrayParam,ParamType.BoolParam},
                    parameters = new object[] { 12, "hello", new byte[] { 1, 2, 3 }, true }
                };
            ApadanaMessageBase msg = ApadanaMessageBase.Create(req, ApadanaMsgType.ExecuteScriptEventRequest, ApadanaMsgHeader.normal);

            var data = msg.GetData<ScriptEventExecutionRequestMessage>();
            Assert.Equal("s1", data.eventName);
            Assert.Equal(4, data.parameters.Length);
            Assert.Equal(12, data.parameters[0]);
            Assert.Equal("hello", data.parameters[1]);
            Assert.Equal(true, data.parameters[3]);
            Assert.Equal(2, ((byte[])data.parameters[2])[1]);
        }

        [Fact]
        public void ChallengeMessageSerialization()
        {
            ChallengeMessage req =
                new ChallengeMessage
                {
                    Player = "u1",
                    GameKey = Guid.NewGuid(),
                    data = new byte[0],
                    PlayerName = "u2",
                    TimeOut = 12
                };
            ApadanaMessageBase msg = ApadanaMessageBase.Create(req, ApadanaMsgType.StartChallengeRequest, ApadanaMsgHeader.normal);

            var data = msg.GetData<ChallengeMessage>();
            Assert.Equal("u1", data.Player);
            Assert.Equal("u2",
                         data.PlayerName);
            Assert.Empty(data.data);
            Assert.Equal(12u, data.TimeOut);
            Assert.True(data.GameKey != Guid.Empty);
            Assert.NotNull(data.data);
        }

        [Fact]
        public void AuthenticationResponseSerialization()
        {
            AuthenticationResponse req =
                new AuthenticationResponse { name = "name", userId = "id", ticket = "ticket", isNewlyCreated = true };
            ApadanaMessageBase msg = ApadanaMessageBase.Create(req, ApadanaMsgType.AuthenticateResponse, ApadanaMsgHeader.normal);

            var data = msg.GetData<AuthenticationResponse>();
            Assert.Equal(data.userId, req.userId);
            Assert.Equal(data.ticket, req.ticket);
            Assert.Equal(data.name, req.name);
            Assert.Equal(data.isNewlyCreated, req.isNewlyCreated);
        }

        [Fact]
        public void SuccessfulResponseSerialization()
        {
            FindMatchResponse resp =
                new FindMatchResponse("game1", "nothing", new string[] { "first", "second" });
            OptionalResponse<FindMatchResponse> option = new OptionalResponse<FindMatchResponse>();
            option.isSuccesful = true;
            option.msg = resp;
            ApadanaMessageBase msg = ApadanaMessageBase.Create(option, ApadanaMsgType.FindMatchResponse, ApadanaMsgHeader.normal);
            msg.SetData(option);
            var data = msg.GetData<OptionalResponse<FindMatchResponse>>();
            Assert.True(data.isSuccesful);
            Assert.Equal(data.msg.gameId, resp.gameId);
            Assert.Equal(data.msg.data, resp.data);
            Assert.Equal(data.msg.players, resp.players);
        }

        [Fact]
        public void FailedResponseSerialization()
        {
            FindMatchResponse resp =
                new FindMatchResponse("game1", "nothing", new string[] { "first", "second" });
            OptionalResponse<FindMatchResponse> option = new OptionalResponse<FindMatchResponse>();
            option.isSuccesful = false;
            option.errorCode = 12;
            option.errorMsg = "something went wrong";
            option.msg = resp;
            ApadanaMessageBase msg = ApadanaMessageBase.Create(resp, ApadanaMsgType.FindMatchResponse, ApadanaMsgHeader.normal);
            msg.SetData(option);
            var data = msg.GetData<OptionalResponse<FindMatchResponse>>();
            Assert.False(data.isSuccesful);
            Assert.Null(data.msg);
            Assert.Equal(data.errorCode, option.errorCode);
            Assert.Equal(data.errorMsg, option.errorMsg);
        }

        #endregion

        #region GenericMessages

        [Fact]
        public void EmptyMessageSerialization()
        {
            EmptyMessage resp = new EmptyMessage();

            ApadanaMessageBase msg = ApadanaMessageBase.CreateWithGameTarget(resp, ApadanaMsgType.HighestInternal + 1, ApadanaMsgHeader.normal, "g1", "t1");

            var data = msg.GetData<EmptyMessage>();
            Assert.Equal("g1", msg.game);
            Assert.Equal("t1", msg.ticket);
        }

        [Fact]
        public void StringMessageSerialization()
        {
            StringMessage resp =
                new StringMessage { value = "hi" };

            ApadanaMessageBase msg = ApadanaMessageBase.Create(resp, ApadanaMsgType.HighestInternal + 1, ApadanaMsgHeader.normal);

            var data = msg.GetData<StringMessage>();
            Assert.Equal("hi", data.value);
        }

        [Fact]
        public void GuidMessageSerialization()
        {
            Guid g = Guid.NewGuid();
            GuidMessage resp = new GuidMessage { value = g };

            ApadanaMessageBase msg = ApadanaMessageBase.Create(resp, ApadanaMsgType.HighestInternal + 1, ApadanaMsgHeader.normal);

            var data = msg.GetData<GuidMessage>();
            Assert.Equal(data.value, g);
        }

        [Fact]
        public void IntegerMessageSerialization()
        {
            IntegerMessage resp =
                new IntegerMessage { value = 13 };

            ApadanaMessageBase msg = ApadanaMessageBase.Create(resp, ApadanaMsgType.HighestInternal + 1, ApadanaMsgHeader.normal);

            var data = msg.GetData<IntegerMessage>();
            Assert.Equal(13, data.value);
        }

        [Fact]
        public void RawMessageSerialization()
        {
            RawMessage resp =
                new RawMessage { value = new byte[] { 13, 13, 15 } };

            ApadanaMessageBase msg = ApadanaMessageBase.Create(resp, ApadanaMsgType.HighestInternal + 1, ApadanaMsgHeader.normal);

            var data = msg.GetData<RawMessage>();
            Assert.Equal(data.value, new byte[] { 13, 13, 15 });
        }

        #endregion

    }
}
