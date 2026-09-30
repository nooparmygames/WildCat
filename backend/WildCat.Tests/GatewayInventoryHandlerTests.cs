using Orleans;
using Orleans.TestingHost;
using WildCat.Gateway;
using WildCat.Interfaces;
using WildCat.Protocol;
using Xunit;

namespace WildCat.Tests
{
    /// <summary>
    /// The inventory handlers over real WebSockets: GetInventory (109), RemoveInventoryItem (115),
    /// UnlockInventory (119) and GetInventoryUnlockTime (121), plus the grant requests clients may
    /// not send (111, 113). Items are granted directly through the inventory grain, as a server
    /// script would.
    /// </summary>
    [Collection(ClusterCollection.Name)]
    public class GatewayInventoryHandlerTests
    {
        private readonly InProcessTestCluster cluster;
        private IGrainFactory GrainFactory => cluster.Client;

        public GatewayInventoryHandlerTests(ClusterFixture fixture)
        {
            cluster = fixture.Cluster;
        }

        private async Task<IItem> CreateItem(Guid gameKey, string name)
        {
            var item = GrainFactory.GetGrain<IItem>(ResourcesUtility.GetItemKey(gameKey.ToString(), name));
            await item.Initialize(name, $"a {name}", null, 100000, true, false, TimeSpan.Zero, false, false, 0, TimeSpan.Zero, null);
            return item;
        }

        private IPlayerInventory Inventory(Guid gameKey, string playerId, string name = "")
        {
            return GrainFactory.GetGrain<IPlayerInventory>(ResourcesUtility.GetPlayerInventoryKey(gameKey.ToString(), playerId, name));
        }

        private static Task<OptionalResponse<GetInventoryResponseMessage>> GetInventory(GatewayPlayer player, string name = "")
        {
            return player.CallAsync<GetInventoryResponseMessage>(WildCatMsgType.GetInventoryRequest, new StringMessage { value = name },
                WildCatMsgType.GetInventoryResponse);
        }

        private static Task<OptionalResponse<GetInventoryResponseMessage>> Remove(GatewayPlayer player, string inventory,
            params (string, int)[] items)
        {
            return player.CallAsync<GetInventoryResponseMessage>(WildCatMsgType.RemoveInventoryItemRequest,
                new RemoveItemInventoryRequestMessage { inventoryName = inventory, items = items }, WildCatMsgType.RemoveInventoryItemResponse);
        }

        private static void AssertRefused<T>(OptionalResponse<T> response, string message) where T : MessageBase, new()
        {
            Assert.False(response.isSuccesful);
            Assert.Equal(GatewayErrors.Refused, response.errorCode);
            Assert.Equal(message, response.errorMsg);
        }

        private static (string, int)[] Sorted((string, int)[] items)
        {
            return items.OrderBy(x => x.Item1, StringComparer.Ordinal).ToArray();
        }

        [Fact]
        public async Task GetInventoryListsWhatThePlayerHolds()
        {
            var (developerKey, gameKey) = await TestSetup.CreateDeveloperAndGame(GrainFactory);
            await using var host = await GatewayTestHost.StartAsync(cluster.Client);
            await using var alice = await GatewayPlayer.ConnectAsync(host, developerKey, gameKey);

            var empty = await GetInventory(alice);
            Assert.True(empty.isSuccesful, empty.errorMsg);
            Assert.Empty(empty.msg.items);

            var gem = await CreateItem(gameKey, "gem");
            var coin = await CreateItem(gameKey, "coin");
            await Inventory(gameKey, alice.Id).AddItem(new IInventoryItem[] { gem, coin }, new[] { 3, 10 });
            await Inventory(gameKey, alice.Id, "bag").AddItem(new IInventoryItem[] { gem }, new[] { 1 });

            var main = await GetInventory(alice);
            Assert.True(main.isSuccesful, main.errorMsg);
            Assert.Equal(new[] { ("coin", 10), ("gem", 3) }, Sorted(main.msg.items));
            var bag = await GetInventory(alice, "bag");
            Assert.Equal(new[] { ("gem", 1) }, bag.msg.items);

            // Inventories belong to the game of the session: the same player in another game has none.
            var (otherDeveloper, otherGame) = await TestSetup.CreateDeveloperAndGame(GrainFactory);
            await using var aliceElsewhere = await GatewayPlayer.ConnectAsync(host, otherDeveloper, otherGame, alice.Device);
            Assert.Equal(alice.Id, aliceElsewhere.Id);
            Assert.Empty((await GetInventory(aliceElsewhere)).msg.items);
        }

