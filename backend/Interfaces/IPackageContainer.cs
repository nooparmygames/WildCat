using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using Orleans;

namespace Interfaces
{
    public interface IPackageContainer : IInventoryItem, IGrantableResorce
    {
        Task Initialize(string name, string description, List<PackageContainerState.PackageItemInfo> items, bool isLocked, List<PlayerInventoryState.InInventoryItemState> unLockItems, bool isRandom, int randomCount, List<PlayerInventoryState.InInventoryItemState> price, int capacity);
        Task SetProperties(string name, string description, List<PackageContainerState.PackageItemInfo> items, bool isLocked, List<PlayerInventoryState.InInventoryItemState> unLockItems, bool isRandom, int randomCount, List<PlayerInventoryState.InInventoryItemState> price, int capacity);
        Task<PackageContainerState> GetInfo();
    }

    [GenerateSerializer]
    [Alias("wildcat.PackageContainerState")]
    public class PackageContainerState
    {
        [GenerateSerializer]
        [Alias("wildcat.PackageContainerState.PackageItemInfo")]
        public class PackageItemInfo
        {
            [Id(0)] public IInventoryItem Item { get; set; }
            [Id(1)] public int Count;
            [Id(2)] public int chance;
        }

        [Id(0)] public bool IsCreated { get; set; }
        [Id(1)] public string Name { get; set; }
        [Id(2)] public string Description { get; set; }
        [Id(3)] public List<PackageItemInfo> items { get; set; }
        [Id(4)] public List<PlayerInventoryState.InInventoryItemState> UnlockItems { get; set; }
        [Id(5)] public bool IsLocked { get; set; }
        [Id(6)] public bool IsRandomDrop { get; set; }
        [Id(7)] public int RandomDropCount { get; set; }
        [Id(8)] public List<PlayerInventoryState.InInventoryItemState> Price { get; set; }

        [Id(9)] public int Capacity { get; set; }

        public PackageContainerState()
        {
            items = new List<PackageItemInfo>();
            UnlockItems = new List<PlayerInventoryState.InInventoryItemState>();
            Capacity = -1;
        }
    }
}