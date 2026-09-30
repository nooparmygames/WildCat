using Orleans;
using Orleans.TestingHost;
using WildCat.Gateway;
using WildCat.Interfaces;
using WildCat.Protocol;
using Xunit;
using static WildCat.Tests.GatewayProfileHandlerTests;

namespace WildCat.Tests
{
    /// <summary>
    /// The PublisherData handlers over real WebSockets: GetPublisherData (34) from game clients and
    /// the server to server SetPublisherData_S (62) and GetPublisherData_S (64), which carry the
    /// developer key and secret instead of a session. Each test uses its own developer and game.
    /// </summary>
    [Collection(ClusterCollection.Name)]
    public class GatewayPublisherDataHandlerTests
    {
        private readonly InProcessTestCluster cluster;
        private IGrainFactory GrainFactory => cluster.Client;

        public GatewayPublisherDataHandlerTests(ClusterFixture fixture)
        {
            cluster = fixture.Cluster;
        }

        private async Task<Guid> SecretOf(Guid developerKey)
        {
            var secret = (await GrainFactory.GetGrain<IDeveloper>(developerKey).GetDeveloperData()).DeveloperSecret;
            Assert.NotEqual(Guid.Empty, secret);
            return secret;
        }

        private static Task<OptionalResponse<EmptyMessage>> ServerSet(GatewayTestClient client, Guid developerKey, Guid secret,
            string key, string value, string ticket = null)
        {
            return client.CallAsync<EmptyMessage>(WildCatMsgType.SetPublisherDataRequest_S,
                new SetPublisherDataRequest { developerKey = developerKey, developerSecret = secret, key = key, value = value },
                WildCatMsgType.SetPublisherDataResponse_S, ticket);
        }

        private static Task<OptionalResponse<StringMessage>> ServerGet(GatewayTestClient client, Guid developerKey, Guid secret,
            string key)
        {
            return client.CallAsync<StringMessage>(WildCatMsgType.GetPublisherDataRequest_S,
                new GetPublisherDataRequest { developerKey = developerKey, developerSecret = secret, key = key },
                WildCatMsgType.GetPublisherDataResponse_S);
        }

        private static async Task<string> ClientGet(LoggedIn player, string key)
        {
            var response = await player.Client.CallAsync<StringMessage>(WildCatMsgType.GetPublisherDataRequest,
                new StringMessage { value = key }, WildCatMsgType.GetPublisherDataResponse, player.Ticket);
            Assert.True(response.isSuccesful, response.errorMsg);
            return response.msg.value;
        }

        [Fact]
        public async Task TheServerWritesWithTheSecretAndGameClientsRead()
        {
            var (developerKey, gameKey) = await TestSetup.CreateDeveloperAndGame(GrainFactory);
            var secret = await SecretOf(developerKey);
            await using var host = await GatewayTestHost.StartAsync(cluster.Client);
            await using var server = await host.ConnectAsync();
            var player = await LogIn(host, developerKey, gameKey);
            await using var _ = player.Client;

            // Missing keys read as "" on both paths (a null string used to break the answer).
            Assert.Equal("", await ClientGet(player, "motd"));
            var missing = await ServerGet(server, developerKey, secret, "motd");
            Assert.True(missing.isSuccesful, missing.errorMsg);
            Assert.Equal("", missing.msg.value);

            var set = await ServerSet(server, developerKey, secret, "motd", Unicode);
            Assert.True(set.isSuccesful, set.errorMsg);
            Assert.Equal(Unicode, await ClientGet(player, "motd"));
            var read = await ServerGet(server, developerKey, secret, "motd");
            Assert.True(read.isSuccesful, read.errorMsg);
            Assert.Equal(Unicode, read.msg.value);
            Assert.Equal(Unicode, await GrainFactory.GetGrain<IDeveloperStorage>(developerKey).GetString("motd"));

            Assert.True((await ServerSet(server, developerKey, secret, "motd", "")).isSuccesful);
            Assert.Equal("", await ClientGet(player, "motd"));
            Assert.True((await ServerSet(server, developerKey, secret, Unicode, "unicode key")).isSuccesful);
            Assert.Equal("unicode key", await ClientGet(player, Unicode));
        }

