using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using WildCat.Interfaces;
using Orleans;
using Orleans.Concurrency;
using Orleans.Runtime;

namespace WildCat.Grains
{
    [Reentrant]
    class RelayServerMatchMaker : Grain, ICustomMatchMaker
    {
        private const int CHECK_PERIOD = 3;
        private List<RelayServerPlayerData> onlinePlayers;
        private HashSet<IPlayer> pendingPlayers;
        private Dictionary<string, TaskCompletionSource<FoundRelayMatchResult>> PlayerTasks;

        private IGrainTimer timer;

        private string gameKey;
        private GameMode gameMode;

        public override async Task OnActivateAsync(CancellationToken cancellationToken)
        {
            string type;
            var gk = this.GetPrimaryKey(out type);
            gameKey = gk.ToString();
            var gameModesManager = GrainFactory.GetGrain<IGameModeManager>(gk);
            gameMode = await gameModesManager.GetGameModeData(type);
            if (gameMode == null)
            {
                gameMode = GameMode.CreateDefault();

            }

            onlinePlayers = new List<RelayServerPlayerData>();
            pendingPlayers = new HashSet<IPlayer>();
            PlayerTasks = new Dictionary<string, TaskCompletionSource<FoundRelayMatchResult>>();
            TryRegisterTimer();
            await base.OnActivateAsync(cancellationToken);
        }

        private void TryRegisterTimer()
        {
            if (timer == null)
                timer = this.RegisterGrainTimer<object>(OnTimer, null, new GrainTimerCreationOptions
                {
                    DueTime = TimeSpan.FromSeconds(0.1),
                    Period = TimeSpan.FromSeconds(CHECK_PERIOD),
                    Interleave = true
                });
        }

        public Task<int> GetOnlinePlayersCount()
        {
            return Task.FromResult(onlinePlayers.Count);
        }

        private async Task OnTimer(object o)
        {
            if (onlinePlayers.Count < 2)
            {
                if (onlinePlayers.Count == 1)
                {
                    var player = onlinePlayers[0];
                    //check timeout of tasks
                    if (PlayerTasks.ContainsKey(player.Player.GetPrimaryKeyString())
                        && CheckCriteriaIndexAndRequestTimeout(player))
                    {
                        PlayerTasks[player.Player.GetPrimaryKeyString()].SetResult(null);
                        PlayerTasks.Remove(player.Player.GetPrimaryKeyString());
                        onlinePlayers.RemoveAt(0);
                    }
                }
                return;
            }
            int i = 0;
            while (i < onlinePlayers.Count && onlinePlayers.Count >= 2)
            {
                var player = onlinePlayers[i];
                //if the player is gone while we are waiting
                if (!PlayerTasks.ContainsKey(player.Player.GetPrimaryKeyString()))
                {
                    i++;
                    continue;
                }
                //check timeout of tasks
                if (CheckCriteriaIndexAndRequestTimeout(player))
                {
                    PlayerTasks[player.Player.GetPrimaryKeyString()].SetResult(null);
                    PlayerTasks.Remove(player.Player.GetPrimaryKeyString());
                    onlinePlayers.RemoveAt(i);
                    continue;
                }


                if (pendingPlayers.Contains(player.Player))
                {
                    i++;
                    continue;
                }
                var opponents = FindOpponents(player);
                if (opponents.Count > 0)
                {
                    foreach (var opponent in opponents)
                        pendingPlayers.Add(opponent.Player);
                    pendingPlayers.Add(player.Player);
                    try
                    {
                        List<IPlayer> players = new List<IPlayer>();
                        players.AddRange(opponents.Select(x => x.Player));
                        players.Add(player.Player);
                        //TODO make methods to accept list to avoid conversion
                        var game = await CreateGame(players.ToArray(), "");

                        PlayerTasks[player.Player.GetPrimaryKeyString()].SetResult(new FoundRelayMatchResult(game));
                        PlayerTasks.Remove(player.Player.GetPrimaryKeyString());
                        foreach (var opponent in opponents)
                        {
                            PlayerTasks[opponent.Player.GetPrimaryKeyString()].SetResult(new FoundRelayMatchResult(game));
                            PlayerTasks.Remove(opponent.Player.GetPrimaryKeyString());
                        }
                    }
                    catch
                    {
                        
                        i++;
                    }
                    finally
                    {
                        pendingPlayers.Remove(player.Player);
                        foreach (var opponent in opponents)
                            pendingPlayers.Remove(opponent.Player);
                    }
                }
                else //No opponent found
                {
                    i++;
                }
            } //End of while loop
        }

