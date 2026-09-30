using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using Orleans;

namespace Interfaces
{
    public interface IPlayerInventory : IGrainWithStringKey
    {
        Task<bool> AddItem(IInventoryItem[] item, int[] count);
        Task SetItem(IInventoryItem[] item, int[] count);
        Task<bool> BuyItem(IItem[] item, int[] count);
        Task<bool> RemoveItem(IInventoryItem[] item, int[] count);
        Task<bool> ConsumeItem(IItem[] item, int[] count);
        Task<bool> OpenAndUnlockPackage(IPackageContainer package);
        Task<bool> BuyPackage(IPackageContainer package, int count);
        Task<PlayerInventoryState> GetInventory();
        Task SetSize(int size);
        Task<PlayerInventoryState.InInventoryItemState[]> Unlock();
        Task<(PlayerInventoryState.StateValues.Status, TimeSpan)> GetUnlocktime();
    }

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

        public PlayerInventoryState(int size)
        {
            value = new StateValues(size);
        }

        public PlayerInventoryState()
        {

        }
        public List<InInventoryItemState> items { get { return value.items; } }
        [Id(0)] private StateValues value;

        public int size { get { return value.size; } }
        public StateValues.Status status { get { return value.status; } }
        public bool CanUnlock()
        {
            return value.CanUnlock();
        }

        public TimeSpan GetUnlockTime()
        {
            return value.GetUnlockTime();
        }

        public List<InInventoryItemState> Unlock()
        {
            return value.Unlock();
        }

        public void SetSize(int size)
        {
            value.SetSize(size);
        }

        public bool HasItem(IInventoryItem item, int count)
        {
            return value.HasItem(item, count);
        }

        public bool HasItems(List<PlayerInventoryState.InInventoryItemState> item)
        {
            return value.HasItems(item);
        }

        public bool TryGetItem(IInventoryItem item, int count, out IInventoryItem value)
        {
            return this.value.TryGetItem(item, count, out value);
        }

        public void AddItem(IInventoryItem item, int count)
        {
            value.AddItem(item, count);
        }

        public void SetItem(IInventoryItem item, int count)
        {
            value.SetItem(item, count);
        }

        public bool RemoveItem(IInventoryItem item, int count)
        {
            return value.RemoveItem(item, count);
        }

        public bool CanContain(int count)
        {
            return value.CanContain(count);
        }

        [GenerateSerializer]
        [Alias("wildcat.PlayerInventoryState.StateValues")]
        public struct StateValues
        {
            [Id(0)] public List<InInventoryItemState> items { get; set; }
            [Id(1)] public int size { get; set; }
            [Id(2)] public DateTime startTime;
            [Id(3)] public DateTime endTime;
            public enum Status { NotCountingTimeForUnlock, CountingTimeForUnlock }
            [Id(4)] public Status status;

            public StateValues(int size)
            {
                items = new List<InInventoryItemState>();
                status = Status.NotCountingTimeForUnlock;
                this.size = size;
                startTime = default;
                endTime = default;
            }

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
                status = Status.NotCountingTimeForUnlock;
                List<InInventoryItemState> items = GetUserItems();
                this.items.Clear();
                return items;
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

            public bool HasItems(List<PlayerInventoryState.InInventoryItemState> item)
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
                bool exists = false;
                var inInventoryItem = items.FirstOrDefault(x => x.Item.GetPrimaryKeyString() == item.GetPrimaryKeyString());
                exists = inInventoryItem.Item != null && inInventoryItem.Count >= count;
                if (exists)
                    value = inInventoryItem.Item;
                else
                    value = null;
                return exists;
            }

            public bool IsFull()
            {
                int itemCount = items == null ? 0 : items.Count;
                return size > 0 && size < itemCount;
            }

            public void AddItem(IInventoryItem item, int count)
            {
                if (IsFull())
                    throw new InvalidOperationException($"The inventory is full");
                int index = -1;
                for (int i = 0; i < items.Count; ++i)
                {
                    if (items[i].Item.GetPrimaryKeyString() == item.GetPrimaryKeyString())
                    {
                        index = i;
                        break;
                    }
                }
                InInventoryItemState inInventoryItem = (index == -1) ? new InInventoryItemState() : items[index];
                if (inInventoryItem.Item != null)
                {
                    inInventoryItem = new InInventoryItemState { Count = inInventoryItem.Count + count, Item = inInventoryItem.Item };
                    items[index] = inInventoryItem;
                }
                else
                {
                    items.Add(new InInventoryItemState
                    {
                        Item = item,
                        Count = count
                    });
                }
                if (status == Status.NotCountingTimeForUnlock)
                {
                    startTime = DateTime.UtcNow;
                    endTime = startTime.AddDays(3);
                    status = Status.CountingTimeForUnlock;
                }
            }

            public void SetItem(IInventoryItem item, int count)
            {
                if (IsFull())
                    throw new InvalidOperationException($"The inventory is full");
                int index = -1;
                for (int i = 0; i < items.Count; ++i)
                {
                    if (items[i].Item.GetPrimaryKeyString() == item.GetPrimaryKeyString())
                    {
                        index = i;
                        break;
                    }
                }
                InInventoryItemState inInventoryItem = (index == -1) ? new InInventoryItemState() : items[index];
                if (inInventoryItem.Item != null)
                {
                    inInventoryItem = new InInventoryItemState { Count = count, Item = inInventoryItem.Item };
                    items[index] = inInventoryItem;
                }
                else
                {
                    items.Add(new InInventoryItemState
                    {
                        Item = item,
                        Count = count
                    });
                }
                if (status == Status.NotCountingTimeForUnlock)
                {
                    startTime = DateTime.UtcNow;
                    endTime = startTime.AddDays(3);
                    status = Status.CountingTimeForUnlock;
                }
            }

            public bool RemoveItem(IInventoryItem item, int count)
            {
                int index = -1;
                InInventoryItemState inInventoryItem = new InInventoryItemState();
                for (int i = 0; i < items.Count; ++i)
                {
                    var x = items[i];
                    if (x.Item.GetPrimaryKeyString() == item.GetPrimaryKeyString())
                    {
                        index = i;
                        inInventoryItem = x;
                        break;
                    }
                }
                if (inInventoryItem.Item != null)
                {
                    inInventoryItem = new InInventoryItemState { Count = inInventoryItem.Count - count, Item = inInventoryItem.Item };
                    if (inInventoryItem.Count == 0)
                    {
                        items.RemoveAt(index);
                    }
                    else
                    {
                        items[index] = inInventoryItem;
                    }
                    return true;
                }
                return false;
            }

            public List<InInventoryItemState> GetUserItems()
            {
                List<InInventoryItemState> list = new List<InInventoryItemState>(items.Count);
                foreach (var item in items)
                    list.Add(item);
                return list;
            }

            public bool CanContain(int count)
            {
                return size == -1 || !IsFull(); //items.Count + count < size;
            }
        }
    }
}