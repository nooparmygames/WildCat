using Orleans;
using Orleans.TestingHost;
using WildCat.Gateway;
using WildCat.Interfaces;
using WildCat.Protocol;
using Xunit;
using Status = WildCat.Protocol.ChallengeRequestsMessage.Request.Status;

namespace WildCat.Tests
{
    /// <summary>
    /// The challenge handlers over real WebSockets, with two or three connected players:
    /// StartChallenge (17), AcceptChallenge (20), DenyChallenge (23), GetPlayerChallenges (161),
    /// and the challenge events pushed from the ChallengeEvents stream (19, 22, 25).
    /// </summary>
    [Collection(ClusterCollection.Name)]
    public class GatewayChallengeHandlerTests
    {
        private readonly InProcessTestCluster cluster;

        public GatewayChallengeHandlerTests(ClusterFixture fixture)
        {
            cluster = fixture.Cluster;
        }

        private static readonly WildCatMsgType[] ChallengeRequests =
        {
            WildCatMsgType.StartChallengeRequest, WildCatMsgType.AcceptChallengeRequest,
            WildCatMsgType.DenyChallengeRequest, WildCatMsgType.GetPlayerChallengesRequest,
        };

        private static Task<OptionalResponse<EmptyMessage>> Challenge(SocialPlayer from, string playerId, byte[] data = null, uint timeout = 30)
        {
            return from.CallAsync<EmptyMessage>(WildCatMsgType.StartChallengeRequest,
                new ChallengeMessage { Player = playerId, data = data ?? new byte[0], TimeOut = timeout }, WildCatMsgType.StartChallengeResponse);
        }

        private static Task<OptionalResponse<EmptyMessage>> Accept(SocialPlayer player, string challengerId, byte[] data = null)
        {
            return player.CallAsync<EmptyMessage>(WildCatMsgType.AcceptChallengeRequest,
                new ChallengeMessage { Player = challengerId, data = data ?? new byte[0] }, WildCatMsgType.AcceptChallengeResponse);
        }

        private static Task<OptionalResponse<EmptyMessage>> Deny(SocialPlayer player, string challengerId, byte[] data = null)
        {
            return player.CallAsync<EmptyMessage>(WildCatMsgType.DenyChallengeRequest,
                new ChallengeMessage { Player = challengerId, data = data ?? new byte[0] }, WildCatMsgType.DenyChallengeResponse);
        }

        private static async Task<ChallengeRequestsMessage> GetChallenges(SocialPlayer player)
        {
            var response = await player.CallAsync<ChallengeRequestsMessage>(WildCatMsgType.GetPlayerChallengesRequest,
                new EmptyMessage(), WildCatMsgType.GetPlayerChallengesResponse);
            Assert.True(response.isSuccesful, response.errorMsg);
            return response.msg;
        }

        /// <summary>Waits until the player's challenge list satisfies <paramref name="condition"/> (some updates are not awaited by the grains).</summary>
        private static async Task<ChallengeRequestsMessage> WaitForChallenges(SocialPlayer player, Func<ChallengeRequestsMessage, bool> condition,
            string description)
        {
            ChallengeRequestsMessage last = null;
            await TestSetup.WaitUntilAsync(async () => condition(last = await GetChallenges(player)), description);
            return last;
        }

        private static void AssertSucceeded<T>(OptionalResponse<T> response) where T : MessageBase, new()
        {
            Assert.True(response.isSuccesful, response.errorMsg);
        }

        private static void AssertRefused<T>(OptionalResponse<T> response, int code, string message) where T : MessageBase, new()
        {
            Assert.False(response.isSuccesful);
            Assert.Equal(code, response.errorCode);
            Assert.Equal(message, response.errorMsg);
        }

        [Fact]
        public async Task ChallengeMessagesHaveTheirOwnHandlers()
        {
            await using var host = await GatewayTestHost.StartAsync(cluster.Client);
            foreach (var type in ChallengeRequests)
            {
                Assert.True(host.Handlers.TryGet(type, out var route, out var handler), $"{type} has no handler");
                Assert.NotNull(handler);
                Assert.Equal(type + 1, route.ResponseType);
                Assert.Equal(SessionRequirement.Player, route.Session);
                Assert.False(route.IsSignInAttempt);
            }
        }

