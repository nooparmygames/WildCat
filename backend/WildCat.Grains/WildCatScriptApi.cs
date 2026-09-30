using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Threading.Tasks;
using System.Threading;
using WildCat.Interfaces;
using Orleans;
using Newtonsoft.Json;
using WildCat.Protocol;

namespace WildCat.Scripting
{
    /// <summary>
    /// The server API a script event sees as the global <c>WildCat</c>. A script runs on behalf of
    /// one player, <see cref="CurrentPlayer"/>, in the game and session of the client that called
    /// the event, and the API is scoped accordingly:
    /// <list type="bullet">
    /// <item>private player data can only be read and written for <see cref="CurrentPlayer"/>;</item>
    /// <item>other players' public data is read only (<see cref="GetPublicPlayerData"/>);</item>
    /// <item>leaderboard scores, inventory grants and publisher data are the trusted server path
    /// (clients cannot do these themselves) and always act on the session's game and developer.</item>
    /// </list>
    /// The event's <c>args</c> come straight from the game client (ExecuteScriptEvent) and are
    /// untrusted: a script must decide amounts, scores and rewards itself, or at least clamp them,
    /// before passing them to the calls below.
    ///
    /// Every call touches the session, checks that it still belongs to this player and game, runs
    /// its grain calls on the calling grain's scheduler, and is cancelled with the script when the
    /// script's time limit passes.
    /// </summary>
    public sealed class WildCatScriptApi
    {
        /// <summary>
        /// Largest count one call may grant or set for a single item.
        /// </summary>
        public const int MaxItemCount = 1_000_000;

        private const int MaxJsonDepth = 64;

        private static readonly JsonSerializerSettings jsonSettings = new JsonSerializerSettings
        {
            MaxDepth = MaxJsonDepth,
            TypeNameHandling = TypeNameHandling.None,
            ReferenceLoopHandling = ReferenceLoopHandling.Error
        };

        private readonly IGrainFactory grainFactory;
        private readonly ISession session;
        private readonly Guid gameKey;
        private readonly TaskScheduler scheduler;
        private readonly ScriptBudget budget;

        /// <summary>
        /// The account id of the player the script runs for.
        /// </summary>
        public readonly string CurrentPlayer;

        internal WildCatScriptApi(IGrainFactory factory, ISession session, Guid gameKey, string playerId,
            TaskScheduler orleansScheduler, ScriptBudget budget)
        {
            this.grainFactory = factory;
            this.session = session;
            this.gameKey = gameKey;
            this.CurrentPlayer = playerId;
            this.scheduler = orleansScheduler;
            this.budget = budget;
        }

        /// <summary>
        /// Gets a private cloud data value of the current player in this game, or null.
        /// </summary>
        public Task<string> GetPlayerData(string key)
        {
            return CallAsync(async _ => (await Storage(CurrentPlayer).GetPlayerString(key)).Item2);
        }

        /// <summary>
        /// Same as <see cref="GetPlayerData(string)"/>; kept for scripts that pass
        /// <see cref="CurrentPlayer"/>. Any other player id throws UnauthorizedAccessException.
        /// </summary>
        public Task<string> GetPlayerData(string player, string key)
        {
            EnsureCurrentPlayer(player, nameof(GetPlayerData));
            return GetPlayerData(key);
        }

        /// <summary>
        /// Stores a private cloud data value for the current player in this game.
        /// </summary>
        public Task SetPlayerData(string key, string value)
        {
            return CallAsync(_ => Storage(CurrentPlayer).SetPlayerString(key, value));
        }

        /// <summary>
        /// Same as <see cref="SetPlayerData(string, string)"/>; kept for scripts that pass
        /// <see cref="CurrentPlayer"/>. Any other player id throws UnauthorizedAccessException.
        /// </summary>
        public Task SetPlayerData(string player, string key, string value)
        {
            EnsureCurrentPlayer(player, nameof(SetPlayerData));
            return SetPlayerData(key, value);
        }

