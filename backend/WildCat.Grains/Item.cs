using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using Orleans;
using WildCat.Interfaces;

namespace WildCat.Grains
{
    public class Item : Grain<ItemState>, IItem
    {
        private bool _IsCreated;

        public override Task OnActivateAsync(CancellationToken cancellationToken)
        {
            _IsCreated = State.IsCreated;
            return base.OnActivateAsync(cancellationToken);
        }

        private void AssertIsCreated()
        {
            //if (!_IsCreated)
            //    throw new InvalidOperationException("Item is being used before being created");
        }

        public Task<InventoryItemType> GetItemType()
        {
            AssertIsCreated();
            return Task.FromResult(InventoryItemType.Item);
        }

        public Task<string> GetDescription()
        {
            AssertIsCreated();
            return Task.FromResult(State.Description);
        }

        public Task<ItemState> GetItemInfo()
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

        public Task Initialize(string name, string description, Dictionary<string, string> metaData, int maxAllowed, bool stackable, bool automaticallyConsumed, TimeSpan effectTime, bool isConsumable, bool isRegenerating, int regenerationAmount, TimeSpan regenerationPeriod,List<PlayerInventoryState.InInventoryItemState> price)
        {
            if (string.IsNullOrWhiteSpace(name))
                throw new ArgumentException("Name should not be null or whitespace");
            State.IsCreated = true;
            _IsCreated = true;
            return SetItemProperties(name, description, metaData, maxAllowed, stackable, automaticallyConsumed, effectTime, isConsumable, isRegenerating, regenerationAmount, regenerationPeriod,price);
        }

        public Task SetDescription(string description)
        {
            AssertIsCreated();
            State.Description = description;
            return WriteStateAsync();
        }

        public Task SetItemProperties(string name, string description, Dictionary<string, string> metaData, int maxAllowed, bool stackable, bool automaticallyConsumed, TimeSpan effectTime, bool isConsumable, bool isRegenerating, int regenerationAmount, TimeSpan regenerationPeriod,List<PlayerInventoryState.InInventoryItemState> price)
        {
            AssertIsCreated();
            State.Name = name;
            State.Description = description;
            State.MetaData = metaData;
            State.MaxAllowedToHave = maxAllowed;
            State.Stackable = stackable;
            State.AutomaticallyConsumed = automaticallyConsumed;
            State.EffectTime = effectTime;
            State.IsConsumable = isConsumable;
            State.IsRegenerating = isRegenerating;
            State.RegenerationAmount = regenerationAmount;
            State.RegenerationPeriod = regenerationPeriod;
            State.Price = price;
            return WriteStateAsync();
        }

        public Task SetName(string name)
        {
            AssertIsCreated();
            State.Name = name;
            return WriteStateAsync();
        }
    }
}
