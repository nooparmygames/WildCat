using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Orleans;

namespace Interfaces
{
    public interface ICustomMatchMaker : IGrainWithGuidCompoundKey
    {
        /// <summary>
        /// Finds a match for the player
        /// </summary>
        /// <param name="playerID">The player we want to find match for</param>
        /// <param name="maximumWaitTime">Maximum amount of time before declaring a failure</param>
        /// <returns>Guid of a iGame or empty in case of failure.</returns>
        Task<FoundRelayMatchResult> FindMatch(IPlayer playerID, uint skill,uint maxWaitTime);
        Task<bool> RemoveDisconnectedPlayer(IPlayer player);
        Task<int> GetOnlinePlayersCount();

    }

    public class FoundRelayMatchResult
    {
        public IRelayServerGame game { get; set; }
        

        public FoundRelayMatchResult(IRelayServerGame game)
        {
            this.game = game;
        }
    }
}