        /// <summary>
        /// Reads a public cloud data value of any player in this game, or null. Read only.
        /// </summary>
        public Task<string> GetPublicPlayerData(string player, string key)
        {
            ValidatePlayerId(player);
            return CallAsync(async _ => (await Storage(player).GetPlayerPublicString(key)).Item2);
        }

        /// <summary>
        /// Stores a public cloud data value for the current player in this game.
        /// </summary>
        public Task SetPublicPlayerData(string key, string value)
        {
            return CallAsync(_ => Storage(CurrentPlayer).SetPlayerPublicString(key, value));
        }

        /// <summary>
        /// The server's time, in UTC.
        /// </summary>
        public Task<DateTime> GetTime()
        {
            return CallAsync(_ => grainFactory.GetGrain<ITimeService>(Guid.Empty).GetServerTime());
        }

        /// <summary>
        /// Sets a player's score in a leaderboard of this game. Any player of the game may be
        /// given a score (for example both sides of a match); the score should come from the
        /// script's own logic, not straight from the client's arguments.
        /// </summary>
        public Task SetScoreInLeaderBoard(string player, string leaderBoard, int score)
        {
            ValidatePlayerId(player);
            return CallAsync(_ => LeaderBoard(leaderBoard).AddPlayerScore(grainFactory.GetGrain<IPlayer>(player), score));
        }

        /// <summary>
        /// Grants items to a player's inventory in the current game. Game clients cannot grant
        /// items themselves, so rewards, purchases and drops go through a script event that calls
        /// this; decide the counts in the script rather than taking them from the client.
        /// Items are named as in the game's item catalog.
        /// </summary>
        /// <param name="player">Account id of the player.</param>
        /// <param name="inventory">Inventory name; empty or null for the main inventory.</param>
        /// <param name="items">Item names.</param>
        /// <param name="counts">How many of each item to add: one count per item, from 1 to
        /// <see cref="MaxItemCount"/>. A stack that would pass int.MaxValue fails the whole call.</param>
        /// <returns>False when the inventory has no room; nothing is added then.</returns>
        public Task<bool> AddInventoryItems(string player, string inventory, string[] items, int[] counts)
        {
            ValidateInventoryChange(player, items, counts, allowZero: false);
            return CallAsync(_ =>
            {
                var (inventoryGrain, itemGrains) = GetInventoryAndItems(player, inventory, items);
                return inventoryGrain.AddItem(itemGrains, counts);
            });
        }

        /// <summary>
        /// Sets the counts of items in a player's inventory in the current game, adding items that
        /// are not there yet. Same parameters as <see cref="AddInventoryItems"/>, except that a
        /// count may be zero.
        /// </summary>
        public Task SetInventoryItems(string player, string inventory, string[] items, int[] counts)
        {
            ValidateInventoryChange(player, items, counts, allowZero: true);
            return CallAsync(_ =>
            {
                var (inventoryGrain, itemGrains) = GetInventoryAndItems(player, inventory, items);
                return inventoryGrain.SetItem(itemGrains, counts);
            });
        }

        private static void ValidateInventoryChange(string player, string[] items, int[] counts, bool allowZero)
        {
            ValidatePlayerId(player);
            if (items == null || counts == null || items.Length != counts.Length)
                throw new ArgumentException("Every item needs exactly one count.", nameof(counts));
            for (int i = 0; i < items.Length; ++i)
            {
                if (string.IsNullOrEmpty(items[i]))
                    throw new ArgumentException("Item names cannot be empty.", nameof(items));
                if (counts[i] < 0 || (counts[i] == 0 && !allowZero) || counts[i] > MaxItemCount)
                {
                    throw new ArgumentException(
                        $"Invalid count {counts[i]} for item {items[i]}; counts go from {(allowZero ? 0 : 1)} to {MaxItemCount}.", nameof(counts));
                }
            }
        }

        private (IPlayerInventory, IInventoryItem[]) GetInventoryAndItems(string player, string inventory, string[] items)
        {
            string game = gameKey.ToString();
            var inventoryGrain = grainFactory.GetGrain<IPlayerInventory>(ResourcesUtility.GetPlayerInventoryKey(game, player, inventory));
            var itemGrains = items
                .Select(name => grainFactory.GetGrain<IInventoryItem>(ResourcesUtility.GetItemKey(game, name), ResourcesUtility.GetGameItemsClassName()))
                .ToArray();
            return (inventoryGrain, itemGrains);
        }

