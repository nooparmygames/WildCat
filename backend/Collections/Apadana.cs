using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using System.Threading;
using Interfaces;
using Orleans;
using Newtonsoft.Json;
using RealtimeServer;

namespace ScriptingAPI
{
    /// <summary>
    /// This class is passed to scripts to access Apadana APIs
    /// </summary>
    public class Apadana
    {
        private readonly IGrainFactory grainFactory;
        private readonly ISession session;
        private TaskScheduler scheduler;

        /// <summary>
        /// The player which we are running the script for
        /// </summary>
        public readonly string CurrentPlayer;

        public Apadana(IGrainFactory factory, ISession session, string playerId, TaskScheduler orleansScheduler)
        {
            this.grainFactory = factory;
            this.session = session;
            this.CurrentPlayer = playerId;
            this.scheduler = orleansScheduler;
        }

        /// <summary>
        /// Gets a players cloud data
        /// </summary>
        /// <param name="player"></param>
        /// <param name="key"></param>
        /// <returns></returns>
        public async Task<string> GetPlayerData(string player, string key)
        {
            return await (await Task.Factory.StartNew(async () =>
            {
                await session.AddUserCall();
                return (await grainFactory.GetGrain<IPlayer>(player).GetPlayerString(key)).Item2;
            }, CancellationToken.None, TaskCreationOptions.None, scheduler));
        }

        /// <summary>
        /// Stores cloud data for a player
        /// </summary>
        /// <param name="player"></param>
        /// <param name="key"></param>
        /// <param name="value"></param>
        /// <returns></returns>
        public async Task SetPlayerData(string player, string key, string value)
        {
            await (await Task.Factory.StartNew(async () =>
            {
                await session.AddUserCall();
                return grainFactory.GetGrain<IPlayer>(player).SetPlayerString(key, value);
            }, CancellationToken.None, TaskCreationOptions.None, scheduler));
        }

        /// <summary>
        /// Gets server time
        /// </summary>
        /// <returns></returns>
        public async Task<DateTime> GetTime()
        {
            return await (await Task.Factory.StartNew(async () =>
            {
                await session.AddUserCall();
                var time = await grainFactory.GetGrain<ITimeService>(Guid.Empty).GetServerTime();
                //TODO fix the interface to return date and time
                return DateTime.Parse(time);
            },CancellationToken.None,TaskCreationOptions.None,scheduler));
        }
        
        /// <summary>
        /// Sets a player's score in leaderboard
        /// </summary>
        /// <param name="player"></param>
        /// <param name="leaderBoard"></param>
        /// <param name="score"></param>
        /// <returns></returns>
        public async Task SetScoreInLeaderBoard(string player, string leaderBoard, int score)
        {
            await (await Task.Factory.StartNew(async () =>
            {
                var sessionInfo = await session.AddUserCallCheckSessionAndGetInfo();
                IPlayer playerGrain = grainFactory.GetGrain<IPlayer>(player);
                return grainFactory.GetGrain<ILeaderBoard>(ResourcesUtility.GetLeaderBoardKey(sessionInfo.GameKey.ToString(), leaderBoard)).AddPlayerScore(playerGrain, score);
            },CancellationToken.None,TaskCreationOptions.None,scheduler));
        }

        /// <summary>
        /// Get top entries in the leaderboard
        /// </summary>
        /// <param name="leaderBoard"></param>
        /// <param name="offset"></param>
        /// <param name="count"></param>
        /// <returns></returns>
        public async Task<GetLeaderBoardResponse> GetTopEntriesInLeaderBoard(string leaderBoard, int offset, int count)
        {
            return await (await Task.Factory.StartNew(async () =>
            {
                count = SanitizeCount(count);
                var sessionInfo = await session.AddUserCallCheckSessionAndGetInfo();
                var leaderBoardGrain = grainFactory.GetGrain<ILeaderBoard>(ResourcesUtility.GetLeaderBoardKey(sessionInfo.GameKey.ToString(), leaderBoard));
                var topEntries = await leaderBoardGrain.GetTopEntries(count, offset);
                GetLeaderBoardResponse resp = new GetLeaderBoardResponse();
                resp.count = topEntries.Item1;
                FillReturningLeaderBoardEntries(resp, topEntries.Item2,true,0,"",offset);
                return resp;
            },CancellationToken.None,TaskCreationOptions.None,scheduler));
        }

