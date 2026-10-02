using Orleans;
using Orleans.TestingHost;
using WildCat.Gateway;
using WildCat.Interfaces;
using WildCat.Protocol;
using Xunit;

namespace WildCat.Tests
{
    /// <summary>
    /// A client logged in through the gateway: its socket, session ticket, account id and the
    /// device id it logged in with (to log in again after a disconnect).
    /// </summary>
    internal sealed class GatewayPlayer : IAsyncDisposable
    {
        private GatewayPlayer(GatewayTestClient client, string ticket, string id, string device)
        {
            Client = client;
            Ticket = ticket;
            Id = id;
            Device = device;
        }

        public GatewayTestClient Client { get; }
        public string Ticket { get; }
        public string Id { get; }
        public string Device { get; }

        public static async Task<GatewayPlayer> ConnectAsync(GatewayTestHost host, Guid developerKey, Guid gameKey,
            string device = null)
        {
            device ??= TestSetup.UniqueId("device");
            var client = await host.ConnectAsync();
            var session = await client.InitializeSessionAsync(developerKey, gameKey);
            var login = await client.LoginWithDeviceAsync(session, device);
            return new GatewayPlayer(client, session.ToString(), login.userId, device);
        }

        /// <summary>A request of this player: the next request id and the session ticket in the header.</summary>
        public WildCatMessageBase Request<T>(WildCatMsgType type, T payload) where T : MessageBase
        {
            return Client.Request(type, payload, Ticket);
        }

        public Task<OptionalResponse<T>> CallAsync<T>(WildCatMsgType type, MessageBase payload, WildCatMsgType responseType)
            where T : MessageBase, new()
        {
            return Client.CallAsync<T>(type, payload, responseType, Ticket);
        }

        public ValueTask DisposeAsync()
        {
            return Client.DisposeAsync();
        }
    }

    /// <summary>
    /// The match handlers over real WebSockets: FindMatch (15), LeaveGame (13), SetMatchProperty
    /// (129), GetPlayerPendingMatches (131) and RejoinMatch (133), the match events RelayServerGame
    /// pushes through the PlayerConnection stream (9, 10, 125-128), and relaying game messages
    /// between players matched through the gateway.
    /// </summary>
    [Collection(ClusterCollection.Name)]
    public class GatewayMatchHandlerTests
    {
        private const WildCatMsgType GameMessage = WildCatMsgType.HighestInternal + 21;
        private const uint Wait = 10;

        private readonly InProcessTestCluster cluster;
        private IGrainFactory GrainFactory => cluster.Client;

        public GatewayMatchHandlerTests(ClusterFixture fixture)
        {
            cluster = fixture.Cluster;
        }

        private IRelayServerGame Match(Guid gameKey, Guid matchId)
        {
            return GrainFactory.GetGrain<IRelayServerGame>(matchId, gameKey.ToString(), null);
        }

        private static WildCatMessageBase FindMatchRequest(GatewayPlayer player, string mode, uint waitTime = Wait)
        {
            return player.Request(WildCatMsgType.FindMatchRequest, new FindMatchRequest(mode, 100, waitTime));
        }

        private static async Task<OptionalResponse<FindMatchResponse>> ReplyTo(GatewayPlayer player, WildCatMessageBase request)
        {
            var reply = await player.Client.WaitForAsync(m => m.hasRequestID && m.requestID == request.requestID,
                $"the reply to {request.msgType}");
            Assert.Equal(WildCatMsgType.FindMatchResponse, reply.msgType);
            Assert.Equal(WildCatMsgHeader.HasRequestID, reply.header);
            return reply.GetPayload<OptionalResponse<FindMatchResponse>>();
        }

        /// <summary>Sends FindMatch for every player at once and returns the answers, all successful.</summary>
        private static async Task<FindMatchResponse[]> FindMatchTogether(string mode, params GatewayPlayer[] players)
        {
            var requests = players.Select(p => FindMatchRequest(p, mode)).ToArray();
            for (int i = 0; i < players.Length; ++i)
                await players[i].Client.SendAsync(requests[i]);
            var answers = new FindMatchResponse[players.Length];
            for (int i = 0; i < players.Length; ++i)
            {
                var answer = await ReplyTo(players[i], requests[i]);
                Assert.True(answer.isSuccesful, answer.errorMsg);
                answers[i] = answer.msg;
            }
            return answers;
        }