        [Fact]
        public async Task ChallengeMessagesNeedALoggedInPlayer()
        {
            await using var game = await GatewaySocialGame.StartAsync(cluster.Client);
            await using var client = await game.Host.ConnectAsync();
            var session = await client.InitializeSessionAsync(game.DeveloperKey, game.GameKey);

            foreach (var type in ChallengeRequests)
            {
                var noTicket = await client.CallAsync<EmptyMessage>(type, new EmptyMessage(), WildCatMsgType.ErrorMsg);
                AssertRefused(noTicket, GatewayErrors.Refused, GatewayErrors.SessionRequired);
                var noLogin = await client.CallAsync<EmptyMessage>(type, new EmptyMessage(), WildCatMsgType.ErrorMsg, session.ToString());
                AssertRefused(noLogin, GatewayErrors.Refused, GatewayErrors.NotLoggedIn);
            }
        }

        [Fact]
        public async Task AcceptingStartsAMatchAndPushesItsIdToBothPlayers()
        {
            await using var game = await GatewaySocialGame.StartAsync(cluster.Client);
            var alice = await game.JoinAsync("Alice");
            var bob = await game.JoinAsync("Bob");

            AssertSucceeded(await Challenge(alice, bob.Id, new byte[] { 1 }));
            var requested = await bob.WaitForEventAsync<ChallengeMessage>(WildCatMsgType.StartChallengeRequestedEvent);
            Assert.Equal(alice.Id, requested.Player);
            Assert.Equal("Alice", requested.PlayerName);
            Assert.Equal(new byte[] { 1 }, requested.data);
            Assert.Equal(Guid.Empty, requested.GameKey);

            var pending = await GetChallenges(alice);
            var sent = Assert.Single(pending.sent);
            Assert.Equal((bob.Id, "Bob", Status.Pending, Guid.Empty), (sent.Player, sent.PlayerName, sent.status, sent.gameId));
            Assert.Equal(new byte[] { 1 }, sent.data);
            Assert.Empty(pending.received);

            AssertSucceeded(await Accept(bob, alice.Id, new byte[] { 2 }));
            var toAlice = await alice.WaitForEventAsync<ChallengeMessage>(WildCatMsgType.ChallengeAcceptedEvent);
            var toBob = await bob.WaitForEventAsync<ChallengeMessage>(WildCatMsgType.ChallengeAcceptedEvent);
            Assert.Equal((bob.Id, "Bob"), (toAlice.Player, toAlice.PlayerName));
            Assert.Equal((alice.Id, "Alice"), (toBob.Player, toBob.PlayerName));
            Assert.Equal(new byte[] { 2 }, toAlice.data);
            Assert.Equal(new byte[] { 2 }, toBob.data);
            Guid matchId = toAlice.GameKey;
            Assert.NotEqual(Guid.Empty, matchId);
            Assert.Equal(matchId, toBob.GameKey);

            var match = game.Grains.GetGrain<IRelayServerGame>(matchId, game.GameKey.ToString(), null);
            Assert.True(await match.IsValidGame());
            Assert.Equal(new[] { alice.Id, bob.Id }.OrderBy(x => x),
                (await match.GetPlayers()).Select(p => p.GetPrimaryKeyString()).OrderBy(x => x));

            sent = Assert.Single((await GetChallenges(alice)).sent);
            Assert.Equal((bob.Id, Status.Started, matchId), (sent.Player, sent.status, sent.gameId));
            var bobs = await WaitForChallenges(bob, c => c.received.Count == 1 && c.received[0].status == Status.Started,
                "Bob's challenge to be marked started");
            Assert.Equal((alice.Id, "Alice", matchId), (bobs.received[0].Player, bobs.received[0].PlayerName, bobs.received[0].gameId));
            Assert.Empty(bobs.sent);

            // A started challenge cannot be accepted again (that used to start a second match).
            AssertRefused(await Accept(bob, alice.Id), GatewayErrors.Refused, ChallengeNotFoundException.DefaultMessage);
            Assert.Equal(1, await alice.CountAfterQuietPeriodAsync(WildCatMsgType.ChallengeAcceptedEvent));
            Assert.Equal(1, bob.Client.Received.Count(m => m.msgType == WildCatMsgType.ChallengeAcceptedEvent));

            // Once it started, either player may challenge the other again.
            AssertSucceeded(await Challenge(bob, alice.Id));
            await alice.WaitForEventAsync<ChallengeMessage>(WildCatMsgType.StartChallengeRequestedEvent);
            game.AssertNothingWentWrong();
        }

