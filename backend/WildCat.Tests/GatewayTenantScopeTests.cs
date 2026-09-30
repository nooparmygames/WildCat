using Orleans;
using Orleans.TestingHost;
using WildCat.Interfaces;
using WildCat.Protocol;
using Xunit;

namespace WildCat.Tests
{
    /// <summary>
    /// Player data belongs to the game of the request's session. One account (a device id) plays
    /// games of two developers here. The cloud data handlers used to go through the Player grain,
    /// which used the game the account had last logged in to, so another developer's data could
    /// be written, read by others or listed with friends.
    /// </summary>
    [Collection(ClusterCollection.Name)]
    public class GatewayTenantScopeTests
    {
        private readonly InProcessTestCluster cluster;
        private IGrainFactory GrainFactory => cluster.Client;

        public GatewayTenantScopeTests(ClusterFixture fixture)
        {
            cluster = fixture.Cluster;
        }

        private static async Task<OptionalResponse<T>> Call<T>(GatewayTestClient client, string ticket, WildCatMsgType type,
            MessageBase payload, WildCatMsgType responseType) where T : MessageBase, new()
        {
            var response = await client.CallAsync<T>(type, payload, responseType, ticket);
            Assert.True(response.isSuccesful, response.errorMsg);
            return response;
        }

        private static Task SetPrivate(GatewayTestClient client, string ticket, string key, string value)
        {
            return Call<EmptyMessage>(client, ticket, WildCatMsgType.SetCloudDataRequest,
                new SetCloudDataRequest { key = key, value = value }, WildCatMsgType.SetCloudDataResponse);
        }

        private static Task SetPublic(GatewayTestClient client, string ticket, string key, string value)
        {
            return Call<EmptyMessage>(client, ticket, WildCatMsgType.SetPublicCloudDataRequest,
                new SetCloudDataRequest { key = key, value = value }, WildCatMsgType.SetPublicCloudDataResponse);
        }

        private static async Task<string> GetPrivate(GatewayTestClient client, string ticket, string key)
        {
            return (await Call<StringMessage>(client, ticket, WildCatMsgType.GetCloudDataRequest,
                new StringMessage { value = key }, WildCatMsgType.GetCloudDataResponse)).msg.value;
        }

        private static async Task<string> GetPublic(GatewayTestClient client, string ticket, string key)
        {
            var answer = (await Call<GetPublicCloudDataResponse>(client, ticket, WildCatMsgType.GetPublicCloudDataRequest,
                new StringMessage { value = key }, WildCatMsgType.GetPublicCloudDataResponse)).msg;
            return answer.hasValue ? answer.value : null;
        }

        private static async Task<string> GetOthersPublic(GatewayTestClient client, string ticket, string playerId, string key)
        {
            var answer = (await Call<GetPlayerPublicCloudDataResponse>(client, ticket, WildCatMsgType.GetPlayerPublicCloudDataRequest,
                new GetPlayerPublicCloudDataRequest { player = playerId, key = key }, WildCatMsgType.GetPlayerPublicCloudDataResponse)).msg;
            return answer.hasValue ? answer.value : null;
        }

        private IPlayerStorage Storage(Guid gameKey, string playerId)
        {
            return GrainFactory.GetGrain<IPlayerStorage>(ResourcesUtility.GetPlayPlayerStorageKey(gameKey.ToString(), playerId));
        }

        [Fact]
        public async Task CloudDataStaysInTheSessionsGame()
        {
            var (developer1, game1) = await TestSetup.CreateDeveloperAndGame(GrainFactory);
            var (developer2, game2) = await TestSetup.CreateDeveloperAndGame(GrainFactory);
            await using var host = await GatewayTestHost.StartAsync(cluster.Client);
            string device = TestSetup.UniqueId("device");

            // Alice plays game 1 of developer 1 ...
            await using var inGame1 = await host.ConnectAsync();
            var session1 = await inGame1.InitializeSessionAsync(developer1, game1);
            string alice = (await inGame1.LoginWithDeviceAsync(session1, device)).userId;
            await SetPrivate(inGame1, session1.ToString(), "progress", "game 1");
            await SetPublic(inGame1, session1.ToString(), "title", "game 1 title");

            // ... then game 2 of developer 2 with the same account.
            await using var inGame2 = await host.ConnectAsync();
            var session2 = await inGame2.InitializeSessionAsync(developer2, game2);
            Assert.Equal(alice, (await inGame2.LoginWithDeviceAsync(session2, device)).userId);
            Assert.Equal("", await GetPrivate(inGame2, session2.ToString(), "progress"));
            await SetPrivate(inGame2, session2.ToString(), "progress", "game 2");
            await SetPublic(inGame2, session2.ToString(), "title", "game 2 title");

            // Bob plays game 1 and reads Alice's public data of game 1, not of the game she played last.
            await using var bob = await host.ConnectAsync();
            var bobSession = await bob.InitializeSessionAsync(developer1, game1);
            await bob.LoginWithDeviceAsync(bobSession, TestSetup.UniqueId("device"));
            Assert.Equal("game 1 title", await GetOthersPublic(bob, bobSession.ToString(), alice, "title"));

            // A game 1 session of Alice while her last login is game 2's (the window a login on another
            // socket opens for requests already past the session check): its requests use game 1.
            var session3 = await GrainFactory.GetGrain<IDeveloper>(developer1).InitializeSession(game1);
            await GrainFactory.GetGrain<ISession>(session3).StartAuthenticatedUserSession("", alice);
            Assert.Equal(game2, await GrainFactory.GetGrain<IPlayer>(alice).GetGameTitleKey());
            await using var late = await host.ConnectAsync();
            Assert.Equal("game 1", await GetPrivate(late, session3.ToString(), "progress"));
            Assert.Equal("game 1 title", await GetPublic(late, session3.ToString(), "title"));
            await SetPrivate(late, session3.ToString(), "progress", "game 1 again");
            await SetPublic(late, session3.ToString(), "title", "game 1 title again");

            Assert.Equal("game 1 again", (await Storage(game1, alice).GetPlayerString("progress")).Item2);
            Assert.Equal("game 1 title again", (await Storage(game1, alice).GetPlayerPublicString("title")).Item2);
            Assert.Equal("game 2", (await Storage(game2, alice).GetPlayerString("progress")).Item2);
            Assert.Equal("game 2 title", (await Storage(game2, alice).GetPlayerPublicString("title")).Item2);
        }

