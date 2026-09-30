using System.Text;
using Orleans;
using Orleans.TestingHost;
using WildCat.Interfaces;
using WildCat.Protocol;
using Xunit;

namespace WildCat.Tests
{
    /// <summary>
    /// Who may do what in a relay match: relaying, rejoining, joining and leaving once it has
    /// finished, what a disconnect does to the others, the property limit and the player
    /// property bags. Every test builds its own match.
    /// </summary>
    [Collection(ClusterCollection.Name)]
    public class RelayServerGameRuleTests
    {
        private const WildCatMsgType GameMessage = WildCatMsgType.HighestInternal + 7;

        private readonly InProcessTestCluster cluster;
        private IGrainFactory GrainFactory => cluster.Client;

        public RelayServerGameRuleTests(ClusterFixture fixture)
        {
            cluster = fixture.Cluster;
        }

        private static byte[] Relayed(int value)
        {
            return WireCodec.Encode(WildCatMessageBase.Create(new IntegerMessage { value = value }, GameMessage, WildCatMsgHeader.normal));
        }

        private static IEnumerable<int> RelayedValues(PushRecorder recorder)
        {
            return recorder.Frames.Where(f => f.msgType == GameMessage).Select(f => f.GetPayload<IntegerMessage>().value);
        }

        [Fact]
        public async Task OnlyPlayersInTheMatchCanRelayIntoIt()
        {
            // A and B play, C dropped out, D never joined.
            var match = await MatchTestSetup.StartAsync(GrainFactory, MatchTestSetup.Mode(false, players: 4), players: 4, joined: 3);
            var (a, b, c, d) = (match.Players[0], match.Players[1], match.Players[2], match.Players[3]);
            await c.Disconnect();
            Assert.Equal(RelayServerPlayerGameStatus.Disconnected, await match.Game.GetPlayerStatus(c.Player));
            Assert.Equal(RelayServerPlayerGameStatus.NotStarted, await match.Game.GetPlayerStatus(d.Player));
            await using var atB = await PushRecorder.StartAsync(cluster.Client, b.Id);
            await match.Game.SendToAllBut(a.Player, 0, Relayed(1));
            await atB.WaitForAsync(f => f.msgType == GameMessage, "the first relayed message");

            var outsider = GrainFactory.GetGrain<IPlayer>(TestSetup.UniqueId("outsider"));
            await match.Game.SendToAllBut(outsider, 0, Relayed(2));
            await match.Game.SendToAllBut(c.Player, 0, Relayed(3));
            await match.Game.SendToAllBut(d.Player, 0, Relayed(4));
            await match.Game.SendToAllBut(a.Player, 0, Relayed(5));

            // Pushes to B keep their order, so once 5 is there anything the others got through
            // would be there too.
            await atB.WaitForAsync(f => f.msgType == GameMessage && f.GetPayload<IntegerMessage>().value == 5, "the last relayed message");
            Assert.Equal(new[] { 1, 5 }, RelayedValues(atB));
        }

        [Fact]
        public async Task OnlyAPlayerWhoDroppedOutCanRejoin()
        {
            // A leaves, B plays, C drops out, D never joined. The match goes on without A.
            var match = await MatchTestSetup.StartAsync(GrainFactory, MatchTestSetup.Mode(false, players: 4), players: 4, joined: 3);
            var (a, b, c, d) = (match.Players[0], match.Players[1], match.Players[2], match.Players[3]);
            await match.Game.PlayerFinished(a.Player);
            await c.Disconnect();
            Assert.Equal(RelayServerGameStatus.SomeFinished, await match.Game.GetGameState());
            await TestSetup.WaitUntilAsync(async () => (await MatchTestSetup.RunningMatches(a.Player)).Count == 0,
                "the match to leave A's running matches");

            // Rejoining used to put A back InGame in a match that A's state no longer listed.
            foreach (var player in new[] { a, b, d })
            {
                var refused = await Assert.ThrowsAsync<GameNotRunningException>(() => match.Game.PlayerRejoined(player.Player));
                Assert.Equal(RelayServerGameState.NotDroppedOut, refused.Message);
            }
            await Assert.ThrowsAsync<GameNotRunningException>(() => a.Player.RejoinMatch(match.Game));
            Assert.Equal(RelayServerPlayerGameStatus.Finished, await match.Game.GetPlayerStatus(a.Player));
            Assert.Equal(RelayServerPlayerGameStatus.InGame, await match.Game.GetPlayerStatus(b.Player));
            Assert.Equal(RelayServerPlayerGameStatus.NotStarted, await match.Game.GetPlayerStatus(d.Player));
            Assert.Empty(await MatchTestSetup.RunningMatches(a.Player));

            // A player who left cannot come back through PlayerStarted either.
            var restart = await Assert.ThrowsAsync<GameNotRunningException>(() => match.Game.PlayerStarted(a.Player));
            Assert.Equal(RelayServerGameState.LeftMatch, restart.Message);
            Assert.Equal(RelayServerPlayerGameStatus.Finished, await match.Game.GetPlayerStatus(a.Player));

            // C, who dropped out, can.
            await match.Game.PlayerRejoined(c.Player);
            Assert.Equal(RelayServerPlayerGameStatus.InGame, await match.Game.GetPlayerStatus(c.Player));
            Assert.Equal(RelayServerGameStatus.SomeFinished, await match.Game.GetGameState());
        }