        private static WildCatMessageBase GameTargeted<T>(GatewayPlayer player, WildCatMsgType type, T payload, Guid matchId)
            where T : MessageBase
        {
            return WildCatMessageBase.CreateWithGameTarget(payload, type, WildCatMsgHeader.normal, matchId.ToString(), player.Ticket);
        }

        private static async Task<OptionalResponse<EmptyMessage>> LeaveGame(GatewayPlayer player, Guid matchId)
        {
            var leave = GameTargeted(player, WildCatMsgType.LeaveGameRequest, new EmptyMessage(), matchId);
            leave.SetRequestID(player.Client.NextRequestId());
            var reply = await player.Client.RequestAsync(leave);
            Assert.Equal(WildCatMsgType.LeaveGameResponse, reply.msgType);
            Assert.Equal(WildCatMsgHeader.HasRequestID, reply.header);
            return reply.GetPayload<OptionalResponse<EmptyMessage>>();
        }

        private static Task<OptionalResponse<EmptyMessage>> SetProperty(GatewayPlayer player, Guid matchId, bool isPlayerProperty,
            string key, byte[] value)
        {
            return player.CallAsync<EmptyMessage>(WildCatMsgType.SetMatchPropertyRequest,
                new SetMatchPropertyData { Game = matchId, isPlayerProperty = isPlayerProperty, key = key, value = value },
                WildCatMsgType.SetMatchPropertyResponse);
        }

        private static void AssertRefused<T>(OptionalResponse<T> response, string message) where T : MessageBase, new()
        {
            Assert.False(response.isSuccesful);
            Assert.Equal(GatewayErrors.Refused, response.errorCode);
            Assert.Equal(message, response.errorMsg);
        }

        private Task AddMode(Guid gameKey, string name, int players, bool finishWithOneDisconnect)
        {
            return GrainFactory.GetGrain<IGameModeManager>(gameKey).AddOrUpdateGameMode(new GameMode
            {
                Name = name,
                MinPlayers = players,
                MaxPlayers = players,
                FinishGameWithOneDisconnect = finishWithOneDisconnect,
                Criterias = new List<GameMode.MatchMakingCriteria>
                {
                    new GameMode.MatchMakingCriteria { AcceptableThreshold = 1000, StartTimeInSeconds = 0 },
                },
            });
        }

        [Fact]
        public async Task FindMatchPairsTwoPlayersInTheDefaultModeAndStartsThem()
        {
            var (developerKey, gameKey) = await TestSetup.CreateDeveloperAndGame(GrainFactory);
            await using var host = await GatewayTestHost.StartAsync(cluster.Client);
            await using var alice = await GatewayPlayer.ConnectAsync(host, developerKey, gameKey);
            await using var bob = await GatewayPlayer.ConnectAsync(host, developerKey, gameKey);
            await GrainFactory.GetGrain<IPlayer>(alice.Id).SetPlayerName("Alice");
            await GrainFactory.GetGrain<IPlayer>(bob.Id).SetPlayerName("Bob");

            // "quick" is not one of the game's modes, so the default two player rules apply.
            var answers = await FindMatchTogether("quick", alice, bob);

            Guid matchId = answers[0].gameId;
            Assert.NotEqual(Guid.Empty, matchId);
            Assert.Equal(matchId, answers[1].gameId);
            foreach (var answer in answers)
                Assert.Equal(new[] { alice.Id, bob.Id }.OrderBy(x => x), answer.players.OrderBy(x => x));

            // FindMatch joined both players, and each saw at least its own GamePlayerAddedEvent,
            // which names the player by account id like the other match events (it used to carry
            // the display name).
            var match = Match(gameKey, matchId);
            Assert.Equal(RelayServerGameStatus.AllJoined, await match.GetGameState());
            foreach (var player in new[] { alice, bob })
            {
                var added = await player.Client.WaitForAsync(m => m.msgType == WildCatMsgType.GamePlayerAddedEvent
                    && m.GetPayload<PlayerAddedToGameEvent>().Player == player.Id, $"{player.Id}'s GamePlayerAddedEvent");
                Assert.Equal(matchId, added.GetPayload<PlayerAddedToGameEvent>().Game);
                Assert.Equal(RelayServerPlayerGameStatus.InGame, await match.GetPlayerStatus(GrainFactory.GetGrain<IPlayer>(player.Id)));
            }
        }