        [Fact]
        public async Task RemovingItemsIsAllOrNothingAndNeedsThemHeld()
        {
            var (developerKey, gameKey) = await TestSetup.CreateDeveloperAndGame(GrainFactory);
            await using var host = await GatewayTestHost.StartAsync(cluster.Client);
            await using var alice = await GatewayPlayer.ConnectAsync(host, developerKey, gameKey);
            var gem = await CreateItem(gameKey, "gem");
            var coin = await CreateItem(gameKey, "coin");
            await Inventory(gameKey, alice.Id).AddItem(new IInventoryItem[] { gem, coin }, new[] { 3, 10 });

            AssertRefused(await Remove(alice, "", ("gem", 5)), RemoveInventoryItemHandler.NotHeld);
            AssertRefused(await Remove(alice, "", ("coin", 2), ("gem", 4)), RemoveInventoryItemHandler.NotHeld);
            AssertRefused(await Remove(alice, "", ("gem", -2)), RemoveInventoryItemHandler.NotHeld);
            AssertRefused(await Remove(alice, "", ("gem", 0)), RemoveInventoryItemHandler.NotHeld);
            AssertRefused(await Remove(alice, "", ("pearl", 1)), RemoveInventoryItemHandler.NotHeld);
            AssertRefused(await Remove(alice, "bag", ("gem", 1)), RemoveInventoryItemHandler.NotHeld);
            Assert.Equal(new[] { ("coin", 10), ("gem", 3) }, Sorted((await GetInventory(alice)).msg.items));

            var removed = await Remove(alice, "", ("gem", 2), ("coin", 10));
            Assert.True(removed.isSuccesful, removed.errorMsg);
            Assert.Equal(new[] { ("gem", 1) }, removed.msg.items);
            Assert.Equal(new[] { ("gem", 1) }, (await GetInventory(alice)).msg.items);
        }

        [Fact]
        public async Task AChestWithoutASizeUnlocksAtOnceIntoTheMainInventory()
        {
            var (developerKey, gameKey) = await TestSetup.CreateDeveloperAndGame(GrainFactory);
            await using var host = await GatewayTestHost.StartAsync(cluster.Client);
            await using var alice = await GatewayPlayer.ConnectAsync(host, developerKey, gameKey);
            var gem = await CreateItem(gameKey, "gem");
            await Inventory(gameKey, alice.Id).AddItem(new IInventoryItem[] { gem }, new[] { 1 });
            await Inventory(gameKey, alice.Id, "chest").AddItem(new IInventoryItem[] { gem }, new[] { 4 });

            var time = await alice.CallAsync<GetInventoryUnlockTimeResponseMessage>(WildCatMsgType.GetInventoryUnlockTimeRequest,
                new StringMessage { value = "chest" }, WildCatMsgType.GetInventoryUnlockTimeResponse);
            Assert.True(time.isSuccesful, time.errorMsg);
            Assert.Equal(TimeSpan.Zero, time.msg.time);

            var unlocked = await alice.CallAsync<GetInventoryResponseMessage>(WildCatMsgType.UnlockInventoryRequest,
                new StringMessage { value = "chest" }, WildCatMsgType.UnlockInventoryResponse);
            Assert.True(unlocked.isSuccesful, unlocked.errorMsg);
            Assert.Equal(new[] { ("gem", 4) }, unlocked.msg.items);

            Assert.Empty((await GetInventory(alice, "chest")).msg.items);
            Assert.Equal(new[] { ("gem", 5) }, (await GetInventory(alice)).msg.items);
        }