        [Fact]
        public async Task ADisconnectThatEndsTheMatchFinishesTheOtherPlayers()
        {
            // A and B play, C never joined; the mode ends the match on the first disconnect.
            var match = await MatchTestSetup.StartAsync(GrainFactory, MatchTestSetup.Mode(true, players: 3), players: 3, joined: 2);
            var (a, b, c) = (match.Players[0], match.Players[1], match.Players[2]);
            await using var atB = await PushRecorder.StartAsync(cluster.Client, b.Id);

            await a.Disconnect();

            // A Select with side effects that nobody enumerated left B and C as they were.
            Assert.Equal(RelayServerGameStatus.FinishedWithDisconnect, await match.Game.GetGameState());
            Assert.Equal(RelayServerPlayerGameStatus.Disconnected, await match.Game.GetPlayerStatus(a.Player));
            Assert.Equal(RelayServerPlayerGameStatus.Finished, await match.Game.GetPlayerStatus(b.Player));
            Assert.Equal(RelayServerPlayerGameStatus.Finished, await match.Game.GetPlayerStatus(c.Player));
            var finished = (await atB.WaitForAsync(f => f.msgType == WildCatMsgType.GameFinishedEvent, "B's GameFinishedEvent"))
                .GetPayload<GameFinishedEvent>();
            Assert.Equal(match.Id, finished.Game);
            Assert.True(finished.FinishedWithDisconnect);

            // B's own disconnect afterwards changes nothing, and B is done with the match.
            await b.Disconnect();
            Assert.Equal(RelayServerPlayerGameStatus.Finished, await match.Game.GetPlayerStatus(b.Player));
            Assert.Equal(RelayServerGameStatus.FinishedWithDisconnect, await match.Game.GetGameState());
            Assert.Empty(await MatchTestSetup.RunningMatches(b.Player));
        }

        [Fact]
        public async Task AFinishedMatchStaysFinished()
        {
            // Everyone left.
            var left = await MatchTestSetup.StartAsync(GrainFactory);
            await left.Game.PlayerFinished(left.A.Player);
            await left.Game.PlayerFinished(left.B.Player);
            Assert.Equal(RelayServerGameStatus.AllFinished, await left.Game.GetGameState());

            var started = await Assert.ThrowsAsync<GameNotRunningException>(() => left.Game.PlayerStarted(left.A.Player));
            Assert.Equal(RelayServerGameState.MatchFinished, started.Message);
            Assert.Equal(RelayServerGameStatus.AllFinished, await left.Game.GetGameState());
            Assert.Equal(RelayServerPlayerGameStatus.Finished, await left.Game.GetPlayerStatus(left.A.Player));
            Assert.True(await left.Game.PlayerDisconnected(left.A.Player));
            Assert.Equal(RelayServerGameStatus.AllFinished, await left.Game.GetGameState());
            Assert.Equal(RelayServerPlayerGameStatus.Finished, await left.Game.GetPlayerStatus(left.A.Player));

            // Ended by A's disconnect before B joined (PlayerStarted used to bring it back to
            // SomeJoined, and a later LeaveGame turned it into AllFinished).
            var ended = await MatchTestSetup.StartAsync(GrainFactory, joined: 1);
            await ended.A.Disconnect();
            Assert.Equal(RelayServerGameStatus.FinishedWithDisconnect, await ended.Game.GetGameState());

            var joining = await Assert.ThrowsAsync<GameNotRunningException>(() => ended.Game.PlayerStarted(ended.B.Player));
            Assert.Equal(RelayServerGameState.MatchFinished, joining.Message);
            await ended.Game.PlayerFinished(ended.B.Player);
            await ended.B.Disconnect();
            Assert.Equal(RelayServerGameStatus.FinishedWithDisconnect, await ended.Game.GetGameState());
            Assert.Equal(RelayServerPlayerGameStatus.Finished, await ended.Game.GetPlayerStatus(ended.B.Player));
            Assert.Equal(RelayServerPlayerGameStatus.Disconnected, await ended.Game.GetPlayerStatus(ended.A.Player));
            await TestSetup.WaitUntilAsync(async () => (await MatchTestSetup.RunningMatches(ended.B.Player)).Count == 0,
                "the match to leave B's running matches");
        }

        [Fact]
        public async Task JoiningTellsThePlayersTheAccountIdOfTheNewPlayer()
        {
            var match = await MatchTestSetup.StartAsync(GrainFactory, joined: 1);
            await match.B.Player.SetPlayerName("Bob");
            await using var atA = await PushRecorder.StartAsync(cluster.Client, match.A.Id);

            await match.Game.PlayerStarted(match.B.Player);

            // Every other match event names players by account id; this one used to carry the
            // display name. (A's own event from joining may arrive on A's stream too.)
            var added = (await atA.WaitForAsync(f => f.msgType == WildCatMsgType.GamePlayerAddedEvent
                && f.GetPayload<PlayerAddedToGameEvent>().Player != match.A.Id, "GamePlayerAddedEvent for B"))
                .GetPayload<PlayerAddedToGameEvent>();
            Assert.Equal(match.Id, added.Game);
            Assert.Equal(match.B.Id, added.Player);
        }

