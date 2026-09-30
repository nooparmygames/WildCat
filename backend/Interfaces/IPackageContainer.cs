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

    public class PackageContainerState
    {
        public class PackageItemInfo
        {
            public IInventoryItem Item { get; set; }
            public int Count;
            public int chance;
        }

        public bool IsCreated { get; set; }
        public string Name { get; set; }
        public string Description { get; set; }
        public List<PackageItemInfo> items { get; set; }
        public List<PlayerInventoryState.InInventoryItemState> UnlockItems { get; set; }
        public bool IsLocked { get; set; }
        public bool IsRandomDrop { get; set; }
        public int RandomDropCount { get; set; }
        public List<PlayerInventoryState.InInventoryItemState> Price { get; set; }

        public int Capacity { get; set; }

        public PackageContainerState()
        {
            items = new List<PackageItemInfo>();
            UnlockItems = new List<PlayerInventoryState.InInventoryItemState>();
            Capacity = -1;
        }
    }
}