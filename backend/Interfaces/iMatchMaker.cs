using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Orleans;

namespace Interfaces
{
    public interface IMatchMaker :IGrainWithStringKey
    {
        /// <summary>
        /// Finds a match for the player
        /// </summary>
        /// <param name="playerID">The player we want to find match for</param>
        /// <param name="maximumWaitTime">Maximum amount of time before declaring a failure</param>
        /// <returns>Guid of a iGame or empty in case of failure.</returns>
        Task<FoundMatchResult> FindMatch(IPlayer playerID, int maximumWaitTime);
        Task<bool> RemoveDisconnectedPlayer(IPlayer player);
        Task<int> GetOnlinePlayersCount();
        
    }

    [GenerateSerializer]
    [Alias("wildcat.FoundMatchResult")]
    public class FoundMatchResult
    {
        [Id(0)] public IGame game { get; set; }
        [Id(1)] public string otherPlayerName { get; set; }

        public FoundMatchResult(IGame game,string name)
        {
            this.game = game;
            this.otherPlayerName = name;
        }
    }
}
