using Orleans;
using Orleans.TestingHost;
using WildCat.Interfaces;
using Xunit;

namespace WildCat.Tests
{
    /// <summary>
    /// Call cycles between grains that two independent requests close. CallChainReentrancyFilter
    /// only lets a call back into a grain that is waiting on the same call chain, so these
    /// deadlocked until Orleans' 30 second response timeout; each test gives up after
    /// <see cref="MatchTestSetup.DeadlockLimit"/>.
    /// </summary>
    [Collection(ClusterCollection.Name)]
    public class CallCycleTests
    {
        private readonly InProcessTestCluster cluster;
        private IGrainFactory GrainFactory => cluster.Client;

        public CallCycleTests(ClusterFixture fixture)
        {
            cluster = fixture.Cluster;
        }

        [Theory]
        [InlineData(true)]
        [InlineData(false)]
        public async Task BothPlayersOfAMatchDisconnectingAtOnceDoNotDeadlock(bool finishWithOneDisconnect)
        {
            for (int round = 0; round < 3; ++round)
            {
                var match = await MatchTestSetup.StartAsync(GrainFactory, MatchTestSetup.Mode(finishWithOneDisconnect));

                // Each Disconnected holds its player while it waits for the match, and the match
                // pushes the other player's event to that other, busy player.
                var both = Task.WhenAll(match.A.Disconnect(), match.B.Disconnect());

                Assert.True(await MatchTestSetup.FinishInTime(both),
                    "Player.Disconnected of both players did not finish (Player -> RelayServerGame -> the other Player).");
                Assert.False(await match.A.Player.IsLoggedIn());
                Assert.False(await match.B.Player.IsLoggedIn());
                var statuses = new[]
                {
                    await match.Game.GetPlayerStatus(match.A.Player),
                    await match.Game.GetPlayerStatus(match.B.Player),
                };
                if (finishWithOneDisconnect)
                {
                    // Whoever came first ended the match; the other was finished by it.
                    Assert.Equal(RelayServerGameStatus.FinishedWithDisconnect, await match.Game.GetGameState());
                    Assert.Contains(RelayServerPlayerGameStatus.Disconnected, statuses);
                    Assert.Contains(RelayServerPlayerGameStatus.Finished, statuses);
                    Assert.Empty(await MatchTestSetup.RunningMatches(match.A.Player));
                    Assert.Empty(await MatchTestSetup.RunningMatches(match.B.Player));
                }
                else
                {
                    Assert.All(statuses, s => Assert.Equal(RelayServerPlayerGameStatus.Disconnected, s));
                    Assert.Equal(new[] { match.Id }, await MatchTestSetup.RunningMatches(match.A.Player));
                    Assert.Equal(new[] { match.Id }, await MatchTestSetup.RunningMatches(match.B.Player));
                }
            }
        }

        [Fact]
        public async Task PendingMatchesWhileTheOpponentRelaysDoesNotDeadlock()
        {
            var match = await MatchTestSetup.StartAsync(GrainFactory);

            // SendToAllBut holds the match while it pushes to B; GetPendingMatches holds B while
            // it reads the match.
            var relay = MatchTestSetup.RepeatFor(TimeSpan.FromSeconds(3),
                i => match.Game.SendToAllBut(match.A.Player, 0, new byte[] { (byte)i }));
            var pending = MatchTestSetup.RepeatFor(TimeSpan.FromSeconds(3),
                _ => match.B.Player.GetPendingMatches());

            Assert.True(await MatchTestSetup.FinishInTime(Task.WhenAll(relay, pending)),
                "GetPendingMatches (Player -> RelayServerGame.GetState) and SendToAllBut (RelayServerGame -> Player.SendDataToConnection) deadlocked.");
        }

        [Fact]
        public async Task LeaveGameAndCloseTheSocketAtOnceDoesNotDeadlock()
        {
            foreach (bool finishWithOneDisconnect in new[] { true, false })
            {
                for (int round = 0; round < 3; ++round)
                {
                    var match = await MatchTestSetup.StartAsync(GrainFactory, MatchTestSetup.Mode(finishWithOneDisconnect));

                    // LeaveGame holds the match while it tells A the match is over for A; the closed
                    // socket's Disconnected holds A while it tells the match.
                    var both = Task.WhenAll(match.Game.PlayerFinished(match.A.Player), match.A.Disconnect());

                    Assert.True(await MatchTestSetup.FinishInTime(both),
                        "PlayerFinished (RelayServerGame -> Player.GameFinished) and Player.Disconnected (Player -> RelayServerGame) deadlocked.");
                    Assert.False(await match.A.Player.IsLoggedIn());
                    // Either way round, A is done with the match.
                    await TestSetup.WaitUntilAsync(async () => (await MatchTestSetup.RunningMatches(match.A.Player)).Count == 0,
                        "the match to leave A's running matches");
                    Assert.NotEqual(RelayServerPlayerGameStatus.InGame, await match.Game.GetPlayerStatus(match.A.Player));
                }
            }
        }