        [Fact]
        public async Task DenyingTellsTheChallengerOnce()
        {
            await using var game = await GatewaySocialGame.StartAsync(cluster.Client);
            var alice = await game.JoinAsync("Alice");
            var bob = await game.JoinAsync("Bob");
            var carol = await game.JoinAsync("Carol");

            AssertSucceeded(await Challenge(alice, bob.Id));
            await bob.WaitForEventAsync<ChallengeMessage>(WildCatMsgType.StartChallengeRequestedEvent);

            // Carol has no challenge from Alice: her denial used to reach Alice all the same.
            AssertSucceeded(await Deny(carol, alice.Id, new byte[] { 4 }));

            AssertSucceeded(await Deny(bob, alice.Id, new byte[] { 3 }));
            var denied = await alice.WaitForEventAsync<ChallengeMessage>(WildCatMsgType.ChallengeDeniedEvent);
            Assert.Equal((bob.Id, "Bob", Guid.Empty), (denied.Player, denied.PlayerName, denied.GameKey));
            Assert.Equal(new byte[] { 3 }, denied.data);

            Assert.Empty((await GetChallenges(alice)).sent);
            await WaitForChallenges(bob, c => c.received.Count == 0, "Bob's received challenge to go");

            AssertSucceeded(await Deny(bob, alice.Id));
            AssertRefused(await Accept(bob, alice.Id), GatewayErrors.Refused, ChallengeNotFoundException.DefaultMessage);
            AssertRefused(await Deny(bob, ""), GatewayErrors.Refused, ChallengeNotFoundException.DefaultMessage);
            Assert.Equal(1, await alice.CountAfterQuietPeriodAsync(WildCatMsgType.ChallengeDeniedEvent));
            Assert.DoesNotContain(bob.Client.Received, m => m.msgType == WildCatMsgType.ChallengeDeniedEvent);
            Assert.DoesNotContain(alice.Client.Received, m => m.msgType == WildCatMsgType.ChallengeAcceptedEvent);
            game.AssertNothingWentWrong();
        }

        [Fact]
        public async Task AcceptingAfterTheTimeoutIsRefused()
        {
            await using var game = await GatewaySocialGame.StartAsync(cluster.Client);
            var alice = await game.JoinAsync("Alice");
            var bob = await game.JoinAsync("Bob");

            AssertSucceeded(await Challenge(alice, bob.Id, timeout: 1));
            await bob.WaitForEventAsync<ChallengeMessage>(WildCatMsgType.StartChallengeRequestedEvent);
            await Task.Delay(TimeSpan.FromSeconds(1.2));

            AssertRefused(await Accept(bob, alice.Id), GatewayErrors.Refused, ChallengeTimeoutException.DefaultMessage);
            Assert.Empty((await GetChallenges(alice)).sent);
            await WaitForChallenges(bob, c => c.received.Count == 0, "Bob's expired challenge to go");
            AssertRefused(await Accept(bob, alice.Id), GatewayErrors.Refused, ChallengeNotFoundException.DefaultMessage);
            Assert.Equal(0, await alice.CountAfterQuietPeriodAsync(WildCatMsgType.ChallengeAcceptedEvent));
            Assert.DoesNotContain(bob.Client.Received, m => m.msgType == WildCatMsgType.ChallengeAcceptedEvent);

            // An expired challenge does not stop the challengee from challenging back.
            AssertSucceeded(await Challenge(bob, alice.Id));
            await alice.WaitForEventAsync<ChallengeMessage>(WildCatMsgType.StartChallengeRequestedEvent);
            game.AssertNothingWentWrong();
        }

        [Fact]
        public async Task AnOfflinePlayerCanBeChallengedAndAcceptLater()
        {
            await using var game = await GatewaySocialGame.StartAsync(cluster.Client);
            var alice = await game.JoinAsync("Alice");
            var bob = await game.CreateAsync("Bob");

            AssertSucceeded(await Challenge(alice, bob.Id, new byte[] { 5 }));

            // The challenge survives both grains being deactivated (it was never written before).
            await cluster.Deactivate(game.Grains.GetGrain<IPlayerChallengeHandler>(game.GameKey, alice.Id, null));
            await cluster.Deactivate(game.Grains.GetGrain<IPlayerChallengeHandler>(game.GameKey, bob.Id, null));

            await game.ConnectAsync(bob);
            var bobs = await GetChallenges(bob);
            Assert.Empty(bobs.sent);
            var received = Assert.Single(bobs.received);
            Assert.Equal((alice.Id, "Alice", Status.Pending), (received.Player, received.PlayerName, received.status));
            Assert.Equal(new byte[] { 5 }, received.data);

            AssertSucceeded(await Accept(bob, alice.Id));
            var toAlice = await alice.WaitForEventAsync<ChallengeMessage>(WildCatMsgType.ChallengeAcceptedEvent);
            var toBob = await bob.WaitForEventAsync<ChallengeMessage>(WildCatMsgType.ChallengeAcceptedEvent);
            Assert.NotEqual(Guid.Empty, toAlice.GameKey);
            Assert.Equal(toAlice.GameKey, toBob.GameKey);
            // (The request event is not checked: a memory stream delivers an event it had not
            // pulled yet to a subscription made after it was published, so under load it can
            // still reach Bob just after he logs in.)
            game.AssertNothingWentWrong();
        }