        [Fact]
        public async Task FindMatchUsesTheModeTheGameDefines()
        {
            var (developerKey, gameKey) = await TestSetup.CreateDeveloperAndGame(GrainFactory);
            await AddMode(gameKey, "trio", players: 3, finishWithOneDisconnect: true);
            await using var host = await GatewayTestHost.StartAsync(cluster.Client);
            await using var a = await GatewayPlayer.ConnectAsync(host, developerKey, gameKey);
            await using var b = await GatewayPlayer.ConnectAsync(host, developerKey, gameKey);
            await using var c = await GatewayPlayer.ConnectAsync(host, developerKey, gameKey);

            // By the default rules two of them would be paired and the third left waiting.
            var answers = await FindMatchTogether("trio", a, b, c);

            Assert.Single(answers.Select(x => x.gameId).Distinct());
            var expected = new[] { a.Id, b.Id, c.Id }.OrderBy(x => x).ToArray();
            foreach (var answer in answers)
                Assert.Equal(expected, answer.players.OrderBy(x => x).ToArray());
            Assert.Equal(RelayServerGameStatus.AllJoined, await Match(gameKey, answers[0].gameId).GetGameState());
        }

        [Fact]
        public async Task FindMatchAnswersMatchNotFoundWhenTheWaitRunsOut()
        {
            var (developerKey, gameKey) = await TestSetup.CreateDeveloperAndGame(GrainFactory);
            await using var host = await GatewayTestHost.StartAsync(cluster.Client);
            await using var alice = await GatewayPlayer.ConnectAsync(host, developerKey, gameKey);

            var started = DateTime.UtcNow;
            var request = FindMatchRequest(alice, "lonely", waitTime: 1);
            await alice.Client.SendAsync(request);
            var answer = await ReplyTo(alice, request);

            AssertRefused(answer, FindMatchHandler.MatchNotFound);
            Assert.True(DateTime.UtcNow - started < TimeSpan.FromSeconds(10), "The wait was not kept.");

            // An empty mode name used to fail inside Orleans with an internal error.
            var noMode = FindMatchRequest(alice, "");
            await alice.Client.SendAsync(noMode);
            AssertRefused(await ReplyTo(alice, noMode), FindMatchHandler.NoGameMode);
        }

        [Fact]
        public async Task FindMatchDoesNotHoldUpOtherRequestsOrTheRelay()
        {
            var (developerKey, gameKey) = await TestSetup.CreateDeveloperAndGame(GrainFactory);
            await using var host = await GatewayTestHost.StartAsync(cluster.Client);
            await using var alice = await GatewayPlayer.ConnectAsync(host, developerKey, gameKey);
            await using var bob = await GatewayPlayer.ConnectAsync(host, developerKey, gameKey);
            var matchId = (await FindMatchTogether("duel", alice, bob))[0].gameId;

            // Alice looks for another match that nobody else wants; it waits several seconds.
            var waiting = FindMatchRequest(alice, "nobody-else", waitTime: 6);
            await alice.Client.SendAsync(waiting);

            var beat = await alice.CallAsync<EmptyMessage>(WildCatMsgType.HeartBeatRequest, new EmptyMessage(), WildCatMsgType.HeartBeatResponse);
            Assert.True(beat.isSuccesful, beat.errorMsg);
            var pending = await alice.CallAsync<PendingMatchesCollectionData>(WildCatMsgType.GetPlayerPendingMatchesRequest,
                new EmptyMessage(), WildCatMsgType.GetPlayerMatchesResponse);
            Assert.True(pending.isSuccesful, pending.errorMsg);

            await alice.Client.SendAsync(GameTargeted(alice, GameMessage, new IntegerMessage { value = 1 }, matchId));
            Assert.Equal(1, (await bob.Client.WaitForTypeAsync(GameMessage)).GetPayload<IntegerMessage>().value);
            await bob.Client.SendAsync(GameTargeted(bob, GameMessage, new IntegerMessage { value = 2 }, matchId));
            Assert.Equal(2, (await alice.Client.WaitForTypeAsync(GameMessage)).GetPayload<IntegerMessage>().value);

            // All of that was answered while FindMatch was still waiting.
            Assert.DoesNotContain(alice.Client.Received, m => m.hasRequestID && m.requestID == waiting.requestID);
            AssertRefused(await ReplyTo(alice, waiting), FindMatchHandler.MatchNotFound);
        }