        /// <summary>
        /// Get LeaderBoard entries around a player
        /// </summary>
        /// <param name="player"></param>
        /// <param name="leaderBoard"></param>
        /// <param name="count"></param>
        /// <returns></returns>
        public async Task<GetLeaderBoardResponse> GetAroundMeEntriesInLeaderBoard(string player, string leaderBoard, int count)
        {
            return await (await Task.Factory.StartNew(async () =>
           {
               count = SanitizeCount(count);
               var sessionInfo = await session.AddUserCallCheckSessionAndGetInfo();
               var leaderBoardGrain = grainFactory.GetGrain<ILeaderBoard>(ResourcesUtility.GetLeaderBoardKey(sessionInfo.GameKey.ToString(), leaderBoard));
               IPlayer playerGrain = grainFactory.GetGrain<IPlayer>(player);
               var aroundMeEntries = await leaderBoardGrain.GetAroundMe(playerGrain, count);
               GetLeaderBoardResponse resp = new GetLeaderBoardResponse();
               resp.playerRank = aroundMeEntries.Item1;
               FillReturningLeaderBoardEntries(resp, aroundMeEntries.Item2,false,aroundMeEntries.Item1,playerGrain.GetPrimaryKeyString(),0);
               return resp;
           }, CancellationToken.None, TaskCreationOptions.None, scheduler));
        }

        /// <summary>
        /// Gets a player's rank in a specific leaderboard
        /// </summary>
        /// <param name="player"></param>
        /// <param name="leaderBoard"></param>
        /// <returns></returns>
        public async Task<int> GetPlayerRankInLeaderBoard(string player, string leaderBoard)
        {
            return await (await Task.Factory.StartNew(async () =>
            {
                
                var sessionInfo = await session.AddUserCallCheckSessionAndGetInfo();
                var leaderBoardGrain = grainFactory.GetGrain<ILeaderBoard>(ResourcesUtility.GetLeaderBoardKey(sessionInfo.GameKey.ToString(), leaderBoard));
                IPlayer playerGrain = grainFactory.GetGrain<IPlayer>(player);
                var rank = await leaderBoardGrain.GetPlayerRank(playerGrain);
                return rank;
            }, CancellationToken.None, TaskCreationOptions.None, scheduler));
        }

        private static void FillReturningLeaderBoardEntries(GetLeaderBoardResponse responseToSend, List<Interfaces.LeaderBoardEntry> entries,bool isTop,int playerRank,string playerId,int offset)
        {
            for(int i=0;i<entries.Count;++i)
            {
                int index = 0;
                if(!isTop)
                {
                    index = entries.FindIndex(x=>x.PlayerID == playerId);
                }
                var item = entries[i];
                var current = new RealtimeServer.LeaderBoardEntry
                {
                    score = item.Score,
                    userName = item.Name,
                    playerId = item.PlayerID,
                    entryTime = item.EntryTime,

                };
                if (isTop)
                {
                    current.Rank = offset + i + 1;
                }
                else
                {
                    current.Rank = playerRank + (i - index);
                }
                responseToSend.entries.Add(current);
            }
        }

        private int SanitizeCount(int count)
        {
            if (count > 20 || count < 1)
                count = 20;
            return count;
        }

        /// <summary>
        /// Gets a publisher data value
        /// </summary>
        /// <param name="key"></param>
        /// <returns></returns>
        public async Task<string> GetPublisherData(string key)
        {
            return await (await Task.Factory.StartNew(async () =>
            {
                var sessionInfo = await session.AddUserCallCheckSessionAndGetInfo();
                var readValue = await grainFactory.GetGrain<IDeveloperStorage>(sessionInfo.DeveloperKey).GetString(key);
                return readValue;
            }, CancellationToken.None, TaskCreationOptions.None, scheduler));
        }

        public async Task SetPublisherData(string key, string value)
        {
            await (await Task.Factory.StartNew(async () =>
            {
                var sessionInfo = await session.AddUserCallCheckSessionAndGetInfo();
                var developerData = await grainFactory.GetGrain<IDeveloper>(sessionInfo.DeveloperKey).GetDeveloperData();
                await grainFactory.GetGrain<IDeveloperStorage>(sessionInfo.DeveloperKey).SetString(key, value, developerData.DeveloperSecret);
            }, CancellationToken.None, TaskCreationOptions.None, scheduler));
        }

        /// <summary>
        /// Deserializes an object from JSON using JSON.NET and returns the object
        /// </summary>
        /// <typeparam name="T"></typeparam>
        /// <param name="json"></param>
        /// <returns></returns>
        public T DeserializeObjectFromJson<T>(string json)
        {
            return JsonConvert.DeserializeObject<T>(json);
        }

        /// <summary>
        /// Serializes an object to JSON using JSON.NET library and returns the JSON data
        /// </summary>
        /// <typeparam name="T"></typeparam>
        /// <param name="obj"></param>
        /// <returns></returns>
        public string SerializeObjectToJson<T>(T obj)
        {
            return JsonConvert.SerializeObject(obj);
        }
    }
    
}
