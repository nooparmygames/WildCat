using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Interfaces;
using Orleans;
using Orleans.Concurrency;

namespace Collections
{
    [Reentrant]
    class MatchMaker : Grain, IMatchMaker
    {
        private List<PlayerData> onlinePlayers;
        private HashSet<IPlayer> pendingPlayers;
        
        private Dictionary<IPlayer, string> opponentNames;
        private Dictionary<string,Tuple<TaskCompletionSource<FoundMatchResult>,DateTime,int>> PlayerTasks;

        private IDisposable timer;
        private Random rand;
        private bool isThereAnyNewPlayer = false;
        
        public override async Task OnActivateAsync()
        {
            rand = new Random();
            onlinePlayers = new List<PlayerData>();
            opponentNames = new Dictionary<IPlayer, string>();

            pendingPlayers = new HashSet<IPlayer>();
            PlayerTasks = new Dictionary<string, Tuple<TaskCompletionSource<FoundMatchResult>, DateTime, int>>();
            TryRegisterTimer();
            await base.OnActivateAsync();
        }

        public Task<int> GetOnlinePlayersCount()
        {
            return Task.FromResult(onlinePlayers.Count);
        }

        private void TryRegisterTimer()
        {
            if (timer == null)
                timer = RegisterTimer(OnTimer, null, TimeSpan.FromSeconds(1), TimeSpan.FromSeconds(3));
        }

        

        private async Task OnTimer(object o)
        {
            if(onlinePlayers.Count < 2 || !isThereAnyNewPlayer)
            {
                timer.Dispose();
                timer = null;
                return;
            }
            int i = 0;
            while(i < onlinePlayers.Count && onlinePlayers.Count >= 2)
            {
                var player = onlinePlayers[i];
                if ((DateTime.Now - PlayerTasks[player.Player.GetPrimaryKeyString()].Item2).Seconds > PlayerTasks[player.Player.GetPrimaryKeyString()].Item3 ||
                    (DateTime.Now - PlayerTasks[player.Player.GetPrimaryKeyString()].Item2).Minutes > 0)
                {
                    PlayerTasks[player.Player.GetPrimaryKeyString()].Item1.SetResult(null);
                    PlayerTasks.Remove(player.Player.GetPrimaryKeyString());
                    onlinePlayers.RemoveAt(i);
                    continue;
                }

                var elo = player.Elo;
                if (pendingPlayers.Contains(player.Player))
                {
                    i++;
                    continue;
                }
                var opponent = FindOther(player.Player, elo);
                if (opponent != null)
                {
                    pendingPlayers.Add(opponent);
                    pendingPlayers.Add(player.Player);
                    try
                    {
                        int opponentELO = await opponent.GetEloScore();
                        string ext;
                        var game = await CreateGame(player.Player, opponent, (int)this.GetPrimaryKeyLong(out ext), ext, elo, opponentELO);
                        var opponentName = opponentNames[player.Player];
                        var playerName = opponentNames[opponent];
                        opponentNames.Remove(player.Player);
                        opponentNames.Remove(opponent);
                        PlayerTasks[player.Player.GetPrimaryKeyString()].Item1.SetResult(new FoundMatchResult(game, opponentName));
                        PlayerTasks[opponent.GetPrimaryKeyString()].Item1.SetResult(new FoundMatchResult(game, playerName));
                        PlayerTasks.Remove(player.Player.GetPrimaryKeyString());
                        PlayerTasks.Remove(opponent.GetPrimaryKeyString());
                    }
                    catch
                    {
                        i++;
                    }
                    finally
                    {
                        pendingPlayers.Remove(player.Player);
                        pendingPlayers.Remove(opponent);
                    }
                }
                else
                {
                    i++;
                }
            }
            isThereAnyNewPlayer = false;
        }

        private async Task<IGame> CreateGame(IPlayer p1, IPlayer p2, int cost, string gameName, int elo1, int elo2)
        {
            var game = GrainFactory.GetGrain<IGame>(Guid.NewGuid(),"gamekey",null);
            await game.SetGameProperties(cost);
            var names = await game.StartGame(new IPlayer[] { p1, p2 });
            var count = onlinePlayers.Count;
            onlinePlayers.RemoveAll(x => x.Player == p1 || x.Player == p2);
            opponentNames[p1] = names[p2];
            opponentNames[p2] = names[p1];
            return game;
        }

        private IPlayer FindOther(IPlayer player, int elo)
        {
            IPlayer opponent;
            var list = onlinePlayers.OrderBy(x => Math.Abs(x.Elo - elo)).ToList();
            var count = list.Count();
            if (count < 2)
                return null;
            do
            {
                
                //a random number [0,count)
                var i = rand.Next(count);
                opponent = list[i].Player;
            } while (opponent.GetPrimaryKeyString() == player.GetPrimaryKeyString());
            return opponent;
        }

        private void AddPlayer(int elo, IPlayer player)
        {
            onlinePlayers.Add(new PlayerData(player, elo));
            isThereAnyNewPlayer = true;
        }

        public async Task<FoundMatchResult> FindMatch(IPlayer player, int maximumWaitTime)
        {
            if (PlayerTasks.ContainsKey(player.GetPrimaryKeyString()))
                return await (PlayerTasks[player.GetPrimaryKeyString()].Item1.Task);
            var elo =  await player.GetEloScore();
            AddPlayer(elo, player);
            TaskCompletionSource<FoundMatchResult> t = new TaskCompletionSource<FoundMatchResult>();
            PlayerTasks.Add(player.GetPrimaryKeyString(),Tuple.Create(t,DateTime.Now,maximumWaitTime));
            TryRegisterTimer();
            return await t.Task;
        }

        public Task<bool> RemoveDisconnectedPlayer(IPlayer player)
        {
            onlinePlayers.RemoveAll(x => x.Player.GetPrimaryKeyString() == player.GetPrimaryKeyString());
            return Task.FromResult(true);
        }
    }

    public struct PlayerData
    {
        public IPlayer Player;
        public int Elo;

        public PlayerData(IPlayer player, int elo)
        {
            this.Elo = elo;
            this.Player = player;
        }
    }
}
