using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using Orleans;
using Orleans.Runtime;
using WildCat.Interfaces;
using System.Linq;

namespace WildCat.Grains
{
    public class LeaderBoard : Grain, ILeaderBoard
    {
        private readonly IPersistentState<LeaderBoardState> state;

        public LeaderBoard([PersistentState(GrainStateNames.LeaderBoard, GrainStorageNames.Blob)] IPersistentState<LeaderBoardState> state)
        {
            this.state = state;
        }

        private LeaderBoardState State => state.State;

        private List<LeaderBoardEntry> _leaderBoardData;
        public override async Task OnActivateAsync(CancellationToken cancellationToken)
        {
            _leaderBoardData = await CreateList();
            //_IsCreated = State.IsCreated;
            await base.OnActivateAsync(cancellationToken);
        }

        private async Task<List<LeaderBoardEntry>> CreateList()
        {
            var data = new List<LeaderBoardEntry>();
            var tasks = new List<Task<List<LeaderBoardEntry>>>();
            for (int i = 0; i < State.NumberOfLists; i++)
            {
                tasks[i] =  GrainFactory.GetGrain<ILeaderBoardStorage>(ResourcesUtility.GetLeaderBoardChunkKey(this.GetPrimaryKeyString(), i)).GetEntries();
            }
            var lists = await Task.WhenAll(tasks);
            for (int i = 0; i < lists.Length; i++)
            {
                
                data.AddRange(lists[i]);
            }
            return data;
        }
        
        public async Task AddPlayerScore(IPlayer player, int score)
        {
            AssertIsCreated();
            int index = FindPlayerIndex(player);
            bool isSet = false;
            if (index < 0)
            {
                var name = await player.GetPlayerName();
                _leaderBoardData.Add(new LeaderBoardEntry(player.GetPrimaryKeyString(), name, score, DateTime.Now));
                isSet = true;
            }
            else if (_leaderBoardData[index].Score < score || !State.AcceptBetterScoresOnly)
            {
                var name = await player.GetPlayerName();
                _leaderBoardData[index] = new LeaderBoardEntry(player.GetPrimaryKeyString(), name, score, DateTime.Now);
                isSet = true;
            }
            if (isSet)
            {
                _leaderBoardData.Sort((x, y) => y.Score - x.Score);
                for (int i = 0; i < _leaderBoardData.Count / State.MaxListLength; i++)
                {
                    await GrainFactory.GetGrain<ILeaderBoardStorage>(ResourcesUtility.GetLeaderBoardChunkKey(this.GetPrimaryKeyString(), i))
                        .WriteEntries(_leaderBoardData.GetRange(i*State.MaxListLength, State.MaxListLength));
                }

                var lastChunkIndex = ((_leaderBoardData.Count / State.MaxListLength) + 1);
                if (lastChunkIndex * State.MaxListLength < _leaderBoardData.Count)
                {
                    await GrainFactory.GetGrain<ILeaderBoardStorage>(ResourcesUtility.GetLeaderBoardChunkKey(this.GetPrimaryKeyString(), lastChunkIndex))
                        .WriteEntries(_leaderBoardData.GetRange(lastChunkIndex*State.MaxListLength, _leaderBoardData.Count - (lastChunkIndex*State.MaxListLength)));
                    State.NumberOfLists = _leaderBoardData.Count / State.MaxListLength + 1;
                }
                else
                {
                    State.NumberOfLists = _leaderBoardData.Count / State.MaxListLength;
                }
                await state.WriteStateAsync();
            }
        }

        private int FindPlayerIndex(IPlayer player)
        {
            return _leaderBoardData.FindIndex(x => x.PlayerID == player.GetPrimaryKeyString());
        }