        [Fact]
        public async Task GameMessagesAreRelayedBothWaysInOrderBetweenMatchedPlayers()
        {
            var (developerKey, gameKey) = await TestSetup.CreateDeveloperAndGame(GrainFactory);
            await using var host = await GatewayTestHost.StartAsync(cluster.Client);
            await using var alice = await GatewayPlayer.ConnectAsync(host, developerKey, gameKey);
            await using var bob = await GatewayPlayer.ConnectAsync(host, developerKey, gameKey);
            var matchId = (await FindMatchTogether("duel", alice, bob))[0].gameId;

            const int count = 50;
            for (int i = 0; i < count; ++i)
            {
                await alice.Client.SendAsync(GameTargeted(alice, GameMessage, new IntegerMessage { value = i }, matchId));
                await bob.Client.SendAsync(GameTargeted(bob, GameMessage, new IntegerMessage { value = 1000 + i }, matchId));
            }
            foreach (var (receiver, first) in new[] { (bob, 0), (alice, 1000) })
            {
                for (int i = 0; i < count; ++i)
                {
                    var relayed = await receiver.Client.WaitForTypeAsync(GameMessage);
                    Assert.Equal(first + i, relayed.GetPayload<IntegerMessage>().value);
                    Assert.False(relayed.hasAuthData);
                    Assert.Equal(matchId.ToString(), relayed.game);
                }
            }
        }

        [Fact]
        public async Task SetMatchPropertyPushesToThePlayersAndKeepsTheLimit()
        {
            var (developerKey, gameKey) = await TestSetup.CreateDeveloperAndGame(GrainFactory);
            await using var host = await GatewayTestHost.StartAsync(cluster.Client);
            await using var alice = await GatewayPlayer.ConnectAsync(host, developerKey, gameKey);
            await using var bob = await GatewayPlayer.ConnectAsync(host, developerKey, gameKey);
            await using var carol = await GatewayPlayer.ConnectAsync(host, developerKey, gameKey);
            var matchId = (await FindMatchTogether("duel", alice, bob))[0].gameId;

            // A match property goes to everyone in the match, the sender included.
            Assert.True((await SetProperty(alice, matchId, false, "round", new byte[] { 1 })).isSuccesful);
            foreach (var player in new[] { alice, bob })
            {
                var changed = (await player.Client.WaitForTypeAsync(WildCatMsgType.MatchPropertyChangedEvent))
                    .GetPayload<MatchPropertyChangeData>();
                Assert.Equal(matchId, changed.Game);
                Assert.True(changed.isGameProperty);
                Assert.Equal("round", changed.key);
                Assert.Equal(new byte[] { 1 }, changed.value);
            }

            // A player property goes to the others only. (It used to throw in the grain.)
            Assert.True((await SetProperty(alice, matchId, true, "hp", new byte[] { 9 })).isSuccesful);
            var playerChanged = (await bob.Client.WaitForTypeAsync(WildCatMsgType.MatchPropertyChangedEvent))
                .GetPayload<MatchPropertyChangeData>();
            Assert.False(playerChanged.isGameProperty);
            Assert.Equal(alice.Id, playerChanged.Player);
            Assert.Equal("hp", playerChanged.key);
            Assert.Equal(new byte[] { 9 }, playerChanged.value);

            // Keys and values share 10 KB. "round", "hp" and their values use 9 bytes and the key
            // "map" 3 more, so a 10227 byte value fits and 10228 bytes do not.
            var tooBig = await SetProperty(bob, matchId, false, "map", new byte[RelayServerGameState.MaxPropertyBytes - 12]);
            AssertRefused(tooBig, SetMatchPropertyHandler.TooLarge);
            Assert.True((await SetProperty(bob, matchId, false, "map", new byte[RelayServerGameState.MaxPropertyBytes - 13])).isSuccesful);
            AssertRefused(await SetProperty(bob, matchId, true, "extra", new byte[1]), SetMatchPropertyHandler.TooLarge);
            // Replacing a value counts the new size instead of the old one.
            Assert.True((await SetProperty(bob, matchId, false, "map", new byte[] { 7 })).isSuccesful);

            // Someone outside the match changes nothing.
            AssertRefused(await SetProperty(carol, matchId, false, "round", new byte[] { 5 }), RelayServerGameState.NotAPlayer);
            AssertRefused(await SetProperty(carol, matchId, true, "hp", new byte[] { 5 }), RelayServerGameState.NotAPlayer);

            // Alice saw her match property, Bob's two accepted ones, and never her own player property.
            await TestSetup.WaitUntilAsync(() => alice.Client.Received.Count(m => m.msgType == WildCatMsgType.MatchPropertyChangedEvent) >= 3,
                "Alice's property events");
            var aliceEvents = alice.Client.Received.Where(m => m.msgType == WildCatMsgType.MatchPropertyChangedEvent)
                .Select(m => m.GetPayload<MatchPropertyChangeData>()).ToList();
            Assert.Equal(new[] { "round", "map", "map" }, aliceEvents.Select(e => e.key));
            Assert.All(aliceEvents, e => Assert.True(e.isGameProperty));

            var state = await Match(gameKey, matchId).GetState();
            Assert.Equal(new byte[] { 1 }, state.GameProperties["round"]);
            Assert.Equal(new byte[] { 7 }, state.GameProperties["map"]);
            Assert.Equal(new byte[] { 9 }, state.PlayerProperties[alice.Id]["hp"]);
            Assert.False(state.PlayerProperties.ContainsKey(carol.Id));
        }