        [Fact]
        public async Task EveryPlayersPropertiesAreKept()
        {
            var match = await MatchTestSetup.StartAsync(GrainFactory, MatchTestSetup.Mode(false));
            var (a, b) = (match.A, match.B);
            Assert.True(await match.Game.SetPlayerProperty(a.Player, "hp", new byte[] { 1 }));
            Assert.True(await match.Game.SetPlayerProperty(b.Player, "hp", new byte[] { 2 }));
            Assert.True(await match.Game.SetPlayerProperty(b.Player, "color", new byte[] { 3 }));
            Assert.True(await match.Game.SetPlayerProperty(a.Player, "hp", new byte[] { 4 }));
            Assert.True(await match.Game.SetGameProperty(a.Player, "round", new byte[] { 5 }));

            void Check(Dictionary<string, byte[]> game, Dictionary<string, Dictionary<string, byte[]>> players)
            {
                Assert.Equal(new byte[] { 5 }, Assert.Single(game).Value);
                Assert.Equal(2, players.Count);
                Assert.Equal(new byte[] { 4 }, Assert.Single(players[a.Id]).Value);
                Assert.Equal(2, players[b.Id].Count);
                Assert.Equal(new byte[] { 2 }, players[b.Id]["hp"]);
                Assert.Equal(new byte[] { 3 }, players[b.Id]["color"]);
            }

            var state = await match.Game.GetState();
            Check(state.GameProperties, state.PlayerProperties);
            await cluster.Deactivate(match.Game);
            state = await match.Game.GetState();
            Check(state.GameProperties, state.PlayerProperties);

            // A player who drops out and rejoins gets both bags.
            await b.Disconnect();
            await using var atB = await PushRecorder.StartAsync(cluster.Client, b.Id);
            await match.Game.PlayerRejoined(b.Player);
            var sync = (await atB.WaitForAsync(f => f.msgType == WildCatMsgType.MatchRejoinSyncEvent, "MatchRejoinSyncEvent"))
                .GetPayload<MatchSyncEventData>();
            Check(sync.gameProperties, sync.playerProperties);
        }

        [Fact]
        public async Task MatchPropertyKeysCountAgainstTheLimit()
        {
            var match = await MatchTestSetup.StartAsync(GrainFactory);
            var (a, b) = (match.A, match.B);

            // The review's probe stored 50 properties with 1 MB keys and empty values; each one
            // was written to storage and pushed to every player.
            for (int i = 0; i < 5; ++i)
                Assert.False(await match.Game.SetGameProperty(a.Player, i + new string('k', 1_000_000), Array.Empty<byte>()));
            Assert.False(await match.Game.SetPlayerProperty(a.Player, new string('k', 1_000_000), Array.Empty<byte>()));
            var state = await match.Game.GetState();
            Assert.Empty(state.GameProperties);
            Assert.Empty(state.PlayerProperties);

            // Keys are limited in length, and their bytes count with the values.
            string longest = new string('k', RelayServerGameState.MaxPropertyKeyLength);
            Assert.False(await match.Game.SetGameProperty(a.Player, longest + "k", Array.Empty<byte>()));
            Assert.False(await match.Game.SetPlayerProperty(a.Player, longest + "k", Array.Empty<byte>()));
            Assert.False(await match.Game.SetGameProperty(a.Player, null, Array.Empty<byte>()));
            Assert.True(await match.Game.SetGameProperty(a.Player, longest, Array.Empty<byte>()));
            Assert.True(await match.Game.SetPlayerProperty(b.Player, "\u00e9", new byte[10]));

            // Used: 128 + 2 (the key is two bytes in UTF-8) + 10 = 140 bytes. "map" takes 3 more.
            int room = RelayServerGameState.MaxPropertyBytes - 140 - 3 - 1;
            Assert.False(await match.Game.SetGameProperty(b.Player, "map", new byte[room + 1]));
            Assert.False(await match.Game.SetPlayerProperty(a.Player, "map", new byte[room + 1]));
            Assert.True(await match.Game.SetPlayerProperty(a.Player, "map", new byte[room]));
            Assert.False(await match.Game.SetGameProperty(b.Player, "x", Array.Empty<byte>()));

            // Replacing a property counts its key once, with the new value.
            Assert.True(await match.Game.SetPlayerProperty(a.Player, "map", Array.Empty<byte>()));
            Assert.True(await match.Game.SetGameProperty(b.Player, "x", Array.Empty<byte>()));
            state = await match.Game.GetState();
            int stored = state.GameProperties.Sum(p => Encoding.UTF8.GetByteCount(p.Key) + p.Value.Length)
                + state.PlayerProperties.Values.SelectMany(bag => bag).Sum(p => Encoding.UTF8.GetByteCount(p.Key) + p.Value.Length);
            Assert.Equal(128 + 2 + 10 + 3 + 1, stored);
        }
    }
}
