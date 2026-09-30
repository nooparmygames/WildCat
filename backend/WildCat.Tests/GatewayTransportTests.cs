using System.Net.WebSockets;
using Microsoft.Extensions.Logging;
using Orleans;
using Orleans.TestingHost;
using WildCat.Interfaces;
using WildCat.Protocol;
using Xunit;

namespace WildCat.Tests
{
    /// <summary>
    /// The WebSocket transport: reassembling fragmented messages, the message size limit, text
    /// messages, and closing.
    /// </summary>
    [Collection(ClusterCollection.Name)]
    public class GatewayTransportTests
    {
        private readonly InProcessTestCluster cluster;
        private IGrainFactory GrainFactory => cluster.Client;

        public GatewayTransportTests(ClusterFixture fixture)
        {
            cluster = fixture.Cluster;
        }

        /// <summary>
        /// An InitializeSession request whose payload has <paramref name="padding"/> extra bytes at
        /// the end, which the payload decoder ignores, to make a large but valid frame.
        /// </summary>
        private static WildCatMessageBase PaddedInitializeSession(GatewayTestClient client, Guid developerKey, Guid gameKey, int padding)
        {
            var request = client.Request(WildCatMsgType.InitializeSessionRequest,
                new InitializeSessionRequest { DeveloperKey = developerKey, GameKey = gameKey });
            request.RawData = request.RawData.Concat(new byte[padding]).ToArray();
            return request;
        }

        [Fact]
        public async Task AFragmentedMessageIsPutBackTogether()
        {
            var (developerKey, gameKey) = await TestSetup.CreateDeveloperAndGame(GrainFactory);
            await using var host = await GatewayTestHost.StartAsync(cluster.Client);
            await using var client = await host.ConnectAsync();

            var small = client.Request(WildCatMsgType.InitializeSessionRequest,
                new InitializeSessionRequest { DeveloperKey = developerKey, GameKey = gameKey });
            await client.SendFragmentedAsync(WireCodec.Encode(small), 7);
            var reply = await client.WaitForAsync(m => m.requestID == small.requestID, "the reply to the fragmented request");
            Assert.True(reply.GetPayload<OptionalResponse<GuidMessage>>().isSuccesful);

            var large = PaddedInitializeSession(client, developerKey, gameKey, 200_000);
            await client.SendFragmentedAsync(WireCodec.Encode(large), 4096);
            reply = await client.WaitForAsync(m => m.requestID == large.requestID, "the reply to the large request");
            Assert.True(reply.GetPayload<OptionalResponse<GuidMessage>>().isSuccesful);
        }

        [Fact]
        public async Task AMessageOverTheLimitClosesTheSocketWithMessageTooBig()
        {
            var (developerKey, gameKey) = await TestSetup.CreateDeveloperAndGame(GrainFactory);
            await using var host = await GatewayTestHost.StartAsync(cluster.Client, o => o.MaxMessageBytes = 1024);
            await using var client = await host.ConnectAsync();

            var fits = PaddedInitializeSession(client, developerKey, gameKey, 900);
            Assert.True((await client.RequestAsync(fits)).GetPayload<OptionalResponse<GuidMessage>>().isSuccesful);

            await client.SendFragmentedAsync(WireCodec.Encode(PaddedInitializeSession(client, developerKey, gameKey, 2000)), 512);
            await client.Closed.WaitAsync(TestSetup.Timeout);

            Assert.Equal(WebSocketCloseStatus.MessageTooBig, client.CloseStatus);
            await TestSetup.WaitUntilAsync(() => host.Connections.Count == 0, "the connection to be released");
            Assert.Contains(host.Logs.Entries, e => e.Level == LogLevel.Warning && e.Message.Contains("larger than 1024 bytes"));
        }

        [Fact]
        public async Task TextMessagesAreIgnored()
        {
            var (developerKey, gameKey) = await TestSetup.CreateDeveloperAndGame(GrainFactory);
            await using var host = await GatewayTestHost.StartAsync(cluster.Client);
            await using var client = await host.ConnectAsync();

            await client.SendTextAsync("hello");
            Assert.NotEqual(Guid.Empty, await client.InitializeSessionAsync(developerKey, gameKey));
            Assert.Single(client.Received);
        }

        [Fact]
        public async Task AClosedSocketIsAnsweredAndItsLoginReleased()
        {
            var (developerKey, gameKey) = await TestSetup.CreateDeveloperAndGame(GrainFactory);
            await using var host = await GatewayTestHost.StartAsync(cluster.Client);
            var client = await host.ConnectAsync();
            var session = await client.InitializeSessionAsync(developerKey, gameKey);
            var login = await client.LoginWithDeviceAsync(session, TestSetup.UniqueId("device"));
            Assert.Equal(1, host.Connections.Count);

            // The close handshake completes (DisposeAsync would abort after 5 seconds otherwise).
            var started = DateTime.UtcNow;
            await client.DisposeAsync();
            Assert.True(DateTime.UtcNow - started < TimeSpan.FromSeconds(4));

            await TestSetup.WaitUntilAsync(() => host.Connections.Count == 0, "the connection to be released");
            Assert.DoesNotContain(login.userId, host.Connections.PlayerIds);
            await TestSetup.WaitUntilAsync(async () => !await GrainFactory.GetGrain<IPlayer>(login.userId).IsLoggedIn(),
                "the player to be logged out");
        }
    }
}
