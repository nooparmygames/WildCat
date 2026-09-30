using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using Orleans;
using Interfaces;
using Microsoft.Extensions.Logging;

namespace Collections
{
    public class PlayerInventory : Grain<PlayerInventoryState>, IPlayerInventory
    {
        private string gameKey, playerId, playerInventoryName;
        private ILogger logger;

        public PlayerInventory(ILogger<IPlayerInventory> logger)
        {
            this.logger = logger;
        }

        public async override Task OnActivateAsync()
        {
            if (State.items == null)
            {
                State = new PlayerInventoryState(-1);
            }
            (gameKey, playerId, playerInventoryName) = ResourcesUtility.GetPlayerInventoryparts(this.GetPrimaryKeyString());
            var data = await GrainFactory.GetGrain<IPlayerStorage>(ResourcesUtility.GetPlayPlayerStorageKey(gameKey, playerId)).GetPlayerString(this.GetPrimaryKeyString());
            if (data.Item1)
            {
                State = Newtonsoft.Json.JsonConvert.DeserializeObject<PlayerInventoryState>(data.Item2);
            }
            await base.OnActivateAsync();
        }

        public async Task<bool> AddItem(IInventoryItem[] items, int[] counts)
        {
            if (!State.CanContain(counts.Sum()))
                return false;
            var prev = this.State;
            for (int i = 0; i < items.Length; ++i)
            {
                int count = counts[i];
                IInventoryItem item = items[i];
                State.AddItem(item, count);
            }
            try
            {
                await Save();
                return true;
            }
            catch (Exception ex)
            {
                logger.LogError($"AddItem Failed to save  {ex.ToString()}");
                State = prev;
                throw ex;
            }
        }

        public async Task SetItem(IInventoryItem[] items, int[] counts)
        {
            if (!State.CanContain(counts.Sum()))
                return;
            var prev = this.State;
            for (int i = 0; i < items.Length; ++i)
            {
                int count = counts[i];
                IInventoryItem item = items[i];

                State.SetItem(item, count);
            }
            try
            {
                await Save();
            }
            catch (Exception ex)
            {
                logger.LogError($"AddItem Failed to save  {ex.ToString()}");
                State = prev;
                throw ex;
            }
        }

        public async Task<bool> BuyItem(IItem[] items, int[] counts)
        {
            var prev = State;
            bool done = true;
            for (int i = 0; i < items.Length; ++i)
            {
                var item = items[i];
                var count = counts[i];
                var itemInfo = await item.GetItemInfo();
                bool haveItems = State.HasItems(itemInfo.Price);
                bool hasCapacity = State.CanContain(count - itemInfo.Price.Sum(x => x.Count));
                if (haveItems && hasCapacity)
                {
                    foreach (var priceItem in itemInfo.Price)
                        State.RemoveItem(priceItem.Item, priceItem.Count);
                    State.AddItem(item, count);
                }
                else
                {
                    done = false;
                    break;
                }
            }
            if (done)
            {
                try
                {
                    await Save();
                }
                catch (Exception ex)
                {
                    State = prev;
                    logger.LogError($"{ex.ToString()}");
                    done = false;
                }
            }
            return done;
        }

        private Task Save()
        {
            return GrainFactory.GetGrain<IPlayerStorage>(ResourcesUtility.GetPlayPlayerStorageKey(gameKey, playerId)).SetPlayerString(this.GetPrimaryKeyString(), Newtonsoft.Json.JsonConvert.SerializeObject(State));
        }

        public async Task<bool> ConsumeItem(IItem[] items, int[] counts)
        {
            var prev = State;
            bool done = true;
            for (int i = 0; i < items.Length; ++i)
            {
                var item = items[i];
                var count = counts[i];
                var result = State.RemoveItem(item, count);
                if (!result)
                {
                    done = result;
                    break;
                }
            }
            if (done)
            {
                try
                {
                    await Save();
                }
                catch (Exception ex)
                {
                    logger.LogError($"Failed to save {ex}");
                    State = prev;
                    done = false;
                }
            }
            return done;
        }