        /// <summary>
        /// Get top entries in the leaderboard
        /// </summary>
        public Task<GetLeaderBoardResponse> GetTopEntriesInLeaderBoard(string leaderBoard, int offset, int count)
        {
            count = SanitizeCount(count);
            return CallAsync(async _ =>
            {
                var topEntries = await LeaderBoard(leaderBoard).GetTopEntries(count, offset);
                GetLeaderBoardResponse resp = new GetLeaderBoardResponse { type = GetLeaderBoardRequest.RequestType.Top };
                resp.count = topEntries.Item1;
                FillReturningLeaderBoardEntries(resp, topEntries.Item2, true, 0, "", offset);
                return resp;
            });
        }

        /// <summary>
        /// Get LeaderBoard entries around a player
        /// </summary>
        public Task<GetLeaderBoardResponse> GetAroundMeEntriesInLeaderBoard(string player, string leaderBoard, int count)
        {
            ValidatePlayerId(player);
            count = SanitizeCount(count);
            return CallAsync(async _ =>
            {
                IPlayer playerGrain = grainFactory.GetGrain<IPlayer>(player);
                var aroundMeEntries = await LeaderBoard(leaderBoard).GetAroundMe(playerGrain, count);
                GetLeaderBoardResponse resp = new GetLeaderBoardResponse { type = GetLeaderBoardRequest.RequestType.Around };
                resp.playerRank = aroundMeEntries.Item1;
                FillReturningLeaderBoardEntries(resp, aroundMeEntries.Item2, false, aroundMeEntries.Item1, player, 0);
                return resp;
            });
        }

        /// <summary>
        /// Gets a player's rank in a specific leaderboard
        /// </summary>
        public Task<int> GetPlayerRankInLeaderBoard(string player, string leaderBoard)
        {
            ValidatePlayerId(player);
            return CallAsync(_ => LeaderBoard(leaderBoard).GetPlayerRank(grainFactory.GetGrain<IPlayer>(player)));
        }