        private async Task<Tuple<int , int, List<LeaderBoardEntry>>> FindPlayerData(IPlayer player)
        {
            AssertIsCreated();
            int listIndex = -1;
            int dataIndex = -1;
            List<LeaderBoardEntry> data = new List<LeaderBoardEntry>();
            for (int i = 0; i < State.NumberOfLists; i++)
            {
                data = await GrainFactory.GetGrain<ILeaderBoardStorage>(ResourcesUtility.GetLeaderBoardChunkKey(this.GetPrimaryKeyString(), i)).GetEntries();
                var index = data.FindIndex(x => x.PlayerID == player.GetPrimaryKeyString());
                if (index >= 0)
                {
                    listIndex = i;
                    dataIndex = index;
                    break;
                }
            }
            return Tuple.Create(listIndex, dataIndex, data);
        }
        

        public Task FinishLeaderBoard()
        {
            AssertIsCreated();
            return state.ClearStateAsync();
        }

        public Task<Tuple<int, List<LeaderBoardEntry>>> GetAroundMe(IPlayer player, int count)
        {
            AssertIsCreated();
            var index = FindPlayerIndex(player);
            //if not in the board
            if (index < 0)
                return Task.FromResult(Tuple.Create(index, new List<LeaderBoardEntry>()));
            if (_leaderBoardData.Count < count)
                return Task.FromResult(Tuple.Create(index, _leaderBoardData));
            var halfCount = count / 2;
            var start = (index - halfCount > 0) ? index - halfCount : 0;
            if (_leaderBoardData.Count - count < start)
            {
                start = _leaderBoardData.Count - count;
            }
            var around = _leaderBoardData.GetRange(start, count);
            return Task.FromResult(Tuple.Create(index, around));
        }

        public Task<int> GetPlayerRank(IPlayer player)
        {
            AssertIsCreated();
            return Task.FromResult(FindPlayerIndex(player));
        }

        public Task<Tuple<int, List<LeaderBoardEntry>>> GetTopEntries(int count, int offset)
        {
            AssertIsCreated();
            if (offset > _leaderBoardData.Count - 1)
                offset = 0;
            if (offset + count > _leaderBoardData.Count - 1)
                count = _leaderBoardData.Count - offset;
            return Task.FromResult(Tuple.Create(_leaderBoardData.Count, _leaderBoardData.GetRange(offset, count)));
        }

        public Task<List<LeaderBoardEntry>> GetEntries()
        {
            return Task.FromResult(_leaderBoardData);
        }

        public Task Initialize(string name, DateTime startAt, DateTime EndTime, bool acceptBetterScoresOnly)
        {
            if (string.IsNullOrWhiteSpace(name))
                throw new ArgumentException("Name can not be null or whitespace"); State.start = startAt;
            State.end = EndTime;
            State.AcceptBetterScoresOnly = acceptBetterScoresOnly;
            //State.IsCreated = true;
            State.Name = name;
            //_IsCreated = true;
            return state.WriteStateAsync();
        }

        public Task<string> GetKey()
        {
            return Task.FromResult(this.GetPrimaryKeyString());
        }

        public Task<string> GetName()
        {
            return Task.FromResult(State.Name);
        }

        private void AssertIsCreated()
        {
            //if (!_IsCreated)
            //    throw new InvalidOperationException($"This leaderboard with id {this.GetPrimaryKeyString()} is invalid and is not created");
        }

        public Task SetRewards(string[] items, int[] itemCounts)
        {
            List<LeaderBoardReward> rewards = new List<LeaderBoardReward>(itemCounts.Length);
            for (int i = 0; i < itemCounts.Length; ++i)
            {
                rewards.Add(new LeaderBoardReward { Item = items[i], Count = itemCounts[i] });
            }
            State.rewards = rewards;
            return state.WriteStateAsync();

        }

        public Task<(string, int)[]> GetRewards()
        {
            // The interface keeps the legacy tuple array; state stores LeaderBoardReward entries.
            return Task.FromResult(State.rewards?.Select(x => (x.Item, x.Count)).ToArray());
        }

        public Task<LeaderBoardInfo> GetInfo()
        {
            return Task.FromResult(new LeaderBoardInfo
            {
                Name = State.Name,
                AcceptBetterScoresOnly = State.AcceptBetterScoresOnly
            });
        }
    }
}