        [Fact]
        public async Task LeaveGameAnswersAndTheLastPlayerOutFinishesTheMatch()
        {
            var (developerKey, gameKey) = await TestSetup.CreateDeveloperAndGame(GrainFactory);
            await using var host = await GatewayTestHost.StartAsync(cluster.Client);
            await using var alice = await GatewayPlayer.ConnectAsync(host, developerKey, gameKey);
            await using var bob = await GatewayPlayer.ConnectAsync(host, developerKey, gameKey);
            await using var carol = await GatewayPlayer.ConnectAsync(host, developerKey, gameKey);
            var matchId = (await FindMatchTogether("duel", alice, bob))[0].gameId;
            var match = Match(gameKey, matchId);

            // Someone who is not in the match cannot finish it for the others.
            AssertRefused(await LeaveGame(carol, matchId), RelayServerGameState.NotAPlayer);
            Assert.Equal(RelayServerGameStatus.AllJoined, await match.GetGameState());

            var first = await LeaveGame(alice, matchId);
            Assert.True(first.isSuccesful, first.errorMsg);
            Assert.Equal(RelayServerGameStatus.SomeFinished, await match.GetGameState());
            var second = await LeaveGame(bob, matchId);
            Assert.True(second.isSuccesful, second.errorMsg);
            Assert.Equal(RelayServerGameStatus.AllFinished, await match.GetGameState());

            foreach (var player in new[] { alice, bob })
            {
                var finished = (await player.Client.WaitForTypeAsync(WildCatMsgType.GameFinishedEvent)).GetPayload<GameFinishedEvent>();
                Assert.Equal(matchId, finished.Game);
                Assert.False(finished.FinishedWithDisconnect);
            }
        }

        [Fact]
        public async Task LeaveGameNeedsAMatchIdAsTheGameTarget()
        {
            var (developerKey, gameKey) = await TestSetup.CreateDeveloperAndGame(GrainFactory);
            await using var host = await GatewayTestHost.StartAsync(cluster.Client);
            await using var alice = await GatewayPlayer.ConnectAsync(host, developerKey, gameKey);

            // Without a game target the legacy code sent no answer at all.
            var noTarget = await alice.CallAsync<EmptyMessage>(WildCatMsgType.LeaveGameRequest, new EmptyMessage(), WildCatMsgType.LeaveGameResponse);
            AssertRefused(noTarget, Matches.NoGameTarget);

            var notAnId = WildCatMessageBase.CreateWithGameTarget(new EmptyMessage(), WildCatMsgType.LeaveGameRequest,
                WildCatMsgHeader.normal, "not-a-match", alice.Ticket);
            notAnId.SetRequestID(alice.Client.NextRequestId());
            var reply = await alice.Client.RequestAsync(notAnId);
            AssertRefused(reply.GetPayload<OptionalResponse<EmptyMessage>>(), Matches.InvalidGameTarget);

            AssertRefused(await LeaveGame(alice, Guid.NewGuid()), RelayServerGameState.NotAPlayer);
        }