        [Fact]
        public async Task FriendsSeePublicDataOfTheSessionsGame()
        {
            var (developer1, game1) = await TestSetup.CreateDeveloperAndGame(GrainFactory);
            var (developer2, game2) = await TestSetup.CreateDeveloperAndGame(GrainFactory);
            await using var host = await GatewayTestHost.StartAsync(cluster.Client);
            string aliceDevice = TestSetup.UniqueId("device");

            await using var alice1 = await host.ConnectAsync();
            var aliceSession1 = await alice1.InitializeSessionAsync(developer1, game1);
            string alice = (await alice1.LoginWithDeviceAsync(aliceSession1, aliceDevice)).userId;
            await SetPublic(alice1, aliceSession1.ToString(), "level", "7");

            await using var bob = await host.ConnectAsync();
            var bobSession = await bob.InitializeSessionAsync(developer1, game1);
            string bobId = (await bob.LoginWithDeviceAsync(bobSession, TestSetup.UniqueId("device"))).userId;
            var aliceList = GrainFactory.GetGrain<IPlayerFriendList>(game1, alice, null);
            await aliceList.Invite(GrainFactory.GetGrain<IPlayer>(bobId), "Alice", new byte[0]);
            await GrainFactory.GetGrain<IPlayerFriendList>(game1, bobId, null).AcceptInvitation(GrainFactory.GetGrain<IPlayer>(alice), new byte[0]);

            // Alice goes on to play another developer's game and sets the same key there.
            await using var alice2 = await host.ConnectAsync();
            var aliceSession2 = await alice2.InitializeSessionAsync(developer2, game2);
            await alice2.LoginWithDeviceAsync(aliceSession2, aliceDevice);
            await SetPublic(alice2, aliceSession2.ToString(), "level", "99");

            var friends = (await Call<FriendlistCollection>(bob, bobSession.ToString(), WildCatMsgType.GetFriendsRequest,
                new GetFriendsRequestMessage { publicKeys = new[] { "level", "missing", "level" } }, WildCatMsgType.GetFriendsResponse)).msg;
            var friend = Assert.Single(friends.friends);
            Assert.Equal(alice, friend.userId);
            Assert.Equal(new Dictionary<string, string> { ["level"] = "7" }, friend.data);
        }

        [Fact]
        public async Task GetPublicStringsInGameReadsTheNamedGame()
        {
            var (developer, game1) = await TestSetup.CreateDeveloperAndGame(GrainFactory);
            var game2 = await GrainFactory.GetGrain<IDeveloper>(developer).AddGame(new GameInfo { Name = "game2" });
            var session = await GrainFactory.GetGrain<IDeveloper>(developer).InitializeSession(game2);
            var player = await TestSetup.CreateLoggedInPlayer(GrainFactory, session, Guid.NewGuid());
            string id = player.GetPrimaryKeyString();
            await Storage(game1, id).SetPlayerPublicString("level", "1");
            await Storage(game2, id).SetPlayerPublicString("level", "2");

            var inGame1 = await player.GetPublicStringsInGame(game1, new[] { "level" });
            Assert.Equal(WildCat.Interfaces.OnlineStatus.Online, inGame1.Item1);
            Assert.Equal("1", inGame1.Item2["level"]);
            Assert.Equal("2", (await player.GetPublicStringsInGame(game2, new[] { "level" })).Item2["level"]);
            // The old overload follows the last login, game 2 here.
            Assert.Equal("2", (await player.GetPlayerPublicStrings(new[] { "level" })).Item2["level"]);
        }
    }
}
