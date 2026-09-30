#if !MASTER
using WildCat.Interfaces;
using Orleans;
using Orleans.Streams;
using Orleans.TestingHost;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Xunit;

namespace WildCat.Tests
{
    [Collection(ClusterCollection.Name)]
    public class RelayServerGameTests
    {
        private InProcessTestCluster cluster;
        public IGrainFactory GrainFactory => this.cluster.Client;
        public RelayServerGameTests(ClusterFixture fixture)
        {
            this.cluster = fixture.Cluster;
        }
        [Fact]
        public async Task StartGameTest()
        {
            var p1 = GrainFactory.GetGrain<IPlayer>("RG1");
            var p2 = GrainFactory.GetGrain<IPlayer>("RG2");
            var gameID = Guid.NewGuid();
            var game = GrainFactory.GetGrain<IRelayServerGame>(gameID, "gamekey", null);
            var state = await game.GetGameState();
            Assert.Equal(RelayServerGameStatus.NotStarted, state);
            var result = await game.StartGame(new IPlayer[] { p1, p2 }, null);
            Assert.Equal(2, result.Count);
            state = await game.GetGameState();
            Assert.Equal(RelayServerGameStatus.Started, state);
            var p1Status = await game.GetPlayerStatus(p1);
            var p2Status = await game.GetPlayerStatus(p2);
            Assert.Equal(RelayServerPlayerGameStatus.NotStarted, p1Status);
            Assert.Equal(RelayServerPlayerGameStatus.NotStarted, p2Status);
        }

        [Fact]
        public async Task StartGameAndJoinPlayersAndFinishTest()
        {
            var p1 = GrainFactory.GetGrain<IPlayer>("RG3");
            var p2 = GrainFactory.GetGrain<IPlayer>("RG4");
            var gameID = Guid.NewGuid();
            var game = GrainFactory.GetGrain<IRelayServerGame>(gameID, "gamekey", null);
            var state = await game.GetGameState();
            Assert.Equal(RelayServerGameStatus.NotStarted, state);
            var result = await game.StartGame(new IPlayer[] { p1, p2 }, null);
            Assert.Equal(2, result.Count);
            state = await game.GetGameState();
            Assert.Equal(RelayServerGameStatus.Started, state);
            var p1Status = await game.GetPlayerStatus(p1);
            var p2Status = await game.GetPlayerStatus(p2);
            Assert.Equal(RelayServerPlayerGameStatus.NotStarted, p1Status);
            Assert.Equal(RelayServerPlayerGameStatus.NotStarted, p2Status);
            await game.PlayerStarted(p1);
            state = await game.GetGameState();
            Assert.Equal(RelayServerGameStatus.SomeJoined, state);
            await game.PlayerStarted(p2);
            state = await game.GetGameState();
            Assert.Equal(RelayServerGameStatus.AllJoined, state);
            p1Status = await game.GetPlayerStatus(p1);
            p2Status = await game.GetPlayerStatus(p2);
            Assert.Equal(RelayServerPlayerGameStatus.InGame, p1Status);
            Assert.Equal(RelayServerPlayerGameStatus.InGame, p2Status);
            await game.PlayerFinished(p1);
            p1Status = await game.GetPlayerStatus(p1);
            Assert.Equal(RelayServerPlayerGameStatus.Finished, p1Status);
            state = await game.GetGameState();
            Assert.Equal(RelayServerGameStatus.SomeFinished, state);
            await game.PlayerFinished(p2);
            state = await game.GetGameState();
            Assert.Equal(RelayServerGameStatus.AllFinished, state);
            p2Status = await game.GetPlayerStatus(p2);
            Assert.Equal(RelayServerPlayerGameStatus.Finished, p2Status);
        }