        [Fact]
        public async Task AMatchKeepsItsModeAndPropertiesWhenItIsDeactivated()
        {
            var (developerKey, gameKey) = await TestSetup.CreateDeveloperAndGame(GrainFactory);
            await AddMode(gameKey, "strict", players: 2, finishWithOneDisconnect: true);
            await using var host = await GatewayTestHost.StartAsync(cluster.Client);
            await using var alice = await GatewayPlayer.ConnectAsync(host, developerKey, gameKey);
            var bob = await GatewayPlayer.ConnectAsync(host, developerKey, gameKey);
            var matchId = (await FindMatchTogether("strict", alice, bob))[0].gameId;
            Assert.True((await SetProperty(alice, matchId, false, "round", new byte[] { 3 })).isSuccesful);
            Assert.True((await SetProperty(alice, matchId, true, "hp", new byte[] { 4 })).isSuccesful);

            // The mode and the properties used to live only in memory: after a deactivation the
            // next disconnect or finish threw, and the properties were gone.
            await cluster.Deactivate(Match(gameKey, matchId));

            bob.Client.Abort();
            await bob.DisposeAsync();
            var finished = (await alice.Client.WaitForTypeAsync(WildCatMsgType.GameFinishedEvent)).GetPayload<GameFinishedEvent>();
            Assert.Equal(matchId, finished.Game);
            Assert.True(finished.FinishedWithDisconnect);

            await cluster.Deactivate(Match(gameKey, matchId));
            var state = await Match(gameKey, matchId).GetState();
            Assert.Equal("strict", state.Mode.Name);
            Assert.Equal(RelayServerGameStatus.FinishedWithDisconnect, state.State);
            Assert.Equal(RelayServerPlayerGameStatus.Disconnected, state.Players.Single(p => p.player.GetPrimaryKeyString() == bob.Id).state);
            Assert.Equal(new byte[] { 3 }, state.GameProperties["round"]);
            Assert.Equal(new byte[] { 4 }, state.PlayerProperties[alice.Id]["hp"]);

            // The property limit still counts what was stored before the deactivations: "map" and
            // this value would fit on their own, but not with "round", "hp" and their values.
            AssertRefused(await SetProperty(alice, matchId, false, "map", new byte[RelayServerGameState.MaxPropertyBytes - 4]),
                SetMatchPropertyHandler.TooLarge);
            Assert.True((await LeaveGame(alice, matchId)).isSuccesful);
        }

