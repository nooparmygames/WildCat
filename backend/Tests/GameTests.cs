using Interfaces;
using Orleans;
using Orleans.Streams;
using Orleans.TestingHost;
using System;
using System.Linq;
using System.Threading.Tasks;
using Tests;
using Xunit;

namespace GrainUnitTests
{
    [Collection(ClusterCollection.Name)]
    public class GameTests
    {
        private InProcessTestCluster cluster;
        public IGrainFactory GrainFactory => this.cluster.Client;
        public GameTests(ClusterFixture fixture)
        {
            this.cluster = fixture.Cluster;
        }
        [Fact]
        public async Task CreateGameAndCheckProps()
        {
            var game = GrainFactory.GetGrain<IGame>(Guid.NewGuid(),"gamekey",null);
            await game.SetGameProperties(30);
            int cost = await game.GetCost();
            Assert.Equal(30, cost);
        }

        [Fact]
        public async Task AddPlayersToGame()
        {
            var game = GrainFactory.GetGrain<IGame>(Guid.NewGuid(),"gamekey",null);
            await game.SetGameProperties(30);
            var p1 = GrainFactory.GetGrain<IPlayer>("player1");
            var p2 = GrainFactory.GetGrain<IPlayer>("player2");
            await p1.CreateAccountWithDevice(p1.GetPrimaryKeyString());
            await p2.CreateAccountWithDevice(p2.GetPrimaryKeyString());
            await game.StartGame(new IPlayer[] { p1, p2 });
            var players = await game.GetPlayers();
            var p1Value = await game.GetPlayerValue(p1);
            var p2Value = await game.GetPlayerValue(p2);
            Assert.Equal(2, players.Count);
            Assert.True(p1Value == p2Value && p1Value == 0f);
        }

        [Fact]
        public async Task FinishGame()
        {
            var game = GrainFactory.GetGrain<IGame>(Guid.NewGuid(),"gamekey",null);
            var game2 = GrainFactory.GetGrain<IGame>(Guid.NewGuid(),"gamekey",null);
            var state = await game.GetGameState();
            Assert.Equal(GameFinalState.NotStarted, state);
            await game.SetGameProperties(30);
            await game2.SetGameProperties(50);
            var p1 = GrainFactory.GetGrain<IPlayer>("player3");
            var p2 = GrainFactory.GetGrain<IPlayer>("player4");
            var p3 = GrainFactory.GetGrain<IPlayer>("player5");
            var p4 = GrainFactory.GetGrain<IPlayer>("player6");
            await p1.CreateAccountWithDevice(p1.GetPrimaryKeyString());
            await p2.CreateAccountWithDevice(p2.GetPrimaryKeyString());
            await p3.CreateAccountWithDevice(p3.GetPrimaryKeyString());
            await p4.CreateAccountWithDevice(p4.GetPrimaryKeyString());
            await game.StartGame(new IPlayer[] { p1, p2 });
            state = await game.GetGameState();
            Assert.Equal(GameFinalState.Started, state);
            await game2.StartGame(new IPlayer[] { p3, p4 });
            await game.SetPlayerValue(p1, 10);
            await game.SetPlayerValue(p2, 12);
            var score = await game.GetOtherPlayerValue(p1);
            await game.PlayerFinished(p1, 10);
            await game.PlayerFinished(p2, 12);
            await game2.PlayerFinished(p3, 13);
            await game2.PlayerFinished(p4, 13);
            state = await game.GetGameState();
            var state2 = await game2.GetGameState();
            Assert.Equal(GameFinalState.FinishedWithWinner, state);
            Assert.Equal(GameFinalState.FinishedInDraw, state2);
            Assert.Equal(12, score);
        }

        [Fact]
        public async Task GameFinishedWithDisconnect()
        {
            var game = GrainFactory.GetGrain<IGame>(Guid.NewGuid(),"gamekey",null);
            var p1 = GrainFactory.GetGrain<IPlayer>("player7");
            var p2 = GrainFactory.GetGrain<IPlayer>("player8");
            await p1.CreateAccountWithDevice(p1.GetPrimaryKeyString());
            await p2.CreateAccountWithDevice(p2.GetPrimaryKeyString());
            await game.StartGame(new IPlayer[] { p1, p2 });
            var p1State = await game.GetPlayerState(p1);
            var p2State = await game.GetPlayerState(p2);
            Assert.Equal(PlayerStateInGame.Playing, p1State);
            Assert.Equal(PlayerStateInGame.Playing, p2State);
            await game.PlayerDisconnected(p1);
            var state = await game.GetGameState();
            Assert.Equal(GameFinalState.FinishedWithWinner, state);
            var p1Elo = await p1.GetEloScore();
            var p2Elo = await p2.GetEloScore();
            p1State = await game.GetPlayerState(p1);
            p2State = await game.GetPlayerState(p2);
            Assert.Equal(1493, p1Elo);
            Assert.Equal(1507, p2Elo);
            Assert.Equal(PlayerStateInGame.Disconnected, p1State);
            Assert.Equal(PlayerStateInGame.Finished, p2State);
        }
    }
}
