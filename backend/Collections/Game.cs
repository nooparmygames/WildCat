using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using Interfaces;
using Orleans;

namespace Collections
{
    public class Game : Grain<GameState>, IGame
    {
        
        
        public override Task OnActivateAsync(CancellationToken cancellationToken)
        {
            State.maxPlayers = 2; //TODO make it flexible 
            //subsManager = new ObserverSubscriptionManager<IGameObserver>();
            return base.OnActivateAsync(cancellationToken);
        }

        public Task<PlayerStateInGame> GetPlayerState(IPlayer player)
        {
            return Task.FromResult(State.GetPlayerData(player).state);
        }


        public Task<GameFinalState> GetGameState()
        {
            return Task.FromResult(State.finishState);
        }

        //public async Task<bool> ArePlayersReady(iPlayer[] players)
        //{
        //    List<Task<int>> promises = new List<Task<int>>();
        //    foreach (var p in players)
        //    {
        //        promises.Add(p.GetPlayerInt("coin"));
        //    }
        //    int[] coins = await Task.WhenAll(promises);
        //    var result = coins.Any(x => x < State.Cost);
        //    if (!result)
        //    {
        //        Console.WriteLine("players with incorrect coins added");
        //    }
        //    return result;
        //}

        public async Task<Dictionary<IPlayer,string>> StartGame(IPlayer[] players)
        {
            if (State.maxPlayers != players.Length)
                throw new ArgumentException("argument count should be 2 but is " + players.Length);
            State.Players = new List<PlayerInGameData<float>>();
            PlayerInGameData<float> first = new PlayerInGameData<float>(players[0], 0);
            PlayerInGameData<float> second = new PlayerInGameData<float>(players[1], 0);
            //await first.player.IncrementPlayerInt("coin", -State.Cost);
            //await second.player.IncrementPlayerInt("coin", -State.Cost);
            first.Elo = await first.player.GetEloScore();
            first.name = await first.player.GetPlayerName();
            second.Elo = await second.player.GetEloScore();
            second.name = await second.player.GetPlayerName();
            State.Players.Add(first);
            State.Players.Add(second);
            State.finishState = GameFinalState.Started;
            return State.Players.ToDictionary(x => x.player, y => y.name);
        }

        public Task PlayerFinished(IPlayer player, float finalValue)
        {
            var p = State.GetPlayerData(player);
            p.value = finalValue;
            p.state = PlayerStateInGame.Finished;
            if (State.Players.All(x => x.state == PlayerStateInGame.Finished))
                return FinishGame();
            return Task.CompletedTask;
        }

        public async Task<IPlayer> FinishGame()
        {
            IPlayer winner = null;
            IPlayer loser = null;
            GameFinalState result = GameFinalState.Started;

            //If we have no disconnected players and game ended normally
            if (!State.Players.Any(x => x.state == PlayerStateInGame.Disconnected))
            {
                List<Task<float>> promises = new List<Task<float>>();
                for (int i = 0; i < State.Players.Count; ++i)
                    promises.Add(GetPlayerValue(State.Players[i].player));
                var values = await Task.WhenAll(promises);
                
                //check for draw draw
                if (values[0] == values[1])
                {
                    result = GameFinalState.FinishedInDraw;
                }
                //First plaeyr won
                else if (values[0] > values[1])
                {
                    result = GameFinalState.FinishedWithWinner;
                    winner = State.Players[0].player;
                    loser = State.Players[1].player;
                }
                else //second plaeyr won
                {
                    result = GameFinalState.FinishedWithWinner;
                    winner = State.Players[1].player;
                    loser = State.Players[0].player;
                }
            }
            else //If we have a disconnected player.
            {
                //Fix player States, since at DC time other player might be still playing.
                for (int i = 0; i < State.Players.Count; ++i)
                    if (State.Players[i].state == PlayerStateInGame.Playing)
                        State.Players[i].state = PlayerStateInGame.Finished;

                //If both are disconnected, rare but possible.
                if (State.Players[0].state == PlayerStateInGame.Disconnected && State.Players[1].state == PlayerStateInGame.Disconnected)
                {
                    result = GameFinalState.FinishedInDraw;
                }
                else //if only one was disconnected
                {
                    result = GameFinalState.FinishedWithWinner;
                    winner = (State.Players[0].state == PlayerStateInGame.Disconnected) ? State.Players[1].player : State.Players[0].player;
                    loser = (State.Players[0].state == PlayerStateInGame.Disconnected) ? State.Players[0].player : State.Players[1].player;
                }

            }

            State.finishState = result;
            if (result == GameFinalState.FinishedWithWinner)
            {
                await winner.AddGameFinalResult(this, GameResult.Winner, State.Cost, State.GetPlayerData(loser).Elo);
                await loser.AddGameFinalResult(this, GameResult.Loser, State.Cost, State.GetPlayerData(winner).Elo);
            }
            else
            {
                for (int i = 0; i < State.Players.Count; ++i)
                    await State.Players[i].player.AddGameFinalResult(this, GameResult.Draw, State.Cost, 0); //ELO not used
            }
            //subsManager.Notify(s => s.GameFinished(this, State.Players.ToArray()));
            await WriteStateAsync();
            return winner;
        }

        public Task<int> GetCost()
        {
            return Task.FromResult(State.Cost);
        }



        public Task<List<IPlayer>> GetPlayers()
        {
            return Task.FromResult(State.Players.Select(x => x.player).ToList());
        }

        public Task<float> GetPlayerValue(IPlayer playerID)
        {
            return Task.FromResult(State.GetPlayerData(playerID).value);
        }

        public async Task PlayerDisconnected(IPlayer player)
        {
            State.GetPlayerData(player).state = PlayerStateInGame.Disconnected;
            await FinishGame();
            
        }

        public Task SetGameProperties(int cost)
        {
            State.Cost = cost;
            return Task.CompletedTask;
        }

        public Task SetPlayerValue(IPlayer playerID, float value)
        {

            State.GetPlayerData(playerID).value = value;
            return Task.CompletedTask;
        }

        public Task<List<string>> GetPlayerNames()
        {
            return Task.FromResult(State.Players.Select(x => x.name).ToList());
        }

        public Task<float> GetOtherPlayerValue(IPlayer player)
        {
            return Task.FromResult(State.Players.FirstOrDefault(x => x.player.GetPrimaryKeyString() != player.GetPrimaryKeyString()).value);
            
        }

        //public Task Subscribe(IGameObserver observer)
        //{
        //    subsManager.Subscribe(observer);
        //    return Task.CompletedTask;
        //}

        //public Task Unsubscribe(IGameObserver observer)
        //{
        //    subsManager.Unsubscribe(observer);
        //    return Task.CompletedTask;
        //}
    }

    [Serializable]
    public class GameState
    {
        public List<PlayerInGameData<float>> Players { get; set; }
        public int Cost { get; set; }
        public int maxPlayers { get; set; }
        public GameFinalState finishState = GameFinalState.NotStarted;

        public PlayerInGameData<float> GetPlayerData(IPlayer player)
        {
            return Players.First(x => x.player.GetPrimaryKeyString() == player.GetPrimaryKeyString());
        }

        public GameState()
        {
            Players = new List<PlayerInGameData<float>>();
        }
    }
}
