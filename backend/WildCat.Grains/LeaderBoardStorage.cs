using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using WildCat.Interfaces;
using Orleans;

namespace WildCat.Grains
{
    public class LeaderBoardStorage : Grain<LeaderBoardStorageState>, IleaderBoardStorage
    {
        public Task<List<LeaderBoardEntry>> GetEntries()
        {
            return Task.FromResult(State.entries);
        }

        public async Task WriteEntries(List<LeaderBoardEntry> data)
        {
            State.entries = data;
            await WriteStateAsync();
        }
        
        // public async Task AddPlayerScore(IPlayer player, int score)
        // {
        //     var indices = await FindPlayerIndex(player);
        //     var listID = indices.Item1;
        //     var index = indices.Item2;
        //     bool isSet = false;
        //     if (index < 0)
        //     {
        //         var name = await player.GetPlayerName();
        //         var data = await GrainFactory.GetGrain<IleaderBoardStorage>(State.StorageGrainKeys[listID]).GetEntries();
        //         if (data.Count < State.MaxListLength)
        //         {
        //             await GrainFactory.GetGrain<IleaderBoardStorage>(State.StorageGrainKeys[listID])
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
        //         await WriteStateAsync();
        //     }
        // }
        
    }
}