        [Fact]
        public async Task StartGameAndJoinPlayersAndDisconnectTest()
        {
            Guid t = Guid.NewGuid();
            var p1 = GrainFactory.GetGrain<IPlayer>("RG5");
            var p2 = await CreateLoggedInPlayer(t);
            await p1.CreateAccountWithDevice("d1");
            var gameID = Guid.NewGuid();
            var game = GrainFactory.GetGrain<IRelayServerGame>(gameID, "gamekey", null);
            var state = await game.GetGameState();
            Assert.Equal(RelayServerGameStatus.NotStarted, state);
            var result = await game.StartGame(new IPlayer[] { p1, p2 }, null);
            Assert.Equal(2, result.Count);
            state = await game.GetGameState();
            Assert.Equal(RelayServerGameStatus.Started, state);
            var p1Status = await game.GetPlayerStatus(p1);
            var p2Status = await game.GetPlayerStatus(p2);
            Assert.Equal(RelayServerPlayerGameStatus.NotStarted, p1Status);
            Assert.Equal(RelayServerPlayerGameStatus.NotStarted, p2Status);
            await game.PlayerStarted(p1);
            state = await game.GetGameState();
            Assert.Equal(RelayServerGameStatus.SomeJoined, state);
            await game.PlayerStarted(p2);
            state = await game.GetGameState();
            Assert.Equal(RelayServerGameStatus.AllJoined, state);
            p1Status = await game.GetPlayerStatus(p1);
            p2Status = await game.GetPlayerStatus(p2);
            Assert.Equal(RelayServerPlayerGameStatus.InGame, p1Status);
            Assert.Equal(RelayServerPlayerGameStatus.InGame, p2Status);
            await game.PlayerFinished(p1);
            p1Status = await game.GetPlayerStatus(p1);
            Assert.Equal(RelayServerPlayerGameStatus.Finished, p1Status);
            state = await game.GetGameState();
            
            Assert.Equal(RelayServerGameStatus.SomeFinished, state);
            await p2.Disconnected(t);
            state = await game.GetGameState();
            Assert.Equal(RelayServerGameStatus.FinishedWithDisconnect, state);
            p2Status = await game.GetPlayerStatus(p2);
            Assert.Equal(RelayServerPlayerGameStatus.Disconnected, p2Status);
        }

        [Fact]
        public async Task StartGameAndJoinPlayersAndDisconnectAfterOthersFinished()
        {
            Guid t = Guid.NewGuid();
            var p1 = GrainFactory.GetGrain<IPlayer>("RG7");
            var p2 = await CreateLoggedInPlayer(t);
            await p1.CreateAccountWithDevice("d1");
            var gameID = Guid.NewGuid();
            var game = GrainFactory.GetGrain<IRelayServerGame>(gameID, "gamekey", null);
            var state = await game.GetGameState();
            Assert.Equal(RelayServerGameStatus.NotStarted, state);
            var gm = GameMode.CreateDefault();
            gm.FinishGameWithOneDisconnect = false;
            var result = await game.StartGame(new IPlayer[] { p1, p2 }, gm);
            Assert.Equal(2, result.Count);
            state = await game.GetGameState();
            Assert.Equal(RelayServerGameStatus.Started, state);
            var p1Status = await game.GetPlayerStatus(p1);
            var p2Status = await game.GetPlayerStatus(p2);
            Assert.Equal(RelayServerPlayerGameStatus.NotStarted, p1Status);
            Assert.Equal(RelayServerPlayerGameStatus.NotStarted, p2Status);
            await game.PlayerStarted(p1);
            state = await game.GetGameState();
            Assert.Equal(RelayServerGameStatus.SomeJoined, state);
            await game.PlayerStarted(p2);
            state = await game.GetGameState();
            Assert.Equal(RelayServerGameStatus.AllJoined, state);
            p1Status = await game.GetPlayerStatus(p1);
            p2Status = await game.GetPlayerStatus(p2);
            Assert.Equal(RelayServerPlayerGameStatus.InGame, p1Status);
            Assert.Equal(RelayServerPlayerGameStatus.InGame, p2Status);
            await game.PlayerFinished(p1);
            p1Status = await game.GetPlayerStatus(p1);
            Assert.Equal(RelayServerPlayerGameStatus.Finished, p1Status);
            state = await game.GetGameState();
            Assert.Equal(RelayServerGameStatus.SomeFinished, state);
            await p2.Disconnected(t);
            state = await game.GetGameState();
            Assert.Equal(RelayServerGameStatus.SomeFinished, state);
            p2Status = await game.GetPlayerStatus(p2);
            Assert.Equal(RelayServerPlayerGameStatus.Disconnected, p2Status);
        }