        [Fact]
        public async Task GetPlayerChallengesListsSentAndReceivedApart()
        {
            await using var game = await GatewaySocialGame.StartAsync(cluster.Client);
            var alice = await game.JoinAsync("Alice");
            var bob = await game.JoinAsync("Bob");
            var carol = await game.JoinAsync("Carol");

            AssertSucceeded(await Challenge(alice, bob.Id, new byte[] { 1 }));
            AssertSucceeded(await Challenge(carol, alice.Id, new byte[] { 2 }));
            // A new challenge to the same player replaces the pending one.
            AssertSucceeded(await Challenge(alice, bob.Id, new byte[] { 3 }));

            // The received ones used to be listed under sent (docs/legacy-backend-notes.md bug 21).
            var alices = await GetChallenges(alice);
            var sent = Assert.Single(alices.sent);
            Assert.Equal((bob.Id, "Bob", Status.Pending), (sent.Player, sent.PlayerName, sent.status));
            Assert.Equal(new byte[] { 3 }, sent.data);
            var received = Assert.Single(alices.received);
            Assert.Equal((carol.Id, "Carol", Status.Pending), (received.Player, received.PlayerName, received.status));
            Assert.Equal(new byte[] { 2 }, received.data);

            var bobs = await GetChallenges(bob);
            Assert.Empty(bobs.sent);
            received = Assert.Single(bobs.received);
            Assert.Equal(alice.Id, received.Player);
            Assert.Equal(new byte[] { 3 }, received.data);
            game.AssertNothingWentWrong();
        }

        [Fact]
        public async Task StartChallengeRefusesUnknownPlayersYourselfAndCrossedChallenges()
        {
            await using var game = await GatewaySocialGame.StartAsync(cluster.Client);
            var alice = await game.JoinAsync("Alice");
            var bob = await game.JoinAsync("Bob");

            AssertRefused(await Challenge(alice, TestSetup.UniqueId("nobody")), GatewayErrors.Refused, "Player not found.");
            AssertRefused(await Challenge(alice, ""), GatewayErrors.Refused, "Player not found.");
            AssertRefused(await Challenge(alice, alice.Id), GatewayErrors.Refused, "You cannot challenge yourself.");

            AssertSucceeded(await Challenge(bob, alice.Id));
            await alice.WaitForEventAsync<ChallengeMessage>(WildCatMsgType.StartChallengeRequestedEvent);
            AssertRefused(await Challenge(alice, bob.Id), GatewayErrors.AlreadyExists,
                "That player has challenged you already. Accept their challenge instead.");
            Assert.Empty((await GetChallenges(alice)).sent);
            Assert.Equal(0, await bob.CountAfterQuietPeriodAsync(WildCatMsgType.StartChallengeRequestedEvent));

            AssertRefused(await Accept(alice, ""), GatewayErrors.Refused, ChallengeNotFoundException.DefaultMessage);
            AssertRefused(await Accept(alice, TestSetup.UniqueId("nobody")), GatewayErrors.Refused, ChallengeNotFoundException.DefaultMessage);
            game.AssertNothingWentWrong();
        }

        [Fact]
        public async Task AClosedSocketGivesUpItsChallengeEvents()
        {
            await using var game = await GatewaySocialGame.StartAsync(cluster.Client);
            var alice = await game.JoinAsync("Alice");
            var bob = await game.JoinAsync("Bob");

            await game.DisconnectAsync(bob);
            Assert.Equal(0, await game.ChallengeSubscriptionsAsync(bob.Id));

            // Nothing on this node listens for Bob any more, nothing fails, and the challenge waits for him.
            AssertSucceeded(await Challenge(alice, bob.Id));
            await Task.Delay(SocialPlayer.QuietPeriod);
            game.AssertNothingWentWrong();

            await game.ConnectAsync(bob);
            Assert.Equal(1, await game.ChallengeSubscriptionsAsync(bob.Id));
            Assert.Single((await GetChallenges(bob)).received);
            AssertSucceeded(await Deny(bob, alice.Id));
            await alice.WaitForEventAsync<ChallengeMessage>(WildCatMsgType.ChallengeDeniedEvent);
            game.AssertNothingWentWrong();
        }
    }
}
