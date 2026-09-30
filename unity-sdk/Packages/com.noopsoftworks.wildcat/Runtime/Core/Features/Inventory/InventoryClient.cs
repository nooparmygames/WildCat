using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using WildCat.Protocol;

namespace WildCat
{
    /// <summary>
    /// The logged in player's inventories in the session's game. Clients can read, spend (remove)
    /// and unlock, but not grant: since roadmap step 4 the server refuses grants from clients.
    /// Grant items from a server script event (WildCat.AddInventoryItems in the script API) run
    /// with <see cref="ScriptsClient.ExecuteAsync(string, object[])"/>.
    /// </summary>
    public sealed class InventoryClient : FeatureClient
    {
        internal InventoryClient(WildCatConnection connection)
            : base(connection)
        {
        }

        /// <summary>The items in an inventory; "" names the main inventory.</summary>
        public async Task<IReadOnlyList<InventoryItem>> GetAsync(string inventory = "", CancellationToken cancellationToken = default(CancellationToken))
        {
            GetInventoryResponseMessage answer = await Request<GetInventoryResponseMessage>(WildCatMsgType.GetInventoryRequest,
                new StringMessage { value = inventory ?? "" }, WildCatMsgType.GetInventoryResponse, cancellationToken).ConfigureAwait(false);
            return Items(answer.items);
        }

        /// <summary>
        /// Removes (spends) items and returns what is left. Error code 1, changing nothing, when the
        /// inventory does not hold that many.
        /// </summary>
        public async Task<IReadOnlyList<InventoryItem>> RemoveAsync(string inventory, IEnumerable<InventoryItem> items,
            CancellationToken cancellationToken = default(CancellationToken))
        {
            if (items == null)
                throw new ArgumentNullException(nameof(items));
            List<(string, int)> changes = new List<(string, int)>();
            foreach (InventoryItem item in items)
            {
                RequireText(item.Name, "item name");
                if (item.Count < 1)
                    throw new ArgumentException("Counts to remove must be positive.", nameof(items));
                changes.Add((item.Name, item.Count));
            }
            GetInventoryResponseMessage answer = await Request<GetInventoryResponseMessage>(WildCatMsgType.RemoveInventoryItemRequest,
                new RemoveItemInventoryRequestMessage { inventoryName = inventory ?? "", items = changes.ToArray() },
                WildCatMsgType.RemoveInventoryItemResponse, cancellationToken).ConfigureAwait(false);
            return Items(answer.items);
        }

        /// <summary>Unlocks a time-locked inventory (packages) and returns its items.</summary>
        public async Task<IReadOnlyList<InventoryItem>> UnlockAsync(string inventory, CancellationToken cancellationToken = default(CancellationToken))
        {
            GetInventoryResponseMessage answer = await Request<GetInventoryResponseMessage>(WildCatMsgType.UnlockInventoryRequest,
                new StringMessage { value = inventory ?? "" }, WildCatMsgType.UnlockInventoryResponse, cancellationToken).ConfigureAwait(false);
            return Items(answer.items);
        }

        /// <summary>How long until a time-locked inventory can be unlocked.</summary>
        public async Task<TimeSpan> GetUnlockTimeAsync(string inventory, CancellationToken cancellationToken = default(CancellationToken))
        {
            GetInventoryUnlockTimeResponseMessage answer = await Request<GetInventoryUnlockTimeResponseMessage>(
                WildCatMsgType.GetInventoryUnlockTimeRequest, new StringMessage { value = inventory ?? "" },
                WildCatMsgType.GetInventoryUnlockTimeResponse, cancellationToken).ConfigureAwait(false);
            return answer.time;
        }

        private static List<InventoryItem> Items((string, int)[] items)
        {
            List<InventoryItem> result = new List<InventoryItem>();
            if (items == null)
                return result;
            foreach ((string, int) item in items)
                result.Add(new InventoryItem(item.Item1, item.Item2));
            return result;
        }
    }
}
