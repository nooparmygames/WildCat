using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using Orleans;

namespace WildCat.Interfaces
{
    public interface IPlayerInventory : IGrainWithStringKey
    {
        /// <summary>
        /// Grants items. Server-side only: the gateway rejects client grants, scripts use
        /// WildCatScriptApi.AddInventoryItems.
        /// </summary>
        Task<bool> AddItem(IInventoryItem[] item, int[] count);

        /// <summary>
        /// Sets item counts. Server-side only, like <see cref="AddItem"/>.
        /// </summary>
        Task SetItem(IInventoryItem[] item, int[] count);
        Task<bool> BuyItem(IItem[] item, int[] count);
        /// <summary>
        /// Removes items the player holds; returns false and changes nothing when any count is not
        /// positive or larger than what the inventory holds.
        /// </summary>
        Task<bool> RemoveItem(IInventoryItem[] item, int[] count);
        Task<bool> ConsumeItem(IItem[] item, int[] count);
        Task<bool> OpenAndUnlockPackage(IPackageContainer package);
        Task<bool> BuyPackage(IPackageContainer package, int count);
        Task<PlayerInventoryState> GetInventory();
        Task SetSize(int size);
        Task<PlayerInventoryState.InInventoryItemState[]> Unlock();
        Task<(PlayerInventoryState.UnlockStatus, TimeSpan)> GetUnlocktime();
    }

    /// <summary>
    /// A player's inventory. Every piece of data is a public property with an [Id], so the Orleans
    /// serializer (grain calls) and the JSON grain storage serializer both keep all of it. The
    /// legacy version kept its data in a private struct field that JSON skipped, so an inventory
    /// came back empty after reactivation. Property names stay lower case for the callers that
    /// read them (the gateway reads <see cref="items"/>).
    /// </summary>
    [GenerateSerializer]
    [Alias("wildcat.PlayerInventoryState")]
    public class PlayerInventoryState
    {
        [GenerateSerializer]
        [Alias("wildcat.PlayerInventoryState.InInventoryItemState")]
        public struct InInventoryItemState
        {
            [Id(0)] public IInventoryItem Item { get; set; }
            [Id(1)] public int Count { get; set; }
        }

        [GenerateSerializer, Alias("wildcat.PlayerInventoryState.UnlockStatus")]
        public enum UnlockStatus { NotCountingTimeForUnlock, CountingTimeForUnlock }

        public PlayerInventoryState() : this(-1)
        {
        }

        public PlayerInventoryState(int size)
        {
            items = new List<InInventoryItemState>();
            this.size = size;
            status = UnlockStatus.NotCountingTimeForUnlock;
        }

        [Id(0)] public List<InInventoryItemState> items { get; set; }

        /// <summary>
        /// Capacity in item stacks. Zero or less means unlimited and no unlock timer (the main
        /// inventory); a positive size makes a timed chest.
        /// </summary>
        [Id(1)] public int size { get; set; }
        [Id(2)] public DateTime startTime { get; set; }
        [Id(3)] public DateTime endTime { get; set; }
        [Id(4)] public UnlockStatus status { get; set; }

        public bool CanUnlock()
        {
            return true;
        }

        public TimeSpan GetUnlockTime()
        {
            return size <= 0 ? TimeSpan.FromSeconds(0) : endTime - DateTime.UtcNow;
        }

        public List<InInventoryItemState> Unlock()
        {
            if (DateTime.UtcNow < endTime && size > 0)
                throw new InvalidOperationException("You cannot unlock the inventory without passing the time");
            status = UnlockStatus.NotCountingTimeForUnlock;
            List<InInventoryItemState> unlocked = GetUserItems();
            items.Clear();
            return unlocked;
        }

        public void SetSize(int size)
        {
            this.size = size;
        }

        public bool HasItem(IInventoryItem item, int count)
        {
            var inInventoryItem = items.FirstOrDefault(x => x.Item.GetPrimaryKeyString() == item.GetPrimaryKeyString());
            return inInventoryItem.Item != null && inInventoryItem.Count >= count;
        }

