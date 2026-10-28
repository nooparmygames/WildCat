using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using WildCat.Interfaces;
using Orleans;
using Orleans.Runtime;

namespace WildCat.Grains
{
    public class LeaderBoardStorage : Grain, ILeaderBoardStorage
    {
        private readonly IPersistentState<LeaderBoardStorageState> state;

        public LeaderBoardStorage([PersistentState(GrainStateNames.LeaderBoardStorage, GrainStorageNames.Default)] IPersistentState<LeaderBoardStorageState> state)
        {
            this.state = state;
        }

        private LeaderBoardStorageState State => state.State;

        public Task<List<LeaderBoardEntry>> GetEntries()
        {
            return Task.FromResult(State.entries);
        }

        public async Task WriteEntries(List<LeaderBoardEntry> data)
        {
            State.entries = data;
            await state.WriteStateAsync();
        }

        public Task ClearEntries() => state.ClearStateAsync();
        
        // public async Task AddPlayerScore(IPlayer player, int score)
        // {
        //     var indices = await FindPlayerIndex(player);
        //     var listID = indices.Item1;
        //     var index = indices.Item2;
        //     bool isSet = false;
        //     if (index < 0)
        //     {
        //         var name = await player.GetPlayerName();
        //         var data = await GrainFactory.GetGrain<ILeaderBoardStorage>(State.StorageGrainKeys[listID]).GetEntries();
        //         if (data.Count < State.MaxListLength)
        //         {
        //             await GrainFactory.GetGrain<ILeaderBoardStorage>(State.StorageGrainKeys[listID])
        //                 .AddPlayerScore(player, score);
        //         }
        //         State.entries.Add(new LeaderBoardEntry(player.GetPrimaryKeyString(), name, score, DateTime.Now));
        //         isSet = true;
        //     }
        //     else if (State.entries[index].Score < score || !State.AcceptBetterScoresOnly)
        //     {
        //         var name = await player.GetPlayerName();
        //         State.entries[index] = new LeaderBoardEntry(player.GetPrimaryKeyString(), name, score, DateTime.Now);
        //         isSet = true;
        //     }
        //     if (isSet)
        //     {
        //         State.entries.Sort((x, y) => y.Score - x.Score);
        //         await state.WriteStateAsync();
        //     }
        // }
        
    }
}
