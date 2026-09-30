using Orleans;
using Orleans.TestingHost;
using WildCat.Gateway;
using WildCat.Interfaces;
using WildCat.Protocol;
using Xunit;

namespace WildCat.Tests
{
    /// <summary>
    /// Smoke tests for the message groups still served by the legacy adapter (until roadmap step
    /// 6b ports them): one message of each group reaches the old code, answers on its response type
    /// with only the request id in the header, and pushes still arrive. When a group is ported,
    /// its handler tests take over and these keep working unchanged.
    /// </summary>
    [Collection(ClusterCollection.Name)]
    public class GatewayAdapterTests
    {
        private readonly InProcessTestCluster cluster;
        private IGrainFactory GrainFactory => cluster.Client;

        public GatewayAdapterTests(ClusterFixture fixture)
        {
            cluster = fixture.Cluster;
        }

        private async Task<(GatewayTestHost Host, GatewayTestClient Client, string Ticket, string PlayerId, Guid GameKey)> StartLoggedIn()
        {
            var (developerKey, gameKey) = await TestSetup.CreateDeveloperAndGame(GrainFactory);
            var host = await GatewayTestHost.StartAsync(cluster.Client);
            var client = await host.ConnectAsync();
            var session = await client.InitializeSessionAsync(developerKey, gameKey);
            var login = await client.LoginWithDeviceAsync(session, TestSetup.UniqueId("device"));
            return (host, client, session.ToString(), login.userId, gameKey);
        }

        [Fact]
        public async Task ProfileCloudDataAndPublisherDataWorkThroughTheAdapter()
        {
            var (host, client, ticket, _, _) = await StartLoggedIn();
            await using var _ = host;
            await using var __ = client;

            Assert.True((await client.CallAsync<EmptyMessage>(WildCatMsgType.SetNameRequest, new StringMessage { value = "Ada" },
                WildCatMsgType.SetNameResponse, ticket)).isSuccesful);
            Assert.Equal("Ada", (await client.CallAsync<StringMessage>(WildCatMsgType.GetNameRequest, new EmptyMessage(),
                WildCatMsgType.GetNameResponse, ticket)).msg.value);

            Assert.True((await client.CallAsync<EmptyMessage>(WildCatMsgType.SetCloudDataRequest, new SetCloudDataRequest { key = "k", value = "v" },
                WildCatMsgType.SetCloudDataResponse, ticket)).isSuccesful);
            Assert.Equal("v", (await client.CallAsync<StringMessage>(WildCatMsgType.GetCloudDataRequest, new StringMessage { value = "k" },
                WildCatMsgType.GetCloudDataResponse, ticket)).msg.value);

            // A missing public value used to throw while writing the answer, so none arrived.
            var missing = await client.CallAsync<GetPublicCloudDataResponse>(WildCatMsgType.GetPublicCloudDataRequest,
                new StringMessage { value = "nothing-here" }, WildCatMsgType.GetPublicCloudDataResponse, ticket);
            Assert.True(missing.isSuccesful, missing.errorMsg);
            Assert.False(missing.msg.hasValue);

            // So did missing publisher data (a null string).
            var publisher = await client.CallAsync<StringMessage>(WildCatMsgType.GetPublisherDataRequest,
                new StringMessage { value = "nothing-here" }, WildCatMsgType.GetPublisherDataResponse, ticket);
            Assert.True(publisher.isSuccesful, publisher.errorMsg);
            Assert.Equal("", publisher.msg.value);
        }

        [Fact]
        public async Task ServerToServerPublisherDataNeedsNoSessionButTheSecret()
        {
            var (developerKey, _) = await TestSetup.CreateDeveloperAndGame(GrainFactory);
            await using var host = await GatewayTestHost.StartAsync(cluster.Client);
            await using var client = await host.ConnectAsync();

            var set = await client.CallAsync<EmptyMessage>(WildCatMsgType.SetPublisherDataRequest_S,
                new SetPublisherDataRequest { developerKey = developerKey, developerSecret = Guid.NewGuid(), key = "k", value = "v" },
                WildCatMsgType.SetPublisherDataResponse_S);
            Assert.False(set.isSuccesful);
            Assert.Equal(1, set.errorCode);
            var get = await client.CallAsync<StringMessage>(WildCatMsgType.GetPublisherDataRequest_S,
                new GetPublisherDataRequest { developerKey = developerKey, developerSecret = Guid.NewGuid(), key = "k" },
                WildCatMsgType.GetPublisherDataResponse_S);
            Assert.False(get.isSuccesful);
            Assert.Equal(1, get.errorCode);
        }