        [Fact]
        public async Task ADisconnectedPlayerRejoinsFromItsPendingMatchesAndGetsTheMatchState()
        {
            var (developerKey, gameKey) = await TestSetup.CreateDeveloperAndGame(GrainFactory);
            await AddMode(gameKey, "long", players: 2, finishWithOneDisconnect: false);
            await using var host = await GatewayTestHost.StartAsync(cluster.Client);
            await using var alice = await GatewayPlayer.ConnectAsync(host, developerKey, gameKey);
            var bob = await GatewayPlayer.ConnectAsync(host, developerKey, gameKey);
            var matchId = (await FindMatchTogether("long", alice, bob))[0].gameId;

            Assert.True((await SetProperty(alice, matchId, false, "board", new byte[] { 1, 2 })).isSuccesful);
            Assert.True((await SetProperty(bob, matchId, true, "color", new byte[] { 3 })).isSuccesful);
            await alice.Client.WaitForAsync(m => m.msgType == WildCatMsgType.MatchPropertyChangedEvent
                && m.GetPayload<MatchPropertyChangeData>().key == "color", "Bob's player property");

            // Bob's app dies. The match goes on without him and tells Alice.
            bob.Client.Abort();
            await bob.DisposeAsync();
            var removed = (await alice.Client.WaitForTypeAsync(WildCatMsgType.GamePlayerRemovedEvent)).GetPayload<MatchPlayerEventData>();
            Assert.Equal(matchId, removed.Game);
            Assert.Equal(bob.Id, removed.Player);

            // He comes back on a new socket and session and finds the match among his pending ones.
            await using var bobAgain = await GatewayPlayer.ConnectAsync(host, developerKey, gameKey, bob.Device);
            Assert.Equal(bob.Id, bobAgain.Id);
            var pending = await bobAgain.CallAsync<PendingMatchesCollectionData>(WildCatMsgType.GetPlayerPendingMatchesRequest,
                new EmptyMessage(), WildCatMsgType.GetPlayerMatchesResponse);
            Assert.True(pending.isSuccesful, pending.errorMsg);
            var info = Assert.Single(pending.msg.matches);
            Assert.Equal(matchId, info.matchId);
            Assert.Equal(new[] { alice.Id, bob.Id }.OrderBy(x => x), info.players.OrderBy(x => x));
            Assert.Equal(DateTimeKind.Utc, info.startTime.Kind);
            Assert.True(DateTime.UtcNow - info.startTime < TimeSpan.FromMinutes(5), $"Start time {info.startTime:o}");

            var rejoin = await bobAgain.CallAsync<EmptyMessage>(WildCatMsgType.RejoinMatchRequest, new GuidMessage { value = matchId },
                WildCatMsgType.RejoinMatchResponse);
            Assert.True(rejoin.isSuccesful, rejoin.errorMsg);

            var sync = (await bobAgain.Client.WaitForTypeAsync(WildCatMsgType.MatchRejoinSyncEvent)).GetPayload<MatchSyncEventData>();
            Assert.Equal(matchId, sync.Game);
            Assert.Equal(new byte[] { 1, 2 }, sync.gameProperties["board"]);
            Assert.Equal(new byte[] { 3 }, sync.playerProperties[bob.Id]["color"]);
            foreach (var player in new[] { alice, bobAgain })
            {
                var rejoined = (await player.Client.WaitForTypeAsync(WildCatMsgType.GamePlayerRejoinedEvent)).GetPayload<MatchPlayerEventData>();
                Assert.Equal(matchId, rejoined.Game);
                Assert.Equal(bob.Id, rejoined.Player);
            }

            // Back in the match: relayed messages reach him again, and nothing is pending.
            await alice.Client.SendAsync(GameTargeted(alice, GameMessage, new IntegerMessage { value = 42 }, matchId));
            Assert.Equal(42, (await bobAgain.Client.WaitForTypeAsync(GameMessage)).GetPayload<IntegerMessage>().value);
            var none = await bobAgain.CallAsync<PendingMatchesCollectionData>(WildCatMsgType.GetPlayerPendingMatchesRequest,
                new EmptyMessage(), WildCatMsgType.GetPlayerMatchesResponse);
            Assert.Empty(none.msg.matches);
        }

        [Fact]
        public async Task ADisconnectInTheDefaultModeFinishesTheMatchForGood()
        {
            var (developerKey, gameKey) = await TestSetup.CreateDeveloperAndGame(GrainFactory);
            await using var host = await GatewayTestHost.StartAsync(cluster.Client);
            await using var alice = await GatewayPlayer.ConnectAsync(host, developerKey, gameKey);
            var bob = await GatewayPlayer.ConnectAsync(host, developerKey, gameKey);
            var matchId = (await FindMatchTogether("duel", alice, bob))[0].gameId;

            bob.Client.Abort();
            await bob.DisposeAsync();
            var finished = (await alice.Client.WaitForTypeAsync(WildCatMsgType.GameFinishedEvent)).GetPayload<GameFinishedEvent>();
            Assert.Equal(matchId, finished.Game);
            Assert.True(finished.FinishedWithDisconnect);

            await using var bobAgain = await GatewayPlayer.ConnectAsync(host, developerKey, gameKey, bob.Device);
            var pending = await bobAgain.CallAsync<PendingMatchesCollectionData>(WildCatMsgType.GetPlayerPendingMatchesRequest,
                new EmptyMessage(), WildCatMsgType.GetPlayerMatchesResponse);
            Assert.Empty(pending.msg.matches);

            // Rejoining a finished match used to bring it back to life.
            var rejoin = await bobAgain.CallAsync<EmptyMessage>(WildCatMsgType.RejoinMatchRequest, new GuidMessage { value = matchId },
                WildCatMsgType.RejoinMatchResponse);
            AssertRefused(rejoin, RelayServerGameState.MatchFinished);
            Assert.Equal(RelayServerGameStatus.FinishedWithDisconnect, await Match(gameKey, matchId).GetGameState());

            var stranger = await bobAgain.CallAsync<EmptyMessage>(WildCatMsgType.RejoinMatchRequest, new GuidMessage { value = Guid.NewGuid() },
                WildCatMsgType.RejoinMatchResponse);
            AssertRefused(stranger, RelayServerGameState.NotAPlayer);
        }