        [Fact]
        public async Task JoiningAndClosingTheSocketAtOnceDoesNotDeadlock()
        {
            for (int round = 0; round < 3; ++round)
            {
                var match = await MatchTestSetup.StartAsync(GrainFactory, joined: 1);

                // PlayerStarted held the match while it asked B for its display name; B's
                // Disconnected holds B while it tells the match.
                var join = JoinOrLate(match.Game, match.B.Player);
                var both = Task.WhenAll(join, match.B.Disconnect());

                Assert.True(await MatchTestSetup.FinishInTime(both),
                    "PlayerStarted (RelayServerGame -> Player) and Player.Disconnected (Player -> RelayServerGame) deadlocked.");
                Assert.False(await match.B.Player.IsLoggedIn());
                Assert.Equal(RelayServerGameStatus.FinishedWithDisconnect, await match.Game.GetGameState());
            }

            // When the disconnect comes first, the match has already finished.
            static async Task JoinOrLate(IRelayServerGame game, IPlayer player)
            {
                try
                {
                    await game.PlayerStarted(player);
                }
                catch (GameNotRunningException ex) when (ex.Message == RelayServerGameState.MatchFinished)
                {
                }
            }
        }

        [Fact]
        public async Task GetFriendsWhileAFriendGoesOfflineDoesNotDeadlock()
        {
            var (_, b, listA, _) = await Friends();

            // GetFriends holds A's list while it asks player B for public data; B's status change
            // holds player B while B's list tells A's list.
            var reads = MatchTestSetup.RepeatFor(TimeSpan.FromSeconds(3), _ => listA.GetFriends(Array.Empty<string>()));
            var flips = MatchTestSetup.RepeatFor(TimeSpan.FromSeconds(3),
                i => b.SetPresenceStatus(i % 2 == 0 ? OnlineStatus.Offline : OnlineStatus.Online));

            Assert.True(await MatchTestSetup.FinishInTime(Task.WhenAll(reads, flips)),
                "GetFriends (list A -> player B) and B's status change (player B -> list B -> list A) deadlocked.");
            await b.SetPresenceStatus(OnlineStatus.Offline);
            var friends = await listA.GetFriends(Array.Empty<string>());
            Assert.Equal(OnlineStatus.Offline, Assert.Single(friends.Friends).OnlineStatus);
        }

        [Fact]
        public async Task TwoFriendsChangingStatusAtOnceDoNotDeadlock()
        {
            var (_, _, listA, listB) = await Friends();

            for (int round = 0; round < 5; ++round)
            {
                var status = round % 2 == 0 ? OnlineStatus.Offline : OnlineStatus.Online;
                var both = Task.WhenAll(listA.SetStatus("a", status), listB.SetStatus("b", status));
                Assert.True(await MatchTestSetup.FinishInTime(both),
                    "SetStatus of two friends deadlocked (list A -> list B.SetFriendStatus while list B -> list A.SetFriendStatus).");
            }
        }

        /// <summary>Two logged in players of one game who are friends, and their friend lists.</summary>
        private async Task<(IPlayer A, IPlayer B, IPlayerFriendList ListA, IPlayerFriendList ListB)> Friends()
        {
            var (_, gameKey, session) = await TestSetup.CreateDeveloperGameAndSession(GrainFactory);
            var a = await TestSetup.CreateLoggedInPlayer(GrainFactory, session, Guid.NewGuid());
            var b = await TestSetup.CreateLoggedInPlayer(GrainFactory, session, Guid.NewGuid());
            var listA = GrainFactory.GetGrain<IPlayerFriendList>(gameKey, a.GetPrimaryKeyString(), null);
            var listB = GrainFactory.GetGrain<IPlayerFriendList>(gameKey, b.GetPrimaryKeyString(), null);
            await listA.Invite(b, "a", Array.Empty<byte>());
            await listB.AcceptInvitation(a, Array.Empty<byte>());
            Assert.Single((await listA.GetFriends(Array.Empty<string>())).Friends);
            return (a, b, listA, listB);
        }
    }
}