        [Fact]
        public async Task ATimedChestStaysLockedUntilItsTimeHasPassed()
        {
            var (developerKey, gameKey) = await TestSetup.CreateDeveloperAndGame(GrainFactory);
            await using var host = await GatewayTestHost.StartAsync(cluster.Client);
            await using var alice = await GatewayPlayer.ConnectAsync(host, developerKey, gameKey);
            var coin = await CreateItem(gameKey, "coin");
            var chest = Inventory(gameKey, alice.Id, "timed");
            await chest.SetSize(5);
            await chest.AddItem(new IInventoryItem[] { coin }, new[] { 7 });

            var time = await alice.CallAsync<GetInventoryUnlockTimeResponseMessage>(WildCatMsgType.GetInventoryUnlockTimeRequest,
                new StringMessage { value = "timed" }, WildCatMsgType.GetInventoryUnlockTimeResponse);
            Assert.True(time.isSuccesful, time.errorMsg);
            Assert.InRange(time.msg.time, TimeSpan.FromDays(3) - TimeSpan.FromMinutes(5), TimeSpan.FromDays(3));

            var unlock = await alice.CallAsync<GetInventoryResponseMessage>(WildCatMsgType.UnlockInventoryRequest,
                new StringMessage { value = "timed" }, WildCatMsgType.UnlockInventoryResponse);
            AssertRefused(unlock, UnlockInventoryHandler.StillLocked);
            Assert.Equal(new[] { ("coin", 7) }, (await GetInventory(alice, "timed")).msg.items);
            Assert.Empty((await GetInventory(alice)).msg.items);
            Assert.DoesNotContain(host.Logs.Entries, e => e.Level >= Microsoft.Extensions.Logging.LogLevel.Error);
        }

        [Fact]
        public async Task ClientsMayNotGrantItemsOrSetScores()
        {
            var (developerKey, gameKey) = await TestSetup.CreateDeveloperAndGame(GrainFactory);
            await using var host = await GatewayTestHost.StartAsync(cluster.Client);
            await using var alice = await GatewayPlayer.ConnectAsync(host, developerKey, gameKey);
            await CreateItem(gameKey, "gem");

            var refused = new (WildCatMsgType Request, WildCatMsgType Response, MessageBase Payload)[]
            {
                (WildCatMsgType.SetLeaderboardRequest, WildCatMsgType.SetLeaderboardResponse,
                    new SetLeaderBoardRequest { leaderBoardName = "l1", score = 1000 }),
                (WildCatMsgType.AddItemToInventoryRequest, WildCatMsgType.AddItemToInventoryResponse,
                    new BuyItemInventoryRequestMessage { inventoryName = "", items = new[] { ("gem", 99) } }),
                (WildCatMsgType.SetItemToInventoryRequest, WildCatMsgType.SetItemToInventoryResponse,
                    new BuyItemInventoryRequestMessage { inventoryName = "", items = new[] { ("gem", 99) } }),
            };
            foreach (var (request, response, payload) in refused)
            {
                // Refused with or without a session, on the request's own response type.
                AssertRefused(await alice.CallAsync<EmptyMessage>(request, payload, response), GatewayErrors.ClientGrantRejected);
                AssertRefused(await alice.Client.CallAsync<EmptyMessage>(request, payload, response), GatewayErrors.ClientGrantRejected);
            }

            Assert.Empty((await GetInventory(alice)).msg.items);
            var board = GrainFactory.GetGrain<ILeaderBoard>(ResourcesUtility.GetLeaderBoardKey(gameKey.ToString(), "l1"));
            Assert.Equal(-1, await board.GetPlayerRank(GrainFactory.GetGrain<IPlayer>(alice.Id)));
        }

        [Fact]
        public async Task InventoryRequestsNeedALoggedInPlayer()
        {
            var (developerKey, gameKey) = await TestSetup.CreateDeveloperAndGame(GrainFactory);
            await using var host = await GatewayTestHost.StartAsync(cluster.Client);
            await using var client = await host.ConnectAsync();
            var session = await client.InitializeSessionAsync(developerKey, gameKey);

            foreach (var type in new[]
                     {
                         WildCatMsgType.GetInventoryRequest, WildCatMsgType.UnlockInventoryRequest,
                         WildCatMsgType.GetInventoryUnlockTimeRequest,
                     })
            {
                var noTicket = await client.CallAsync<EmptyMessage>(type, new StringMessage(), WildCatMsgType.ErrorMsg);
                Assert.Equal(GatewayErrors.SessionRequired, noTicket.errorMsg);
                var notLoggedIn = await client.CallAsync<EmptyMessage>(type, new StringMessage(), WildCatMsgType.ErrorMsg, session.ToString());
                Assert.Equal(GatewayErrors.NotLoggedIn, notLoggedIn.errorMsg);
            }
            var remove = await client.CallAsync<EmptyMessage>(WildCatMsgType.RemoveInventoryItemRequest,
                new RemoveItemInventoryRequestMessage(), WildCatMsgType.ErrorMsg, session.ToString());
            Assert.Equal(GatewayErrors.NotLoggedIn, remove.errorMsg);
        }
    }
}