        [Fact]
        public async Task MatchRequestsNeedALoggedInPlayer()
        {
            var (developerKey, gameKey) = await TestSetup.CreateDeveloperAndGame(GrainFactory);
            await using var host = await GatewayTestHost.StartAsync(cluster.Client);
            await using var client = await host.ConnectAsync();
            var session = await client.InitializeSessionAsync(developerKey, gameKey);

            var requests = new (WildCatMsgType Type, MessageBase Payload)[]
            {
                (WildCatMsgType.FindMatchRequest, new FindMatchRequest("duel", 1, 1)),
                (WildCatMsgType.LeaveGameRequest, new EmptyMessage()),
                (WildCatMsgType.SetMatchPropertyRequest, new SetMatchPropertyData()),
                (WildCatMsgType.RejoinMatchRequest, new GuidMessage()),
                (WildCatMsgType.GetPlayerPendingMatchesRequest, new EmptyMessage()),
            };
            foreach (var (type, payload) in requests)
            {
                var noTicket = await client.CallAsync<EmptyMessage>(type, payload, WildCatMsgType.ErrorMsg);
                Assert.Equal(GatewayErrors.SessionRequired, noTicket.errorMsg);
                var notLoggedIn = await client.CallAsync<EmptyMessage>(type, payload, WildCatMsgType.ErrorMsg, session.ToString());
                Assert.Equal(GatewayErrors.NotLoggedIn, notLoggedIn.errorMsg);
            }
        }

        [Fact]
        public async Task EveryGroupThreeTypeHasItsOwnHandler()
        {
            await using var host = await GatewayTestHost.StartAsync(cluster.Client);
            var expected = new (WildCatMsgType Request, WildCatMsgType Response, SessionRequirement Session)[]
            {
                (WildCatMsgType.LeaveGameRequest, WildCatMsgType.LeaveGameResponse, SessionRequirement.Player),
                (WildCatMsgType.FindMatchRequest, WildCatMsgType.FindMatchResponse, SessionRequirement.Player),
                (WildCatMsgType.GetLeaderboardRequest, WildCatMsgType.GetLeaderboardResponse, SessionRequirement.Player),
                (WildCatMsgType.SetLeaderboardRequest, WildCatMsgType.SetLeaderboardResponse, SessionRequirement.None),
                (WildCatMsgType.GetInventoryRequest, WildCatMsgType.GetInventoryResponse, SessionRequirement.Player),
                (WildCatMsgType.AddItemToInventoryRequest, WildCatMsgType.AddItemToInventoryResponse, SessionRequirement.None),
                (WildCatMsgType.SetItemToInventoryRequest, WildCatMsgType.SetItemToInventoryResponse, SessionRequirement.None),
                (WildCatMsgType.RemoveInventoryItemRequest, WildCatMsgType.RemoveInventoryItemResponse, SessionRequirement.Player),
                (WildCatMsgType.UnlockInventoryRequest, WildCatMsgType.UnlockInventoryResponse, SessionRequirement.Player),
                (WildCatMsgType.GetInventoryUnlockTimeRequest, WildCatMsgType.GetInventoryUnlockTimeResponse, SessionRequirement.Player),
                (WildCatMsgType.SetMatchPropertyRequest, WildCatMsgType.SetMatchPropertyResponse, SessionRequirement.Player),
                (WildCatMsgType.GetPlayerPendingMatchesRequest, WildCatMsgType.GetPlayerMatchesResponse, SessionRequirement.Player),
                (WildCatMsgType.RejoinMatchRequest, WildCatMsgType.RejoinMatchResponse, SessionRequirement.Player),
            };
            foreach (var (request, response, session) in expected)
            {
                Assert.True(host.Handlers.TryGet(request, out var route, out var handler), $"{request} has no handler");
                Assert.NotNull(handler);
                Assert.Equal(response, route.ResponseType);
                Assert.Equal(session, route.Session);
                Assert.False(route.IsSignInAttempt);
            }
        }
    }
}
