using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using Orleans;
using WildCat.Interfaces;

namespace WildCat.Grains
{
    public class PackageContainer : Grain<PackageContainerState>, IPackageContainer
    {
        private bool _IsCreated;

        public override Task OnActivateAsync(CancellationToken cancellationToken)
        {
            //var s = this.GetPrimaryKeyString();
            _IsCreated = State.IsCreated;
            return base.OnActivateAsync(cancellationToken);
        }

        private void AssertIsCreated()
        {
            if (!_IsCreated)
                throw new InvalidOperationException("Package is being used without being initliazed");
        }

        public Task<InventoryItemType> GetItemType()
        {
            AssertIsCreated();
            return Task.FromResult(InventoryItemType.Package);
        }

        public Task<PackageContainerState> GetInfo()
        {
            AssertIsCreated();
            return Task.FromResult(State);
        }

        public Task<string> GetKey()
        {
            return Task.FromResult(this.GetPrimaryKeyString());
        }

        public Task<string> GetName()
        {
            AssertIsCreated();
            return Task.FromResult(State.Name);
        }

        public Task<string> GetItemName()
        {
            AssertIsCreated();
            return Task.FromResult(State.Name);
        }

        public Task<List<PlayerInventoryState.InInventoryItemState>> Grant()
        {
            //TODO support random draws
            if (State.IsRandomDrop)
                throw new NotImplementedException();
            List<PlayerInventoryState.InInventoryItemState> items = new List<PlayerInventoryState.InInventoryItemState>();
            for (int i = 0; i < State.items.Count; ++i)
            {
                items.Add(new PlayerInventoryState.InInventoryItemState
                {
                    Item = State.items[i].Item,
                    Count = State.items[i].Count
                });
            }
            return Task.FromResult(items);
        }

        public Task Initialize(string name, string description, List<PackageContainerState.PackageItemInfo> items, bool isLocked, List<PlayerInventoryState.InInventoryItemState> unLockItems, bool isRandom, int randomCount, List<PlayerInventoryState.InInventoryItemState> price, int capacity)
        {
            _IsCreated = true;
            State.IsCreated = true;
            return SetProperties(name, description, items, isLocked, unLockItems, isRandom, randomCount, price, capacity);
        }

        public Task SetProperties(string name, string description, List<PackageContainerState.PackageItemInfo> items, bool isLocked, List<PlayerInventoryState.InInventoryItemState> unLockItems, bool isRandom, int randomCount, List<PlayerInventoryState.InInventoryItemState> price, int capacity)
        {
            AssertIsCreated();
            State.Name = name;
            State.Description = description;
            State.items = items;
            State.IsLocked = isLocked;
            State.UnlockItems = unLockItems;
            State.IsRandomDrop = isRandom;
            State.RandomDropCount = randomCount;
            State.Price = price;
            State.Capacity = capacity;
            return WriteStateAsync();
        }
    }
}