        public bool HasItems(List<InInventoryItemState> item)
        {
            bool hasItems = true;
            for (int i = 0; i < item.Count; ++i)
            {
                var inInventoryItem = items.FirstOrDefault(x => x.Item.GetPrimaryKeyString() == item[i].Item.GetPrimaryKeyString());
                hasItems = inInventoryItem.Item != default && inInventoryItem.Count >= item[i].Count;
                if (!hasItems)
                    break;
            }
            return hasItems;
        }

        public bool TryGetItem(IInventoryItem item, int count, out IInventoryItem value)
        {
            var inInventoryItem = items.FirstOrDefault(x => x.Item.GetPrimaryKeyString() == item.GetPrimaryKeyString());
            bool exists = inInventoryItem.Item != null && inInventoryItem.Count >= count;
            value = exists ? inInventoryItem.Item : null;
            return exists;
        }

        public bool IsFull()
        {
            int itemCount = items == null ? 0 : items.Count;
            return size > 0 && itemCount >= size;
        }

        public void AddItem(IInventoryItem item, int count)
        {
            int index = IndexOf(item);
            if (index == -1 && IsFull())
                throw new InvalidOperationException("The inventory is full");
            if (index != -1)
            {
                // checked: a stack that would pass int.MaxValue throws OverflowException, and the
                // grain rolls the whole change back instead of storing a negative count.
                items[index] = new InInventoryItemState { Count = checked(items[index].Count + count), Item = items[index].Item };
            }
            else
            {
                items.Add(new InInventoryItemState
                {
                    Item = item,
                    Count = count
                });
            }
            StartUnlockTimerIfStopped();
        }

        public void SetItem(IInventoryItem item, int count)
        {
            int index = IndexOf(item);
            if (index == -1 && IsFull())
                throw new InvalidOperationException("The inventory is full");
            if (index != -1)
            {
                items[index] = new InInventoryItemState { Count = count, Item = items[index].Item };
            }
            else
            {
                items.Add(new InInventoryItemState
                {
                    Item = item,
                    Count = count
                });
            }
            StartUnlockTimerIfStopped();
        }

        /// <summary>
        /// Removes <paramref name="count"/> of an item. Returns false and changes nothing when the
        /// count is not positive or the inventory holds fewer; clients may call this (message 115),
        /// so a negative count must never add items.
        /// </summary>
        public bool RemoveItem(IInventoryItem item, int count)
        {
            if (count <= 0)
                return false;
            int index = IndexOf(item);
            if (index == -1 || items[index].Count < count)
                return false;
            int remaining = items[index].Count - count;
            if (remaining == 0)
                items.RemoveAt(index);
            else
                items[index] = new InInventoryItemState { Count = remaining, Item = items[index].Item };
            return true;
        }

        public List<InInventoryItemState> GetUserItems()
        {
            return new List<InInventoryItemState>(items);
        }

        public bool CanContain(int additionalStacks)
        {
            return size <= 0 || additionalStacks <= 0 || (long)items.Count + additionalStacks <= size;
        }

        public bool CanContain(IEnumerable<IInventoryItem> additions)
        {
            var existing = items.Select(x => x.Item.GetPrimaryKeyString()).ToHashSet(StringComparer.Ordinal);
            int additionalStacks = additions.Select(x => x.GetPrimaryKeyString())
                .Distinct(StringComparer.Ordinal).Count(key => !existing.Contains(key));
            return CanContain(additionalStacks);
        }

        private int IndexOf(IInventoryItem item)
        {
            string key = item.GetPrimaryKeyString();
            for (int i = 0; i < items.Count; ++i)
            {
                if (items[i].Item.GetPrimaryKeyString() == key)
                    return i;
            }
            return -1;
        }

        private void StartUnlockTimerIfStopped()
        {
            if (status == UnlockStatus.NotCountingTimeForUnlock)
            {
                startTime = DateTime.UtcNow;
                endTime = startTime.AddDays(3);
                status = UnlockStatus.CountingTimeForUnlock;
            }
        }
    }
}
