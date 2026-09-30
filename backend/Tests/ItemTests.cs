using Interfaces;
using Orleans;
using Orleans.Streams;
using Orleans.TestingHost;
using System;
using System.Linq;
using System.Threading.Tasks;
using Tests;
using Xunit;

namespace GrainUnitTests
{
    [Collection(ClusterCollection.Name)]
    public class ItemTests
    {
        private InProcessTestCluster cluster;
        public IGrainFactory GrainFactory => this.cluster.Client;
        public ItemTests(ClusterFixture fixture)
        {
            this.cluster = fixture.Cluster;
        }
        
        [Fact]
        public async Task CreateItemWithPriceTest()
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

            var info = await item2.GetItemInfo();
            Assert.Single(info.Price);
            Assert.Equal("coin", ( await info.Price[0].Item.GetName()));
        }
    }
}