        [Fact]
        public async Task StartGameAndJoinPlayersAndDisconnectBeforeOthersFinished()
        {
            Guid t = Guid.NewGuid();
            var p1 = GrainFactory.GetGrain<IPlayer>("RG9");
            var p2 = await CreateLoggedInPlayer(t);
            await p1.CreateAccountWithDevice("d1");
            var gameID = Guid.NewGuid();
            var game = GrainFactory.GetGrain<IRelayServerGame>(gameID, "gamekey", null);
            var state = await game.GetGameState();
            Assert.Equal(RelayServerGameStatus.NotStarted, state);
            var gm = GameMode.CreateDefault();
            gm.FinishGameWithOneDisconnect = false;
            var result = await game.StartGame(new IPlayer[] { p1, p2 }, gm);
            Assert.Equal(2, result.Count);
            state = await game.GetGameState();
            Assert.Equal(RelayServerGameStatus.Started, state);
            var p1Status = await game.GetPlayerStatus(p1);
            var p2Status = await game.GetPlayerStatus(p2);
            Assert.Equal(RelayServerPlayerGameStatus.NotStarted, p1Status);
            Assert.Equal(RelayServerPlayerGameStatus.NotStarted, p2Status);
            await game.PlayerStarted(p1);
            state = await game.GetGameState();
            Assert.Equal(RelayServerGameStatus.SomeJoined, state);
            await game.PlayerStarted(p2);
            state = await game.GetGameState();
            Assert.Equal(RelayServerGameStatus.AllJoined, state);
            p1Status = await game.GetPlayerStatus(p1);
            p2Status = await game.GetPlayerStatus(p2);
            Assert.Equal(RelayServerPlayerGameStatus.InGame, p1Status);
            Assert.Equal(RelayServerPlayerGameStatus.InGame, p2Status);
            await p2.Disconnected(t);
            state = await game.GetGameState();
            Assert.Equal(RelayServerGameStatus.SomeJoined, state);
            p2Status = await game.GetPlayerStatus(p2);
            Assert.Equal(RelayServerPlayerGameStatus.Disconnected, p2Status);
        }

        [Fact]
        public async Task StartGameAndJoinPlayersAndDisconnectAndRejoin()
        {
            Guid t = Guid.NewGuid();
            var (developerKey, gameKey, sessionKey) = await TestSetup.CreateDeveloperGameAndSession(GrainFactory);
            var p1 = GrainFactory.GetGrain<IPlayer>("R11");
            var p2 = await TestSetup.CreateLoggedInPlayer(GrainFactory, sessionKey, t);
            await p1.CreateAccountWithDevice("d1");
            var gameID = Guid.NewGuid();
            var game = GrainFactory.GetGrain<IRelayServerGame>(gameID, "gamekey", null);
            var state = await game.GetGameState();
            Assert.Equal(RelayServerGameStatus.NotStarted, state);
            var gm = GameMode.CreateDefault();
            gm.FinishGameWithOneDisconnect = false;
            var result = await game.StartGame(new IPlayer[] { p1, p2 }, gm);
            Assert.Equal(2, result.Count);
            state = await game.GetGameState();
            Assert.Equal(RelayServerGameStatus.Started, state);
            var p1Status = await game.GetPlayerStatus(p1);
            var p2Status = await game.GetPlayerStatus(p2);
            Assert.Equal(RelayServerPlayerGameStatus.NotStarted, p1Status);
            Assert.Equal(RelayServerPlayerGameStatus.NotStarted, p2Status);
            await game.PlayerStarted(p1);
            state = await game.GetGameState();
            Assert.Equal(RelayServerGameStatus.SomeJoined, state);
            await game.PlayerStarted(p2);
            state = await game.GetGameState();
            Assert.Equal(RelayServerGameStatus.AllJoined, state);
            p1Status = await game.GetPlayerStatus(p1);
            p2Status = await game.GetPlayerStatus(p2);
            Assert.Equal(RelayServerPlayerGameStatus.InGame, p1Status);
            Assert.Equal(RelayServerPlayerGameStatus.InGame, p2Status);
            await p2.Disconnected(t);
            state = await game.GetGameState();
            Assert.Equal(RelayServerGameStatus.SomeJoined, state);
            p2Status = await game.GetPlayerStatus(p2);
            Assert.Equal(RelayServerPlayerGameStatus.Disconnected, p2Status);
            // The disconnect logged the player out and ended its session, so it comes back on a new one.
            var newSessionKey = await GrainFactory.GetGrain<IDeveloper>(developerKey).InitializeSession(gameKey);
            Assert.NotNull(await p2.LoginWithDeviceID(p2.GetPrimaryKeyString(), newSessionKey, Guid.NewGuid()));
            var matches = await p2.GetPendingMatches();
            Assert.True(matches.Count == 1, matches.Count.ToString());
            Assert.True(matches[0].MatchId == gameID);
            await p2.RejoinMatch(game);
            p2Status = await game.GetPlayerStatus(p2);
            Assert.Equal(RelayServerPlayerGameStatus.InGame, p2Status);
            state = await game.GetGameState();
            Assert.Equal(RelayServerGameStatus.AllJoined, state);
        }

        /// <summary>
        /// Creates a device account logged in with <paramref name="loginTicket"/>. Player.Disconnected
        /// ignores any ticket but the current login's, so the disconnect tests need a real login.
        /// </summary>
        private async Task<IPlayer> CreateLoggedInPlayer(Guid loginTicket)
        {
            var (_, _, sessionKey) = await TestSetup.CreateDeveloperGameAndSession(GrainFactory);
            return await TestSetup.CreateLoggedInPlayer(GrainFactory, sessionKey, loginTicket);
        }
    }
}
#endif