        [Fact]
        public async Task AWrongSecretOrAnUnknownDeveloperIsRefused()
        {
            var (developerKey, _) = await TestSetup.CreateDeveloperAndGame(GrainFactory);
            var secret = await SecretOf(developerKey);
            var (otherDeveloperKey, _) = await TestSetup.CreateDeveloperAndGame(GrainFactory);
            var otherSecret = await SecretOf(otherDeveloperKey);
            await using var host = await GatewayTestHost.StartAsync(cluster.Client);
            await using var server = await host.ConnectAsync();
            Assert.True((await ServerSet(server, developerKey, secret, "k", "kept")).isSuccesful);

            foreach (var (key, wrong) in new[]
                     {
                         (developerKey, Guid.NewGuid()),
                         (developerKey, Guid.Empty),
                         (developerKey, otherSecret),
                         (Guid.NewGuid(), secret),
                         (Guid.Empty, Guid.Empty),
                     })
            {
                AssertFailed(await ServerSet(server, key, wrong, "k", "changed"), GatewayErrors.Refused, DeveloperSecret.Wrong);
                AssertFailed(await ServerGet(server, key, wrong, "k"), GatewayErrors.Refused, DeveloperSecret.Wrong);
            }
            Assert.Equal("kept", await GrainFactory.GetGrain<IDeveloperStorage>(developerKey).GetString("k"));

            // A regenerated secret replaces the old one at once.
            var newSecret = await GrainFactory.GetGrain<IDeveloper>(developerKey).GenerateSecret();
            AssertFailed(await ServerGet(server, developerKey, secret, "k"), GatewayErrors.Refused, DeveloperSecret.Wrong);
            var read = await ServerGet(server, developerKey, newSecret, "k");
            Assert.True(read.isSuccesful, read.errorMsg);
            Assert.Equal("kept", read.msg.value);

            // Expected refusals, not server faults.
            Assert.DoesNotContain(host.Logs.Entries, e => e.Level >= Microsoft.Extensions.Logging.LogLevel.Error);
        }

        [Fact]
        public async Task ServerToServerRequestsNeedNoSessionAndIgnoreAHeaderTicket()
        {
            var (developerKey, _) = await TestSetup.CreateDeveloperAndGame(GrainFactory);
            var secret = await SecretOf(developerKey);
            await using var host = await GatewayTestHost.StartAsync(cluster.Client);
            await using var server = await host.ConnectAsync();

            var noTicket = await ServerSet(server, developerKey, secret, "k", "one");
            Assert.True(noTicket.isSuccesful, noTicket.errorMsg);
            var badTicket = await ServerSet(server, developerKey, secret, "k", "two", "not-a-guid");
            Assert.True(badTicket.isSuccesful, badTicket.errorMsg);
            var unknownTicket = await ServerSet(server, developerKey, secret, "k", "three", Guid.NewGuid().ToString());
            Assert.True(unknownTicket.isSuccesful, unknownTicket.errorMsg);
            Assert.Equal("three", await GrainFactory.GetGrain<IDeveloperStorage>(developerKey).GetString("k"));
        }

        [Fact]
        public async Task GameClientsReadThePublisherDataOfTheirSessionsDeveloper()
        {
            var (developerKey, gameKey) = await TestSetup.CreateDeveloperAndGame(GrainFactory);
            var (otherDeveloperKey, otherGameKey) = await TestSetup.CreateDeveloperAndGame(GrainFactory);
            await using var host = await GatewayTestHost.StartAsync(cluster.Client);
            await using var server = await host.ConnectAsync();
            Assert.True((await ServerSet(server, developerKey, await SecretOf(developerKey), "k", "mine")).isSuccesful);
            Assert.True((await ServerSet(server, otherDeveloperKey, await SecretOf(otherDeveloperKey), "k", "theirs")).isSuccesful);
            var player = await LogIn(host, developerKey, gameKey);
            await using var _ = player.Client;
            var otherPlayer = await LogIn(host, otherDeveloperKey, otherGameKey);
            await using var __ = otherPlayer.Client;

            Assert.Equal("mine", await ClientGet(player, "k"));
            Assert.Equal("theirs", await ClientGet(otherPlayer, "k"));
        }
    }
}
