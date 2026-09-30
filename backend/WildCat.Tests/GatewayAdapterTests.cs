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
        public async Task ScriptEventsAnswerThroughTheAdapter()
        {
            var (host, client, ticket, _, _) = await StartLoggedIn();
            await using var _ = host;
            await using var __ = client;

            var script = await client.CallAsync<ScriptEventExecutionResponseMessage>(WildCatMsgType.ExecuteScriptEventRequest,
                new ScriptEventExecutionRequestMessage { eventName = "no-such-event" }, WildCatMsgType.ExecuteScriptEventResponse, ticket);
            Assert.False(script.isSuccesful);
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
