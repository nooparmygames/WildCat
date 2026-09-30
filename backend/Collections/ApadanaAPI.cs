using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using Orleans;
using Interfaces;

namespace Collections
{
    /// <summary>
    /// This class is passed to scripts to access Apadana APIs
    /// </summary>
    public class ApadanaAPI
    {
        private readonly IGrainFactory grainFactory;
        private readonly ISession session;

        /// <summary>
        /// The player which we are running the script for
        /// </summary>
        public readonly string CurrentPlayer;

        public ApadanaAPI(IGrainFactory factory, ISession session,string playerId)
        {

            this.grainFactory = factory;
            this.session = session;
            this.CurrentPlayer = playerId;
        }

        public async Task<string> GetPlayerData(string player, string key)
        {
            await session.AddUserCall();
            Tuple<bool,string> value = await grainFactory.GetGrain<IPlayer>(player).GetPlayerString(key);
            return value.Item2;
        }

        public async Task SetPlayerData(string player, string key, string value)
        {
            await session.AddUserCall();
            await grainFactory.GetGrain<IPlayer>(player).SetPlayerString(key, value);
        }

        public async Task<DateTime> GetTime()
        {
            await session.AddUserCall();
            var time = await grainFactory.GetGrain<ITimeService>(Guid.Empty).GetServerTime();
            //TODO fix the interface to return date and time
            return DateTime.Parse(time);
        }

        public async Task SetScoreInLeaderBoard(string player, string leaderBoard, int score)
        {
            var sessionInfo = await session.AddUserCallCheckSessionAndGetInfo();
            IPlayer playerGrain = grainFactory.GetGrain<IPlayer>(player);
            await grainFactory.GetGrain<ILeaderBoard>(ResourcesUtility.GetLeaderBoardKey(sessionInfo.GameKey.ToString(), leaderBoard)).AddPlayerScore(playerGrain, score);
        }

        public async Task<List<LeaderBoardEntry>> GetTopEntriesInLeaderBoard(string leaderBoard, int offset,int count)
        {
            count = SanitizeCount(count);
            var sessionInfo = await session.AddUserCallCheckSessionAndGetInfo();
            var leaderBoardGrain = grainFactory.GetGrain<ILeaderBoard>(ResourcesUtility.GetLeaderBoardKey(sessionInfo.GameKey.ToString(), leaderBoard));
            var topEntries = await leaderBoardGrain.GetTopEntries(count, offset);
            return topEntries.Item2;
        }
        
        public async Task<List<LeaderBoardEntry>> GetAroundMeEntriesInLeaderBoard(string player,string leaderBoard, int count)
        {
            count = SanitizeCount(count);
            var sessionInfo = await session.AddUserCallCheckSessionAndGetInfo();
            var leaderBoardGrain = grainFactory.GetGrain<ILeaderBoard>(ResourcesUtility.GetLeaderBoardKey(sessionInfo.GameKey.ToString(), leaderBoard));
            IPlayer playerGrain = grainFactory.GetGrain<IPlayer>(player);
            var topEntries = await leaderBoardGrain.GetAroundMe(playerGrain, count);
            return topEntries.Item2;
        }

        private int SanitizeCount(int count)
        {
            if (count > 20 || count < 1)
                count = 20;
            return count;
        }

        public async Task FinishGame(string player,Guid gameKey)
        {
            var sessionInfo = await session.AddUserCallCheckSessionAndGetInfo();
            var game = grainFactory.GetGrain<IRelayServerGame>(gameKey, sessionInfo.GameKey.ToString(), null);
            IPlayer playerGrain = grainFactory.GetGrain<IPlayer>(player);
            await game.PlayerFinished(playerGrain);
        }

        public async Task<string> GetPublisherData(string key)
        {
            var sessionInfo = await session.AddUserCallCheckSessionAndGetInfo();
            var readValue = await grainFactory.GetGrain<IDeveloperStorage>(sessionInfo.DeveloperKey).GetString(key);
            return readValue;
        }
    }
}
