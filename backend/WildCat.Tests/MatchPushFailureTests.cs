using System.Collections.Concurrent;
using Orleans;
using Orleans.TestingHost;
using WildCat.Interfaces;
using WildCat.Protocol;
using Xunit;

namespace WildCat.Tests
{
    /// <summary>
    /// Makes IPlayer.SendDataToConnection fail for the players in <see cref="Players"/>, the way
    /// a publish fails when the memory stream's queue is full. ClusterFixture registers it on the
    /// test silo; it does nothing for any other player.
    /// </summary>
    public sealed class FailingPushFilter : IIncomingGrainCallFilter
    {
        public static readonly ConcurrentDictionary<string, bool> Players = new();

        public Task Invoke(IIncomingGrainCallContext context)
        {
            if (context.Grain is IPlayer player
                && context.InterfaceMethod?.Name == nameof(IPlayer.SendDataToConnection)
                && Players.ContainsKey(player.GetPrimaryKeyString()))
            {
                throw new InvalidOperationException("The test made this push fail.");
            }
            return context.Invoke();
        }
    }

    /// <summary>
    /// A push that fails reaches one player only: the match change that caused it still
    /// completes, and the other players still get theirs. (A failed publish used to fail the
    /// whole call halfway: LeaveGame skipped removing the match from the player, FindMatch
    /// answered -1 after joining, and a disconnect skipped the logout.)
    /// </summary>
    [Collection(ClusterCollection.Name)]
    public class MatchPushFailureTests
    {
        private readonly InProcessTestCluster cluster;
        private IGrainFactory GrainFactory => cluster.Client;

        public MatchPushFailureTests(ClusterFixture fixture)
        {
            cluster = fixture.Cluster;
        }

        [Fact]
        public async Task LeaveGameFinishesEvenWhenAPushFails()
        {
            var match = await MatchTestSetup.StartAsync(GrainFactory);
            await using var atA = await PushRecorder.StartAsync(cluster.Client, match.A.Id);
            FailingPushFilter.Players[match.B.Id] = true;
            try
            {
                await match.Game.PlayerFinished(match.A.Player);
                // The last player out finishes the match, which pushes GameFinishedEvent to both.
                await match.Game.PlayerFinished(match.B.Player);

                Assert.Equal(RelayServerGameStatus.AllFinished, await match.Game.GetGameState());
                await atA.WaitForAsync(f => f.msgType == WildCatMsgType.GameFinishedEvent, "A's GameFinishedEvent");
                await TestSetup.WaitUntilAsync(async () => (await MatchTestSetup.RunningMatches(match.B.Player)).Count == 0,
                    "the match to leave B's running matches");
            }
            finally
            {
                FailingPushFilter.Players.TryRemove(match.B.Id, out _);
            }
        }

        [Fact]
        public async Task JoiningAMatchCompletesEvenWhenAPushFails()
        {
            var match = await MatchTestSetup.StartAsync(GrainFactory, joined: 0);
            await using var atB = await PushRecorder.StartAsync(cluster.Client, match.B.Id);
            FailingPushFilter.Players[match.A.Id] = true;
            try
            {
                // Each join pushes GamePlayerAddedEvent to the players in the match, A included.
                await match.Game.PlayerStarted(match.A.Player);
                await match.Game.PlayerStarted(match.B.Player);

                Assert.Equal(RelayServerGameStatus.AllJoined, await match.Game.GetGameState());
                var added = await atB.WaitForAsync(f => f.msgType == WildCatMsgType.GamePlayerAddedEvent, "B's GamePlayerAddedEvent");
                Assert.Equal(match.B.Id, added.GetPayload<PlayerAddedToGameEvent>().Player);
            }
            finally
            {
                FailingPushFilter.Players.TryRemove(match.A.Id, out _);
            }
        }

        [Fact]
        public async Task ADisconnectLogsOutEvenWhenAPushFails()
        {
            var match = await MatchTestSetup.StartAsync(GrainFactory);
            FailingPushFilter.Players[match.B.Id] = true;
            try
            {
                // A's disconnect ends the match and pushes GameFinishedEvent to B.
                await match.A.Disconnect();

                Assert.False(await match.A.Player.IsLoggedIn());
                Assert.Equal(RelayServerGameStatus.FinishedWithDisconnect, await match.Game.GetGameState());
                Assert.Empty(await MatchTestSetup.RunningMatches(match.A.Player));
            }
            finally
            {
                FailingPushFilter.Players.TryRemove(match.B.Id, out _);
            }
        }

        [Fact]
        public async Task ADisconnectLogsOutEvenWhenAMatchRefusesThePlayer()
        {
            var match = await MatchTestSetup.StartAsync(GrainFactory, MatchTestSetup.Mode(false));
            // A match that does not know A, as a stale entry would be: it throws "not a player".
            var stranger = GrainFactory.GetGrain<IRelayServerGame>(Guid.NewGuid(), match.GameKey.ToString(), null);
            await match.A.Player.GameStarted(stranger);

            await match.A.Disconnect();

            Assert.False(await match.A.Player.IsLoggedIn());
            Assert.Equal(RelayServerPlayerGameStatus.Disconnected, await match.Game.GetPlayerStatus(match.A.Player));
            // The stale entry is dropped, the real match is kept for a rejoin.
            Assert.Equal(new[] { match.Id }, await MatchTestSetup.RunningMatches(match.A.Player));
        }
    }
}
