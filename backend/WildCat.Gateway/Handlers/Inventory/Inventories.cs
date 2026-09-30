using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Orleans;
using WildCat.Interfaces;

namespace WildCat.Gateway
{
    /// <summary>
    /// What the inventory handlers share. A player has a main inventory (empty name) and any
    /// number of named ones, each an IPlayerInventory keyed "{gameKey}_{playerId}_[{name}_]inv".
    /// Items are granted by server script events (WildCatScriptApi.AddInventoryItems and
    /// SetInventoryItems); clients may read, remove and unlock (see
    /// <see cref="RefusedClientGrantsHandler"/>).
    /// </summary>
    internal static class Inventories
    {
        /// <summary>
        /// The named inventory of the request's player in the session's game. (The legacy code
        /// took the game of the socket's login, which is empty when the ticket was logged in on
        /// another socket; the session's game is the one the dispatcher checked.) A name longer than
        /// <see cref="InputLimits.MaxNameLength"/> is error code 1.
        /// </summary>
        public static IPlayerInventory Get(MessageContext context, string inventoryName)
        {
            return context.Grains.GetGrain<IPlayerInventory>(ResourcesUtility.GetPlayerInventoryKey(
                context.Session.GameKey.ToString(), context.Player.GetPrimaryKeyString(),
                InputLimits.Name(inventoryName ?? "", "inventory name")));
        }

        /// <summary>
        /// An item of the session's game. IInventoryItem has two grain classes (items and
        /// packages), so the item class is named. A name longer than
        /// <see cref="InputLimits.MaxNameLength"/> is error code 1.
        /// </summary>
        public static IInventoryItem Item(MessageContext context, string itemName)
        {
            return context.Grains.GetGrain<IInventoryItem>(
                ResourcesUtility.GetItemKey(context.Session.GameKey.ToString(), InputLimits.Name(itemName, "item name")),
                ResourcesUtility.GetGameItemsClassName());
        }

        /// <summary>The (item name, count) pairs the answers carry.</summary>
        public static async Task<(string, int)[]> NamesAndCounts(IEnumerable<PlayerInventoryState.InInventoryItemState> items)
        {
            if (items == null)
                return Array.Empty<(string, int)>();
            return await Task.WhenAll(items.Select(async x => (await x.Item.GetName(), x.Count)));
        }
    }
}
