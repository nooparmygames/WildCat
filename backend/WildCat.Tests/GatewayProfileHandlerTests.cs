using Orleans;
using Orleans.TestingHost;
using WildCat.Gateway;
using WildCat.Interfaces;
using WildCat.Protocol;
using Xunit;

namespace WildCat.Tests
{
    /// <summary>
    /// The Profile and CloudData handlers over real WebSockets: GetName (36), SetName (38),
    /// GetPlayerName (68), GetCloudData (30), SetCloudData (32), SetPublicCloudData (72),
    /// GetPublicCloudData (74) and GetPlayerPublicCloudData (70), plus the session requirement of
    /// every group 1 message. Each test uses its own developer, game and players.
    /// </summary>
    [Collection(ClusterCollection.Name)]
    public class GatewayProfileHandlerTests
    {
        /// <summary>Text with two-byte, three-byte and four-byte UTF-8 characters (the source stays ASCII).</summary>
        internal const string Unicode = "Zo\u00eb \u674e\u767d \U0001F431 \u0633\u0644\u0627\u0645";

        private readonly InProcessTestCluster cluster;
        private IGrainFactory GrainFactory => cluster.Client;

        public GatewayProfileHandlerTests(ClusterFixture fixture)
        {
            cluster = fixture.Cluster;
        }

        internal sealed record LoggedIn(GatewayTestClient Client, string Ticket, string PlayerId);

        internal static async Task<LoggedIn> LogIn(GatewayTestHost host, Guid developerKey, Guid gameKey)
        {
            var client = await host.ConnectAsync();
            var session = await client.InitializeSessionAsync(developerKey, gameKey);
            var login = await client.LoginWithDeviceAsync(session, TestSetup.UniqueId("device"));
            return new LoggedIn(client, session.ToString(), login.userId);
        }

        internal static void AssertFailed<T>(OptionalResponse<T> response, int code, string message) where T : MessageBase, new()
        {
            Assert.False(response.isSuccesful);
            Assert.Equal(code, response.errorCode);
            Assert.Equal(message, response.errorMsg);
        }

        private static Task<OptionalResponse<EmptyMessage>> SetName(LoggedIn player, string name)
        {
            return player.Client.CallAsync<EmptyMessage>(WildCatMsgType.SetNameRequest, new StringMessage { value = name },
                WildCatMsgType.SetNameResponse, player.Ticket);
        }

        private static async Task<string> GetName(LoggedIn player)
        {
            var response = await player.Client.CallAsync<StringMessage>(WildCatMsgType.GetNameRequest, new EmptyMessage(),
                WildCatMsgType.GetNameResponse, player.Ticket);
            Assert.True(response.isSuccesful, response.errorMsg);
            return response.msg.value;
        }

        private static Task<OptionalResponse<StringMessage>> GetPlayerName(LoggedIn player, string playerId)
        {
            return player.Client.CallAsync<StringMessage>(WildCatMsgType.GetPlayerNameRequest, new StringMessage { value = playerId },
                WildCatMsgType.GetPlayerNameResponse, player.Ticket);
        }

        private static async Task SetCloudData(LoggedIn player, string key, string value)
        {
            var response = await player.Client.CallAsync<EmptyMessage>(WildCatMsgType.SetCloudDataRequest,
                new SetCloudDataRequest { key = key, value = value }, WildCatMsgType.SetCloudDataResponse, player.Ticket);
            Assert.True(response.isSuccesful, response.errorMsg);
        }

        private static async Task<string> GetCloudData(LoggedIn player, string key)
        {
            var response = await player.Client.CallAsync<StringMessage>(WildCatMsgType.GetCloudDataRequest,
                new StringMessage { value = key }, WildCatMsgType.GetCloudDataResponse, player.Ticket);
            Assert.True(response.isSuccesful, response.errorMsg);
            return response.msg.value;
        }

        private static async Task SetPublicCloudData(LoggedIn player, string key, string value)
        {
            var response = await player.Client.CallAsync<EmptyMessage>(WildCatMsgType.SetPublicCloudDataRequest,
                new SetCloudDataRequest { key = key, value = value }, WildCatMsgType.SetPublicCloudDataResponse, player.Ticket);
            Assert.True(response.isSuccesful, response.errorMsg);
        }

        private static async Task<GetPublicCloudDataResponse> GetPublicCloudData(LoggedIn player, string key)
        {
            var response = await player.Client.CallAsync<GetPublicCloudDataResponse>(WildCatMsgType.GetPublicCloudDataRequest,
                new StringMessage { value = key }, WildCatMsgType.GetPublicCloudDataResponse, player.Ticket);
            Assert.True(response.isSuccesful, response.errorMsg);
            return response.msg;
        }

        private static Task<OptionalResponse<GetPlayerPublicCloudDataResponse>> GetPlayerPublicCloudData(LoggedIn player,
            string playerId, string key)
        {
            return player.Client.CallAsync<GetPlayerPublicCloudDataResponse>(WildCatMsgType.GetPlayerPublicCloudDataRequest,
                new GetPlayerPublicCloudDataRequest { player = playerId, key = key }, WildCatMsgType.GetPlayerPublicCloudDataResponse,
                player.Ticket);
        }