        [Fact]
        public async Task FriendsAndChallengesWorkThroughTheAdapterAndPushEvents()
        {
            var (host, alice, aliceTicket, aliceId, gameKey) = await StartLoggedIn();
            await using var _ = host;
            await using var __ = alice;
            await using var bob = await host.ConnectAsync();
            var developerKey = (await GrainFactory.GetGrain<ISession>(Guid.Parse(aliceTicket)).GetDeveloperKey()).DeveloperKey;
            var bobSession = await bob.InitializeSessionAsync(developerKey, gameKey);
            var bobLogin = await bob.LoginWithDeviceAsync(bobSession, TestSetup.UniqueId("device"));

            // GetFriends with public data keys used to throw while reading the request.
            var friends = await alice.CallAsync<FriendlistCollection>(WildCatMsgType.GetFriendsRequest,
                new GetFriendsRequestMessage { publicKeys = new[] { "level" } }, WildCatMsgType.GetFriendsResponse, aliceTicket);
            Assert.True(friends.isSuccesful, friends.errorMsg);
            Assert.Empty(friends.msg.friends);

            var invite = await alice.CallAsync<EmptyMessage>(WildCatMsgType.InviteFriendRequest,
                new InviteFriendRequest { type = InvitationType.AccountId, value = bobLogin.userId, data = new byte[] { 5 } },
                WildCatMsgType.InviteFriendResponse, aliceTicket);
            Assert.True(invite.isSuccesful, invite.errorMsg);
            var pushed = await bob.WaitForTypeAsync(WildCatMsgType.FriendshipInvitationEvent);
            var invitation = pushed.GetPayload<OptionalResponse<FriendlistMessage>>();
            Assert.True(invitation.isSuccesful);
            Assert.Equal(aliceId, invitation.msg.friend);
            Assert.Equal(new byte[] { 5 }, invitation.msg.data);

            var challenges = await alice.CallAsync<ChallengeRequestsMessage>(WildCatMsgType.GetPlayerChallengesRequest,
                new EmptyMessage(), WildCatMsgType.GetPlayerChallengesResponse, aliceTicket);
            Assert.True(challenges.isSuccesful, challenges.errorMsg);
        }

        [Fact]
        public async Task MatchesLeaderboardsInventoryAndScriptsAnswerThroughTheAdapter()
        {
            var (host, client, ticket, playerId, gameKey) = await StartLoggedIn();
            await using var _ = host;
            await using var __ = client;

            var pending = await client.CallAsync<PendingMatchesCollectionData>(WildCatMsgType.GetPlayerPendingMatchesRequest,
                new EmptyMessage(), WildCatMsgType.GetPlayerMatchesResponse, ticket);
            Assert.True(pending.isSuccesful, pending.errorMsg);
            Assert.Empty(pending.msg.matches);

            var inventory = await client.CallAsync<GetInventoryResponseMessage>(WildCatMsgType.GetInventoryRequest,
                new StringMessage(), WildCatMsgType.GetInventoryResponse, ticket);
            Assert.True(inventory.isSuccesful, inventory.errorMsg);
            Assert.Empty(inventory.msg.items);

            var leaderboard = await client.RequestAsync(client.Request(WildCatMsgType.GetLeaderboardRequest,
                new GetLeaderBoardRequest { leaderBoardName = "l1", type = GetLeaderBoardRequest.RequestType.Me }, ticket));
            Assert.Equal(WildCatMsgType.GetLeaderboardResponse, leaderboard.msgType);

            var script = await client.CallAsync<ScriptEventExecutionResponseMessage>(WildCatMsgType.ExecuteScriptEventRequest,
                new ScriptEventExecutionRequestMessage { eventName = "no-such-event" }, WildCatMsgType.ExecuteScriptEventResponse, ticket);
            Assert.False(script.isSuccesful);

            foreach (var (request, response) in new[]
                     {
                         (WildCatMsgType.SetLeaderboardRequest, WildCatMsgType.SetLeaderboardResponse),
                         (WildCatMsgType.AddItemToInventoryRequest, WildCatMsgType.AddItemToInventoryResponse),
                         (WildCatMsgType.SetItemToInventoryRequest, WildCatMsgType.SetItemToInventoryResponse),
                     })
            {
                var refused = await client.CallAsync<EmptyMessage>(request, new EmptyMessage(), response, ticket);
                Assert.False(refused.isSuccesful);
                Assert.Equal(GatewayErrors.Refused, refused.errorCode);
                Assert.Equal(GatewayErrors.ClientGrantRejected, refused.errorMsg);
            }
        }

        [Fact]
        public async Task LeaveGameAnswersInsteadOfThrowing()
        {
            // The answer used to be built with an empty ticket, which threw, so no answer went out.
            var (host, client, ticket, playerId, gameKey) = await StartLoggedIn();
            await using var _ = host;
            await using var __ = client;
            var matchId = Guid.NewGuid();
            var player = GrainFactory.GetGrain<IPlayer>(playerId);
            var other = GrainFactory.GetGrain<IPlayer>(TestSetup.UniqueId("other"));
            var game = GrainFactory.GetGrain<IRelayServerGame>(matchId, gameKey.ToString(), null);
            await game.StartGame(new[] { player, other }, null);
            await game.PlayerStarted(player);

            var leave = WildCatMessageBase.CreateWithGameTarget(new EmptyMessage(), WildCatMsgType.LeaveGameRequest,
                WildCatMsgHeader.normal, matchId.ToString(), ticket);
            leave.SetRequestID(client.NextRequestId());
            var reply = await client.RequestAsync(leave);

            Assert.Equal(WildCatMsgType.LeaveGameResponse, reply.msgType);
            Assert.Equal(WildCatMsgHeader.HasRequestID, reply.header);
            Assert.True(reply.GetPayload<OptionalResponse<EmptyMessage>>().isSuccesful);
        }

        [Fact]
        public async Task AdapterTypesStillNeedALoggedInPlayer()
        {
            await using var host = await GatewayTestHost.StartAsync(cluster.Client);
            await using var client = await host.ConnectAsync();

            // A ticket-less request used to reach the old code with no player and fail with a
            // NullReferenceException text; now the dispatcher refuses it first.
            var reply = await client.CallAsync<EmptyMessage>(WildCatMsgType.GetFriendsRequest, new GetFriendsRequestMessage(), WildCatMsgType.ErrorMsg);
            Assert.Equal(GatewayErrors.SessionRequired, reply.errorMsg);
        }
    }
}
