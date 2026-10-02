using Orleans;
using Orleans.TestingHost;
using WildCat.Gateway;
using WildCat.Interfaces;
using WildCat.Protocol;
using Xunit;

namespace WildCat.Tests
{
    /// <summary>
    /// The Session handlers over real WebSockets: InitializeSession (6), HeartBeat (100) and
    /// GetServerTime (42).
    /// </summary>
    [Collection(ClusterCollection.Name)]
    public class GatewaySessionHandlerTests
    {
        private readonly InProcessTestCluster cluster;
        private IGrainFactory GrainFactory => cluster.Client;

        public GatewaySessionHandlerTests(ClusterFixture fixture)
        {
            cluster = fixture.Cluster;
        }

        [Fact]
        public async Task InitializeSessionOpensASessionForTheGame()
        {
            var (developerKey, gameKey) = await TestSetup.CreateDeveloperAndGame(GrainFactory);
            await using var host = await GatewayTestHost.StartAsync(cluster.Client);
            await using var client = await host.ConnectAsync();

            var response = await client.CallAsync<GuidMessage>(WildCatMsgType.InitializeSessionRequest,
                new InitializeSessionRequest { DeveloperKey = developerKey, GameKey = gameKey },
                WildCatMsgType.InitializeSessionResponse);

            Assert.True(response.isSuccesful, response.errorMsg);
            var session = GrainFactory.GetGrain<ISession>(response.msg.value);
            Assert.True(await session.IsInitialized());
            var info = await session.GetDeveloperKey();
            Assert.Equal(developerKey, info.DeveloperKey);
            Assert.Equal(gameKey, info.GameKey);
            Assert.True(string.IsNullOrEmpty(info.UserID));
        }

        [Fact]
        public async Task InitializeSessionRefusesAGameThatIsNotTheDevelopers()
        {
            var (developerKey, _) = await TestSetup.CreateDeveloperAndGame(GrainFactory);
            var (_, otherGame) = await TestSetup.CreateDeveloperAndGame(GrainFactory);
            await using var host = await GatewayTestHost.StartAsync(cluster.Client);
            await using var client = await host.ConnectAsync();

            var wrongGame = await client.CallAsync<GuidMessage>(WildCatMsgType.InitializeSessionRequest,
                new InitializeSessionRequest { DeveloperKey = developerKey, GameKey = otherGame },
                WildCatMsgType.InitializeSessionResponse);
            Assert.False(wrongGame.isSuccesful);
            Assert.Equal(GatewayErrors.Refused, wrongGame.errorCode);
            Assert.Equal($"The game key {otherGame} is not valid.", wrongGame.errorMsg);

            var unknownDeveloper = await client.CallAsync<GuidMessage>(WildCatMsgType.InitializeSessionRequest,
                new InitializeSessionRequest { DeveloperKey = Guid.NewGuid(), GameKey = otherGame },
                WildCatMsgType.InitializeSessionResponse);
            Assert.False(unknownDeveloper.isSuccesful);
            Assert.Equal(GatewayErrors.Refused, unknownDeveloper.errorCode);
        }

        [Fact]
        public async Task InitializeSessionIgnoresHeaderFieldsItDoesNotUse()
        {
            // Player targets used to make the response throw, because responses copied the
            // request header. Now the response carries only the request id.
            var (developerKey, gameKey) = await TestSetup.CreateDeveloperAndGame(GrainFactory);
            await using var host = await GatewayTestHost.StartAsync(cluster.Client);
            await using var client = await host.ConnectAsync();

            var request = WildCatMessageBase.CreateWithPlayerTargets(
                new InitializeSessionRequest { DeveloperKey = developerKey, GameKey = gameKey },
                WildCatMsgType.InitializeSessionRequest, WildCatMsgHeader.normal, new[] { "someone" }, "not-a-ticket");
            request.SetRequestID(client.NextRequestId());
            var reply = await client.RequestAsync(request);

            Assert.Equal(WildCatMsgType.InitializeSessionResponse, reply.msgType);
            Assert.Equal(WildCatMsgHeader.HasRequestID, reply.header);
            Assert.True(reply.GetPayload<OptionalResponse<GuidMessage>>().isSuccesful);
        }

        [Fact]
        public async Task HeartBeatNeedsALoggedInPlayerAndKeepsItOnline()
        {
            var (developerKey, gameKey) = await TestSetup.CreateDeveloperAndGame(GrainFactory);
            await using var host = await GatewayTestHost.StartAsync(cluster.Client);
            await using var client = await host.ConnectAsync();
            var session = await client.InitializeSessionAsync(developerKey, gameKey);

            var noTicket = await client.CallAsync<EmptyMessage>(WildCatMsgType.HeartBeatRequest, new EmptyMessage(), WildCatMsgType.ErrorMsg);
            Assert.Equal(GatewayErrors.SessionRequired, noTicket.errorMsg);
            var notLoggedIn = await client.CallAsync<EmptyMessage>(WildCatMsgType.HeartBeatRequest, new EmptyMessage(),
                WildCatMsgType.ErrorMsg, session.ToString());
            Assert.Equal(GatewayErrors.NotLoggedIn, notLoggedIn.errorMsg);

            var login = await client.LoginWithDeviceAsync(session, TestSetup.UniqueId("device"));
            var player = GrainFactory.GetGrain<IPlayer>(login.userId);
            var before = (await player.GetState()).LastHeartBeat;
            await Task.Delay(20);

            var beat = await client.CallAsync<EmptyMessage>(WildCatMsgType.HeartBeatRequest, new EmptyMessage(),
                WildCatMsgType.HeartBeatResponse, session.ToString());

            Assert.True(beat.isSuccesful, beat.errorMsg);
            var state = await player.GetState();
            Assert.True(state.LastHeartBeat > before);
            Assert.Equal(WildCat.Interfaces.OnlineStatus.Online, state.OnlineStatus);
        }

        [Fact]
        public async Task GetServerTimeAnswersTheUtcTimeWithoutASession()
        {
            await using var host = await GatewayTestHost.StartAsync(cluster.Client);
            await using var client = await host.ConnectAsync();

            var before = DateTime.UtcNow;
            var time = await client.CallAsync<DateTimeMessage>(WildCatMsgType.GetServerTimeRequest, new EmptyMessage(),
                WildCatMsgType.GetServerTimeResponse);
            var after = DateTime.UtcNow;
            Assert.True(time.isSuccesful, time.errorMsg);
            Assert.Equal(DateTimeKind.Utc, time.msg.value.Kind);
            Assert.InRange(time.msg.value, before, after);

            // The SDK sends its ticket along; it is not checked.
            var withTicket = await client.CallAsync<DateTimeMessage>(WildCatMsgType.GetServerTimeRequest, new EmptyMessage(),
                WildCatMsgType.GetServerTimeResponse, Guid.NewGuid().ToString());
            Assert.True(withTicket.isSuccesful, withTicket.errorMsg);
        }
    }
}
