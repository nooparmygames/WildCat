using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Orleans;

namespace WildCat.Interfaces
{
    public interface ICustomMatchMaker : IGrainWithGuidCompoundKey
    {
        /// <summary>
        /// Finds a match for the player
        /// </summary>
        /// <param name="playerID">The player we want to find match for</param>
        /// <param name="maximumWaitTime">Maximum amount of time before declaring a failure</param>
        /// <returns>The relay game that was created, or an empty id in case of failure.</returns>
        Task<FoundRelayMatchResult> FindMatch(IPlayer playerID, uint skill, uint maxWaitTime,
            Guid loginTicket = default, CancellationToken cancellationToken = default);
        Task<bool> RemoveDisconnectedPlayer(IPlayer player);
        Task<int> GetOnlinePlayersCount();

    }

    [GenerateSerializer]
    [Alias("wildcat.FoundRelayMatchResult")]
    public class FoundRelayMatchResult
    {
        [Id(0)] public IRelayServerGame game { get; set; }
        

        public FoundRelayMatchResult(IRelayServerGame game)
        {
            this.game = game;
        }
    }
}
