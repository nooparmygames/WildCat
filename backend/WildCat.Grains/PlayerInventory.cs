using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Orleans;
using Orleans.Runtime;
using WildCat.Interfaces;
using Microsoft.Extensions.Logging;

namespace WildCat.Grains
{
    /// <summary>
    /// A player's named inventory, keyed "{gameKey}_{playerId}_[{name}_]inv". The state lives in
    /// the grain's own storage (the legacy code stored a JSON blob in PlayerStorage instead). Every
    /// change is applied to State and written; when a change fails part way or the write fails,
    /// the last written state is read back so memory and storage agree.
    /// </summary>
    public class PlayerInventory : Grain, IPlayerInventory
    {
        private readonly IPersistentState<PlayerInventoryState> state;

        private PlayerInventoryState State => state.State;

        private string gameKey, playerId;
        private readonly ILogger logger;

        public PlayerInventory(
            [PersistentState(GrainStateNames.PlayerInventory, GrainStorageNames.Default)] IPersistentState<PlayerInventoryState> state,
            ILogger<IPlayerInventory> logger)
        {
            this.state = state;
            this.logger = logger;
        }

        public override Task OnActivateAsync(CancellationToken cancellationToken)
        {
            State.items ??= new List<PlayerInventoryState.InInventoryItemState>();
            (gameKey, playerId, _) = ResourcesUtility.GetPlayerInventoryparts(this.GetPrimaryKeyString());
            return base.OnActivateAsync(cancellationToken);
        }

        public async Task<bool> AddItem(IInventoryItem[] items, int[] counts)
        {
            AssertSameLength(items, counts);
            if (!State.CanContain(counts.Sum()))
                return false;
            return await Apply(nameof(AddItem), () =>
            {
                for (int i = 0; i < items.Length; ++i)
                    State.AddItem(items[i], counts[i]);
                return Task.FromResult(true);
            });
        }

        public async Task SetItem(IInventoryItem[] items, int[] counts)
        {
            AssertSameLength(items, counts);
            if (!State.CanContain(counts.Sum()))
                return;
            await Apply(nameof(SetItem), () =>
            {
                for (int i = 0; i < items.Length; ++i)
                    State.SetItem(items[i], counts[i]);
                return Task.FromResult(true);
            });
        }

        public Task<bool> BuyItem(IItem[] items, int[] counts)
        {
            AssertSameLength(items, counts);
            return Apply(nameof(BuyItem), async () =>
            {
                for (int i = 0; i < items.Length; ++i)
                {
                    var item = items[i];
                    var count = counts[i];
                    var itemInfo = await item.GetItemInfo();
                    bool haveItems = State.HasItems(itemInfo.Price);
                    bool hasCapacity = State.CanContain(count - itemInfo.Price.Sum(x => x.Count));
                    if (!haveItems || !hasCapacity)
                        return false;
                    foreach (var priceItem in itemInfo.Price)
                        State.RemoveItem(priceItem.Item, priceItem.Count);
                    State.AddItem(item, count);
                }
                return true;
            });
        }

        public Task<bool> ConsumeItem(IItem[] items, int[] counts)
        {
            AssertSameLength(items, counts);
            return Apply(nameof(ConsumeItem), () => Task.FromResult(RemoveAll(items, counts)));
        }

        public Task<PlayerInventoryState> GetInventory()
        {
            return Task.FromResult(State);
        }

        public Task<bool> RemoveItem(IInventoryItem[] items, int[] counts)
        {
            AssertSameLength(items, counts);
            return Apply(nameof(RemoveItem), () => Task.FromResult(RemoveAll(items, counts)));
        }

        public async Task<bool> OpenAndUnlockPackage(IPackageContainer package)
        {
            if (!State.TryGetItem(package, 1, out _))
                return false;
            var packageData = await package.GetInfo();
            bool shouldGrant = !packageData.IsLocked || State.HasItems(packageData.UnlockItems);
            if (!shouldGrant)
                return false;

            var grantedItems = await package.Grant();
            bool canContain = State.CanContain(grantedItems.Sum(x => x.Count) - packageData.UnlockItems.Sum(x => x.Count));
            if (!canContain)
                return false;

            return await Apply(nameof(OpenAndUnlockPackage), () =>
            {
                if (packageData.IsLocked)
                {
                    foreach (var item in packageData.UnlockItems)
                        State.RemoveItem(item.Item, item.Count);
                }
                foreach (var item in grantedItems)
                    State.AddItem(item.Item, item.Count);
                return Task.FromResult(true);
            });
        }

        public async Task<bool> BuyPackage(IPackageContainer package, int count)
        {
            var info = await package.GetInfo();
            bool hasPrice = State.HasItems(info.Price);
            bool canContain = State.CanContain(count - info.Price.Sum(x => x.Count));
            if (!hasPrice || !canContain)
                return false;

            return await Apply(nameof(BuyPackage), () =>
            {
                foreach (var priceItem in info.Price)
                    State.RemoveItem(priceItem.Item, priceItem.Count);
                State.AddItem(package, count);
                return Task.FromResult(true);
            });
        }

        public async Task SetSize(int size)
        {
            await Apply(nameof(SetSize), () =>
            {
                State.SetSize(size);
                return Task.FromResult(true);
            });
        }

        public async Task<PlayerInventoryState.InInventoryItemState[]> Unlock()
        {
            List<PlayerInventoryState.InInventoryItemState> data = null;
            await Apply(nameof(Unlock), async () =>
            {
                data = State.Unlock();
                // The unlocked items go to the player's main inventory. For the main inventory that
                // is this grain, a call to itself that the call chain reentrancy filter lets through
                // (bug 17 in docs/legacy-backend-notes.md).
                await GrainFactory.GetGrain<IPlayerInventory>(ResourcesUtility.GetPlayerInventoryKey(gameKey, playerId))
                    .AddItem(data.Select(x => x.Item).ToArray(), data.Select(x => x.Count).ToArray());
                return true;
            });
            return data.ToArray();
        }

        public Task<(PlayerInventoryState.UnlockStatus, TimeSpan)> GetUnlocktime()
        {
            return Task.FromResult((State.status, State.GetUnlockTime()));
        }

        private bool RemoveAll(IInventoryItem[] items, int[] counts)
        {
            for (int i = 0; i < items.Length; ++i)
            {
                if (!State.RemoveItem(items[i], counts[i]))
                    return false;
            }
            return true;
        }

        private static void AssertSameLength(Array items, int[] counts)
        {
            if (items == null || counts == null || items.Length != counts.Length)
                throw new ArgumentException("Every item needs exactly one count.");
        }

        /// <summary>
        /// Runs <paramref name="change"/> against State and writes the result. When the change
        /// returns false nothing is written; when it returns false or throws, or the write fails,
        /// the last written state is read back so a half-applied change does not stay in memory.
        /// </summary>
        private async Task<bool> Apply(string operation, Func<Task<bool>> change)
        {
            bool applied;
            try
            {
                applied = await change();
                if (applied)
                    await state.WriteStateAsync();
            }
            catch (Exception ex)
            {
                logger.LogWarning(ex, "{Operation} failed on inventory {Inventory}", operation, this.GetPrimaryKeyString());
                await state.ReadStateAsync();
                throw;
            }

            if (!applied)
                await state.ReadStateAsync();
            return applied;
        }
    }
}