        private bool CheckCriteriaIndexAndRequestTimeout(RelayServerPlayerData player)
        {
            player.CriteriaIndex = GetPlayerCriteriaIndex(player);
            return (DateTime.Now - player.StartTime).Seconds > player.WaitTime ||
                                (DateTime.Now - player.StartTime).Minutes > 0;
        }

        private async Task<IRelayServerGame> CreateGame(IPlayer[] players, string gameName)
        {
            var game = GrainFactory.GetGrain<IRelayServerGame>(Guid.NewGuid(), gameKey, null);
            var names = await game.StartGame(players,gameMode);


            onlinePlayers.RemoveAll(x => players.Contains(x.Player));

            return game;
        }

        private List<RelayServerPlayerData> FindOpponents(RelayServerPlayerData player)
        {
            var criteria = gameMode.Criterias[player.CriteriaIndex];
            int maxNeeded = (criteria.IsMinPlayersEnough) ? gameMode.MinPlayers - 1 : gameMode.MaxPlayers - 1;
            List<RelayServerPlayerData> opponents = new List<RelayServerPlayerData>();
            var list = onlinePlayers.OrderBy(x => Math.Abs(x.Skill - player.Skill)).ToList();
            var count = list.Count();
            if (count < 2)
                return null;
            int i = 0;
            while (i < count && opponents.Count < gameMode.MaxPlayers - 1)
            {
                if (Math.Abs(list[i].Skill - player.Skill) <= criteria.AcceptableThreshold &&
                    list[i].CriteriaIndex <= player.CriteriaIndex &&
                    list[i].Player.GetPrimaryKeyString() != player.Player.GetPrimaryKeyString() &&
                    !pendingPlayers.Contains(list[i].Player))
                {
                    opponents.Add(list[i]);
                }
                else
                {
                }
                ++i;
            }
            if (opponents.Count > 0 && opponents.Count < maxNeeded)
                return new List<RelayServerPlayerData>();
            return opponents;
        }

        private int GetPlayerCriteriaIndex(RelayServerPlayerData data)
        {
            var timeSpent = (DateTime.Now - data.StartTime).TotalSeconds;
            int criteriaIndex = -1;
            for (int i = gameMode.Criterias.Count - 1; i >= 0; --i)
            {
                if (timeSpent >= gameMode.Criterias[i].StartTimeInSeconds)
                {
                    criteriaIndex = i;
                    break;
                }
            }
            if (criteriaIndex < 0)
                throw new InvalidOperationException("The criteria should never be null");
            return criteriaIndex;
        }

        private void AddPlayer(uint waitTime, uint skill, IPlayer player)
        {
            onlinePlayers.Add(new RelayServerPlayerData(player, skill, waitTime));

        }

        /// <summary>
        /// Finds a match, if timeout happens returns null.
        /// </summary>
        /// <param name="player"></param>
        /// <param name="skill"></param>
        /// <param name="maximumWaitTime"></param>
        /// <returns></returns>
        public async Task<FoundRelayMatchResult> FindMatch(IPlayer player, uint skill, uint maximumWaitTime)
        {
            if (PlayerTasks.ContainsKey(player.GetPrimaryKeyString()))
                return (await PlayerTasks[player.GetPrimaryKeyString()].Task);
            var playerSkill = skill;
            AddPlayer(maximumWaitTime, playerSkill, player);
            TaskCompletionSource<FoundRelayMatchResult> t = new TaskCompletionSource<FoundRelayMatchResult>();

            PlayerTasks.Add(player.GetPrimaryKeyString(), t);

            return await t.Task;
        }

        public Task<bool> RemoveDisconnectedPlayer(IPlayer player)
        {
            onlinePlayers.RemoveAll(x => x.Player.GetPrimaryKeyString() == player.GetPrimaryKeyString());
            return Task.FromResult(true);
        }
    }

    public class RelayServerPlayerData
    {
        public IPlayer Player;
        public uint Skill;
        public DateTime StartTime;
        public uint WaitTime;
        public int CriteriaIndex;

        public RelayServerPlayerData(IPlayer player, uint skill, uint waitTime)
        {
            this.Skill = skill;
            this.StartTime = DateTime.Now;
            this.Player = player;
            this.WaitTime = waitTime;
            this.CriteriaIndex = 0;
        }
    }
}