        [Fact]
        public async Task SetNameAndGetNameKeepUnicodeAndEmptyNames()
        {
            var (developerKey, gameKey) = await TestSetup.CreateDeveloperAndGame(GrainFactory);
            await using var host = await GatewayTestHost.StartAsync(cluster.Client);
            var alice = await LogIn(host, developerKey, gameKey);
            await using var _ = alice.Client;

            Assert.Equal("", await GetName(alice));

            Assert.True((await SetName(alice, Unicode)).isSuccesful);
            Assert.Equal(Unicode, await GetName(alice));
            Assert.Equal(Unicode, await GrainFactory.GetGrain<IPlayer>(alice.PlayerId).GetPlayerName());

            Assert.True((await SetName(alice, "")).isSuccesful);
            Assert.Equal("", await GetName(alice));
        }

        [Fact]
        public async Task GetPlayerNameReadsAnotherPlayersName()
        {
            var (developerKey, gameKey) = await TestSetup.CreateDeveloperAndGame(GrainFactory);
            await using var host = await GatewayTestHost.StartAsync(cluster.Client);
            var alice = await LogIn(host, developerKey, gameKey);
            await using var _ = alice.Client;
            var bob = await LogIn(host, developerKey, gameKey);
            await using var __ = bob.Client;
            Assert.True((await SetName(bob, Unicode)).isSuccesful);

            var name = await GetPlayerName(alice, bob.PlayerId);
            Assert.True(name.isSuccesful, name.errorMsg);
            Assert.Equal(Unicode, name.msg.value);

            // A player nobody created has no name, as before.
            var nobody = await GetPlayerName(alice, TestSetup.UniqueId("nobody"));
            Assert.True(nobody.isSuccesful, nobody.errorMsg);
            Assert.Equal("", nobody.msg.value);

            // Orleans refuses empty grain keys; that used to reach the client as code -1.
            AssertFailed(await GetPlayerName(alice, ""), GatewayErrors.Refused, RequestedPlayer.IdRequired);
            AssertFailed(await GetPlayerName(alice, "  "), GatewayErrors.Refused, RequestedPlayer.IdRequired);
        }

        [Fact]
        public async Task PrivateCloudDataRoundTripsForThePlayerAndGameOfTheSession()
        {
            var (developerKey, gameKey) = await TestSetup.CreateDeveloperAndGame(GrainFactory);
            await using var host = await GatewayTestHost.StartAsync(cluster.Client);
            var alice = await LogIn(host, developerKey, gameKey);
            await using var _ = alice.Client;
            var bob = await LogIn(host, developerKey, gameKey);
            await using var __ = bob.Client;

            // A missing key reads as "".
            Assert.Equal("", await GetCloudData(alice, "missing"));

            await SetCloudData(alice, "level", Unicode);
            Assert.Equal(Unicode, await GetCloudData(alice, "level"));
            await SetCloudData(alice, "level", "7");
            Assert.Equal("7", await GetCloudData(alice, "level"));
            await SetCloudData(alice, "blank", "");
            Assert.Equal("", await GetCloudData(alice, "blank"));
            await SetCloudData(alice, "", "empty key");
            Assert.Equal("empty key", await GetCloudData(alice, ""));
            await SetCloudData(alice, Unicode, "unicode key");
            Assert.Equal("unicode key", await GetCloudData(alice, Unicode));

            // Stored in the player's storage of this game.
            var storage = GrainFactory.GetGrain<IPlayerStorage>(ResourcesUtility.GetPlayPlayerStorageKey(gameKey.ToString(), alice.PlayerId));
            Assert.Equal("7", (await storage.GetPlayerString("level")).Item2);

            // Private: other players neither read it as public data nor share the key.
            var peek = await GetPlayerPublicCloudData(bob, alice.PlayerId, "level");
            Assert.True(peek.isSuccesful, peek.errorMsg);
            Assert.False(peek.msg.hasValue);
            Assert.Equal("", await GetCloudData(bob, "level"));
            Assert.False((await GetPublicCloudData(alice, "level")).hasValue);
        }

