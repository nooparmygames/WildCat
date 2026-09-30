#if !MASTER
using Interfaces;
using Orleans;
using Orleans.Streams;
using Orleans.TestingHost;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Tests;
using Xunit;


namespace GrainUnitTests
{
    [Collection(ClusterCollection.Name)]
    public class PlayerInventoryTests
    {
        private TestCluster cluster;
        public IGrainFactory GrainFactory => this.cluster.GrainFactory;
        public PlayerInventoryTests(ClusterFixture fixture)
        {
            this.cluster = fixture.Cluster;
        }

        private async Task<Tuple<Guid, Guid, Guid>> CreateDeveloperAndSession(string developerName)
        {
            var developerKey = await GrainFactory.GetGrain<IDeveloperManager>("main").AddDeveloper(CreateDeveloper(developerName));
            var developer = GrainFactory.GetGrain<IDeveloper>(developerKey);
            var gameKey = await developer.AddGame(new GameInfo { Name = "game1" });
            var sessionKey = await developer.InitializeSession(gameKey);
            return Tuple.Create(sessionKey, developerKey, gameKey);
        }

        private DeveloperState CreateDeveloper(string name)
        {
            DeveloperState data = new DeveloperState
            {
                FirstName = name,
                Email = $"{name}@{name}.com"
            };
            return data;
        }

        [Fact]
        public async Task AddItemToInventoryAndBuyPackageTest()
        {
            string game = Guid.NewGuid().ToString();
            var itemKey = ResourcesUtility.GetItemKey("game", "coin");
            var item = GrainFactory.GetGrain<IItem>(itemKey);
            await item.Initialize("coin", "coins are used to buy stuff in the shop", null, 100000, false, false, new TimeSpan(0), false, false, 0, new TimeSpan(0), null);

            var item2Key = ResourcesUtility.GetItemKey("game", "ship");
            var item2 = GrainFactory.GetGrain<IItem>(item2Key);
            await item2.Initialize("coin", "coins are used to buy stuff in the shop", null, 100000, false, false, new TimeSpan(0), false, false, 0, new TimeSpan(0), new System.Collections.Generic.List<PlayerInventoryState.InInventoryItemState>
            {
                new PlayerInventoryState.InInventoryItemState
                {
                    Item = item,
                    Count = 100
                }
            });

            var packageKey = ResourcesUtility.GetPackageKey(game, "PackOfCoins");
            var package = GrainFactory.GetGrain<IPackageContainer>(packageKey);
            await package.Initialize("Pack of coins", "A pack of 100 coins", new System.Collections.Generic.List<PackageContainerState.PackageItemInfo>
            {
               new PackageContainerState.PackageItemInfo
               {
                   Item = item,
                   Count=100
               }
            }, false, null, false, 0, new System.Collections.Generic.List<PlayerInventoryState.InInventoryItemState> {
                new PlayerInventoryState.InInventoryItemState
                {
                    Count=2,
                    Item = item2
                }
            }, -1);

            var sessionKey = (await CreateDeveloperAndSession("developer5")).Item1;

            var guid = "device01";
            var player = GrainFactory.GetGrain<IPlayer>(guid);

            await player.CreateAccountWithDevice(player.GetPrimaryKeyString());
            Guid t = Guid.NewGuid();
            await player.LoginWithDeviceID(guid, sessionKey, t);
            var inventoryKey = ResourcesUtility.GetPlayerInventoryKey(game, player.GetPrimaryKeyString());
            var inventory = GrainFactory.GetGrain<IPlayerInventory>(inventoryKey);
            await inventory.AddItem(new IInventoryItem[] { item }, new int[] { 5 });
            await inventory.AddItem(new IInventoryItem[] { item }, new int[] { 5 });
            var info = await inventory.GetInventory();
            Assert.Single(info.items);
            Assert.Equal(10, info.items[0].Count);
            bool isPackAdded = await inventory.BuyPackage(package, 1);
            Assert.False(isPackAdded);
            Assert.Single(info.items);
            Assert.Equal(10, info.items[0].Count);
            await inventory.AddItem(new IInventoryItem[] { item2 }, new int[] { 4 });
            info = await inventory.GetInventory();
            Assert.Equal(2, info.items.Count);
            Assert.Equal(10, info.items[0].Count);
            Assert.Equal(4, info.items[1].Count);
            isPackAdded = await inventory.BuyPackage(package, 1);
            Assert.True(isPackAdded);
            info = await inventory.GetInventory();
            Assert.Equal(3, info.items.Count);
            Assert.Equal(2, info.items[1].Count);
        }

