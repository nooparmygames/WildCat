using WildCat.Interfaces;
using Orleans;
using Orleans.Streams;
using Orleans.TestingHost;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Xunit;

namespace WildCat.Tests
{
    [Collection(ClusterCollection.Name)]
    public class PackageContainerTests
    {
        private InProcessTestCluster cluster;
        public IGrainFactory GrainFactory => this.cluster.Client;

        public PackageContainerTests(ClusterFixture fixture)
        {
            this.cluster = fixture.Cluster;
        }

        [Fact]
        public async Task CreatePackageWithPriceTest()
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
            },-1);

            var info = await package.GetInfo();
            Assert.Single(info.Price);
            Assert.Equal(info.Price[0].Item.GetPrimaryKeyString(), item2.GetPrimaryKeyString());
            Assert.Single(info.items);
            Assert.Equal(100, info.items[0].Count);
            Assert.Equal(info.items[0].Item.GetPrimaryKeyString(), item.GetPrimaryKeyString());
        }
    }
}