        [Fact]
        public async Task PublicCloudDataIsReadByItsOwnerAndByOtherPlayers()
        {
            var (developerKey, gameKey) = await TestSetup.CreateDeveloperAndGame(GrainFactory);
            await using var host = await GatewayTestHost.StartAsync(cluster.Client);
            var alice = await LogIn(host, developerKey, gameKey);
            await using var _ = alice.Client;
            var bob = await LogIn(host, developerKey, gameKey);
            await using var __ = bob.Client;

            // A missing public value used to throw while the answer was written, so none arrived.
            var missing = await GetPublicCloudData(alice, "title");
            Assert.False(missing.hasValue);
            Assert.Equal("", missing.value);

            await SetPublicCloudData(alice, "title", Unicode);
            var own = await GetPublicCloudData(alice, "title");
            Assert.True(own.hasValue);
            Assert.Equal(Unicode, own.value);

            var seen = await GetPlayerPublicCloudData(bob, alice.PlayerId, "title");
            Assert.True(seen.isSuccesful, seen.errorMsg);
            Assert.True(seen.msg.hasValue);
            Assert.Equal(Unicode, seen.msg.value);

            // An empty value is a value.
            await SetPublicCloudData(alice, "motto", "");
            var empty = await GetPublicCloudData(alice, "motto");
            Assert.True(empty.hasValue);
            Assert.Equal("", empty.value);
            var emptySeen = await GetPlayerPublicCloudData(bob, alice.PlayerId, "motto");
            Assert.True(emptySeen.msg.hasValue);
            Assert.Equal("", emptySeen.msg.value);

            // Public values are not private ones.
            Assert.Equal("", await GetCloudData(alice, "title"));

            var noKey = await GetPlayerPublicCloudData(bob, alice.PlayerId, "missing");
            Assert.True(noKey.isSuccesful, noKey.errorMsg);
            Assert.False(noKey.msg.hasValue);
            var nobody = await GetPlayerPublicCloudData(bob, TestSetup.UniqueId("nobody"), "title");
            Assert.True(nobody.isSuccesful, nobody.errorMsg);
            Assert.False(nobody.msg.hasValue);
            AssertFailed(await GetPlayerPublicCloudData(bob, "", "title"), GatewayErrors.Refused, RequestedPlayer.IdRequired);

            var storage = GrainFactory.GetGrain<IPlayerStorage>(ResourcesUtility.GetPlayPlayerStorageKey(gameKey.ToString(), alice.PlayerId));
            Assert.Equal(Unicode, (await storage.GetPlayerPublicString("title")).Item2);
        }

        /// <summary>The group 1 requests that need a logged in player, with a payload each.</summary>
        internal static IEnumerable<(WildCatMsgType Type, MessageBase Payload)> PlayerRequests()
        {
            yield return (WildCatMsgType.GetNameRequest, new EmptyMessage());
            yield return (WildCatMsgType.SetNameRequest, new StringMessage { value = "x" });
            yield return (WildCatMsgType.GetPlayerNameRequest, new StringMessage { value = "x" });
            yield return (WildCatMsgType.GetCloudDataRequest, new StringMessage { value = "k" });
            yield return (WildCatMsgType.SetCloudDataRequest, new SetCloudDataRequest { key = "k", value = "v" });
            yield return (WildCatMsgType.SetPublicCloudDataRequest, new SetCloudDataRequest { key = "k", value = "v" });
            yield return (WildCatMsgType.GetPublicCloudDataRequest, new StringMessage { value = "k" });
            yield return (WildCatMsgType.GetPlayerPublicCloudDataRequest, new GetPlayerPublicCloudDataRequest { player = "x", key = "k" });
            yield return (WildCatMsgType.GetPublisherDataRequest, new StringMessage { value = "k" });
            yield return (WildCatMsgType.ExecuteScriptEventRequest, new ScriptEventExecutionRequestMessage { eventName = "e" });
        }

        [Fact]
        public async Task GroupOneRequestsNeedALoggedInPlayer()
        {
            var (developerKey, gameKey) = await TestSetup.CreateDeveloperAndGame(GrainFactory);
            await using var host = await GatewayTestHost.StartAsync(cluster.Client);
            await using var client = await host.ConnectAsync();
            var session = await client.InitializeSessionAsync(developerKey, gameKey);

            foreach (var (type, payload) in PlayerRequests())
            {
                var noTicket = await client.CallAsync<EmptyMessage>(type, payload, WildCatMsgType.ErrorMsg);
                AssertFailed(noTicket, GatewayErrors.Refused, GatewayErrors.SessionRequired);
                var notLoggedIn = await client.CallAsync<EmptyMessage>(type, payload, WildCatMsgType.ErrorMsg, session.ToString());
                AssertFailed(notLoggedIn, GatewayErrors.Refused, GatewayErrors.NotLoggedIn);
            }
        }

        [Fact]
        public async Task GroupOneTypesHaveTheirOwnHandlers()
        {
            await using var host = await GatewayTestHost.StartAsync(cluster.Client);
            var expected = PlayerRequests().Select(r => (r.Type, SessionRequirement.Player))
                .Append((WildCatMsgType.SetPublisherDataRequest_S, SessionRequirement.None))
                .Append((WildCatMsgType.GetPublisherDataRequest_S, SessionRequirement.None));

            foreach (var (type, session) in expected)
            {
                Assert.True(host.Handlers.TryGet(type, out var route, out var handler), $"{type} has no handler");
                Assert.NotNull(handler);
                Assert.Equal(type + 1, route.ResponseType);
                Assert.Equal(session, route.Session);
                Assert.False(route.IsSignInAttempt);
            }
        }
    }
}