        [Fact]
        public async Task RemoveItemToInventoryAndBuyPackageTest()
        {
            string game = Guid.NewGuid().ToString();
            var itemKey = ResourcesUtility.GetItemKey("game", "coin");
            IItem item = GrainFactory.GetGrain<IItem>(itemKey);
            await item.Initialize("coin", "coins are used to buy stuff in the shop", null, 100000, false, false, new TimeSpan(0), false, false, 0, new TimeSpan(0), null);

            var sessionKey = (await CreateDeveloperAndSession("developer7")).Item1;

            var guid = "device02";
            var player = GrainFactory.GetGrain<IPlayer>(guid);
            Guid t = Guid.NewGuid();
            await player.CreateAccountWithDevice(player.GetPrimaryKeyString());
            await player.LoginWithDeviceID(guid, sessionKey, t);
            var inventoryKey = ResourcesUtility.GetPlayerInventoryKey(game, player.GetPrimaryKeyString());
            var inventory = GrainFactory.GetGrain<IPlayerInventory>(inventoryKey);
            await inventory.AddItem(new IInventoryItem[] { item }, new[] { 10 });
            var info = await inventory.GetInventory();
            Assert.Single(info.items);
            Assert.Equal(10, info.items[0].Count);
            await inventory.RemoveItem(new IInventoryItem[] { item }, new int[] { 10 });
            info = await inventory.GetInventory();
            Assert.Empty(info.items);
        }

        [Fact]
        public async Task UnlockItemsInInventory()
        {
            string game = Guid.NewGuid().ToString();
            var itemKey = ResourcesUtility.GetItemKey("game3", "coin");
            IItem item = GrainFactory.GetGrain<IItem>(itemKey);
            await item.Initialize("coin", "coins are used to buy stuff in the shop", null, 100000, false, false, new TimeSpan(0), false, false, 0, new TimeSpan(0), null);

            var sessionKey = (await CreateDeveloperAndSession("developer8")).Item1;

            var guid = "device03";
            var player = GrainFactory.GetGrain<IPlayer>(guid);

            await player.CreateAccountWithDevice(player.GetPrimaryKeyString());
            Guid t = Guid.NewGuid();
            await player.LoginWithDeviceID(guid, sessionKey, t);
            var inventoryKey = ResourcesUtility.GetPlayerInventoryKey(game, player.GetPrimaryKeyString());
            var bagKey = ResourcesUtility.GetPlayerInventoryKey(game, player.GetPrimaryKeyString(), "bag");
            var inventory = GrainFactory.GetGrain<IPlayerInventory>(inventoryKey);
            var bag = GrainFactory.GetGrain<IPlayerInventory>(bagKey);
            await bag.SetSize(100);
            await bag.AddItem(new IInventoryItem[] { item }, new[] { 10 });
            await inventory.AddItem(new IInventoryItem[] { item }, new[] { 10 });
            var (bagState, bagTime) = await bag.GetUnlocktime();
            var (inventoryState, inventoryTime) = await inventory.GetUnlocktime();
            Assert.Equal(0.0, inventoryTime.TotalMilliseconds);
            Assert.True(bagTime.TotalDays > 2);
            var inventoryItems = await inventory.Unlock();
            Assert.Single(inventoryItems);
            await Assert.ThrowsAsync<InvalidOperationException>(async () => { await bag.Unlock(); });


        }
    }
}
#endif