        public Task<PlayerInventoryState> GetInventory()
        {
            return Task.FromResult(State);
        }

        public async Task<bool> RemoveItem(IInventoryItem[] items, int[] counts)
        {
            var prev = State;
            bool done = true;
            for (int i = 0; i < items.Length; ++i)
            {
                var count = counts[i];
                var item = items[i];
                var result = State.RemoveItem(item, count);
                if (!result)
                {
                    done = false;
                    break;
                }
            }
            if (done)
            {
                try
                {
                    await Save();
                }
                catch (Exception ex)
                {
                    logger.LogError($"Failed to store {ex}");
                    State = prev;
                    done = false;
                }
            }
            return done;
        }

        public async Task<bool> OpenAndUnlockPackage(IPackageContainer package)
        {
            var prev = State;
            IInventoryItem uselessItem;
            if (!State.TryGetItem(package, 1, out uselessItem))
            {
                return false;
            }
            bool shouldGrant = false;
            var packageData = await package.GetInfo();
            if (!packageData.IsLocked)
            {
                shouldGrant = true;
            }
            else
            {
                var unlockingItems = packageData.UnlockItems;
                shouldGrant = State.HasItems(unlockingItems);
            }
            if (shouldGrant)
            {
                var grantedItems = await package.Grant();
                bool canContain = State.CanContain(grantedItems.Sum(x => x.Count) - packageData.UnlockItems.Sum(x => x.Count));
                if (canContain)
                {
                    if (packageData.IsLocked)
                    {
                        foreach (var item in packageData.UnlockItems)
                            State.RemoveItem(item.Item, item.Count);
                    }

                    foreach (var item in grantedItems)
                        State.AddItem(item.Item, item.Count);
                    try
                    {
                        await Save();
                        return true;
                    }
                    catch (Exception ex)
                    {
                        State = prev;
                        logger.LogError($"Failed to store {ex.ToString()}");
                        return false;
                    }
                }
                else
                {
                    return false;
                }
            }
            else
            {
                return false;
            }
        }

        public async Task<bool> BuyPackage(IPackageContainer package, int count)
        {
            var prev = State;
            var info = await package.GetInfo();
            bool hasPrice = State.HasItems(info.Price);
            bool canContain = State.CanContain(count - info.Price.Sum(x => x.Count));
            if (hasPrice && canContain)
            {
                foreach (var priceItem in info.Price)
                {
                    State.RemoveItem(priceItem.Item, priceItem.Count);
                }
                State.AddItem(package, count);
                try
                {
                    await Save();
                    return true;
                }
                catch (Exception ex)
                {
                    State = prev;
                    logger.LogError($"Failed to save {ex}");
                    return false;
                }
            }
            else
            {
                return false;
            }
        }

        public async Task SetSize(int size)
        {
            var prev = State.size;
            try
            {
                State.SetSize(size);
                await Save();
            }
            catch (Exception ex)
            {
                State.SetSize(prev);
                throw ex;
            }
        }

        public async Task<PlayerInventoryState.InInventoryItemState[]> Unlock()
        {
            var prev = State;
            List<PlayerInventoryState.InInventoryItemState> data = State.Unlock();
            try
            {
                await GrainFactory.GetGrain<IPlayerInventory>(ResourcesUtility.GetPlayerInventoryKey(gameKey, playerId)).AddItem(data.Select(x => x.Item).ToArray(), data.Select(x => x.Count).ToArray());
                await Save();
                return data.ToArray();
            }
            catch (Exception ex)
            {
                logger.LogError($"Failed to store {ex.ToString()}");
                State = prev;
                throw ex;
            }
        }

        public Task<(PlayerInventoryState.StateValues.Status, TimeSpan)> GetUnlocktime()
        {
            return Task.FromResult((State.status, State.GetUnlockTime()));
        }
    }
}