        private static void FillReturningLeaderBoardEntries(GetLeaderBoardResponse responseToSend, List<WildCat.Interfaces.LeaderBoardEntry> entries,bool isTop,int playerRank,string playerId,int offset)
        {
            for(int i=0;i<entries.Count;++i)
            {
                int index = 0;
                if(!isTop)
                {
                    index = entries.FindIndex(x=>x.PlayerID == playerId);
                }
                var item = entries[i];
                var current = new WildCat.Protocol.LeaderBoardEntry
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

        private static int SanitizeCount(int count)
        {
            if (count > 20 || count < 1)
                count = 20;
            return count;
        }

        /// <summary>
        /// Gets a publisher data value of the session's developer.
        /// </summary>
        public Task<string> GetPublisherData(string key)
        {
            return CallAsync(info => grainFactory.GetGrain<IDeveloperStorage>(info.DeveloperKey).GetString(key));
        }

        /// <summary>
        /// Stores a publisher data value for the session's developer.
        /// </summary>
        public Task SetPublisherData(string key, string value)
        {
            return CallAsync(async info =>
            {
                var developerData = await grainFactory.GetGrain<IDeveloper>(info.DeveloperKey).GetDeveloperData();
                await grainFactory.GetGrain<IDeveloperStorage>(info.DeveloperKey).SetString(key, value, developerData.DeveloperSecret);
            });
        }

        /// <summary>
        /// Deserializes an object from JSON using JSON.NET and returns the object. Type names in
        /// the JSON are ignored and nesting is limited to 64 levels.
        /// </summary>
        public T DeserializeObjectFromJson<T>(string json)
        {
            return JsonConvert.DeserializeObject<T>(json, jsonSettings);
        }

        /// <summary>
        /// Serializes an object to JSON using JSON.NET library and returns the JSON data. Nesting
        /// is limited to 64 levels.
        /// </summary>
        public string SerializeObjectToJson<T>(T obj)
        {
            var serializer = JsonSerializer.Create(jsonSettings);
            using var text = new StringWriter(CultureInfo.InvariantCulture);
            using (var writer = new DepthLimitedJsonWriter(text, MaxJsonDepth))
                serializer.Serialize(writer, obj);
            return text.ToString();
        }

        private IPlayerStorage Storage(string player)
        {
            return grainFactory.GetGrain<IPlayerStorage>(ResourcesUtility.GetPlayPlayerStorageKey(gameKey.ToString(), player));
        }

        private ILeaderBoard LeaderBoard(string leaderBoard)
        {
            return grainFactory.GetGrain<ILeaderBoard>(ResourcesUtility.GetLeaderBoardKey(gameKey.ToString(), leaderBoard));
        }

        private void EnsureCurrentPlayer(string player, string operation)
        {
            if (!string.Equals(player, CurrentPlayer, StringComparison.Ordinal))
            {
                throw new UnauthorizedAccessException(
                    $"{operation}: a script can only use the private data of the player it runs for (WildCat.CurrentPlayer). " +
                    "Use GetPublicPlayerData to read another player's public data.");
            }
        }

        /// <summary>
        /// Player ids are account ids; grain keys are built by joining ids with underscores, so an
        /// id containing one could reach another grain.
        /// </summary>
        private static void ValidatePlayerId(string player)
        {
            if (string.IsNullOrEmpty(player))
                throw new ArgumentException("A player id is required.", nameof(player));
            if (player.Contains('_'))
                throw new ArgumentException("Player ids cannot contain underscores.", nameof(player));
        }

        private Task CallAsync(Func<SessionGameInfo, Task> call)
        {
            return CallAsync<bool>(async info =>
            {
                await call(info);
                return true;
            });
        }

        /// <summary>
        /// Runs <paramref name="call"/> on the grain scheduler after checking the session, and
        /// gives up when the script's time limit passes.
        /// </summary>
        private async Task<T> CallAsync<T>(Func<SessionGameInfo, Task<T>> call)
        {
            budget.ThrowIfExpired();
            var work = Task.Factory.StartNew(async () =>
            {
                var info = await GetSessionInfo();
                return await call(info);
            }, budget.Token, TaskCreationOptions.None, scheduler).Unwrap();
            try
            {
                return await work.WaitAsync(budget.Token);
            }
            catch (OperationCanceledException) when (budget.IsExpired)
            {
                throw budget.CreateTimeoutException();
            }
        }

        private async Task<SessionGameInfo> GetSessionInfo()
        {
            if (session == null)
                throw new InvalidOperationException("This script run has no session, so it cannot use the WildCat API.");
            var info = await session.TouchAndGetInfo();
            if (info.GameKey != gameKey)
                throw new UnauthorizedAccessException("The session belongs to a different game than this script event.");
            if (!string.Equals(info.UserID, CurrentPlayer, StringComparison.Ordinal))
                throw new UnauthorizedAccessException("The session belongs to a different player than the one this script runs for.");
            return info;
        }

        /// <summary>
        /// Stops serialization of deeply nested objects before JSON.NET's recursion can exhaust the
        /// stack (JSON.NET's MaxDepth only applies to reading).
        /// </summary>
        private sealed class DepthLimitedJsonWriter : JsonTextWriter
        {
            private readonly int maxDepth;

            public DepthLimitedJsonWriter(TextWriter writer, int maxDepth) : base(writer)
            {
                this.maxDepth = maxDepth;
            }

            public override void WriteStartObject()
            {
                EnsureDepth();
                base.WriteStartObject();
            }

            public override void WriteStartArray()
            {
                EnsureDepth();
                base.WriteStartArray();
            }

            public override void WriteStartConstructor(string name)
            {
                EnsureDepth();
                base.WriteStartConstructor(name);
            }

            private void EnsureDepth()
            {
                if (Top >= maxDepth)
                    throw new JsonSerializationException($"The object is nested deeper than {maxDepth} levels.");
            }
        }
    }
}
