using System;
using System.Linq;
using System.Threading.Tasks;
using WildCat.Protocol;

namespace WildCat.Gateway
{
    /// <summary>
    /// RemoveInventoryItem (115 -> 116): the player spends or drops items from one of its
    /// inventories, all or nothing, and gets the inventory back. Error code 1 when the inventory
    /// does not hold that many of an item or a count is not positive.
    /// </summary>
    internal sealed class RemoveInventoryItemHandler : RequestHandler<RemoveItemInventoryRequestMessage, GetInventoryResponseMessage>
    {
        public const string NotHeld = "The inventory does not hold these items in these amounts.";

        public override WildCatMsgType RequestType => WildCatMsgType.RemoveInventoryItemRequest;
        public override WildCatMsgType ResponseType => WildCatMsgType.RemoveInventoryItemResponse;

        protected override async Task<GetInventoryResponseMessage> HandleAsync(MessageContext context,
            RemoveItemInventoryRequestMessage request)
        {
            var items = request.items ?? Array.Empty<(string, int)>();
            var inventory = Inventories.Get(context, request.inventoryName);
            bool removed = await inventory.RemoveItem(
                items.Select(x => Inventories.Item(context, x.Item1)).ToArray(),
                items.Select(x => x.Item2).ToArray());
            if (!removed)
                throw new RequestFailedException(GatewayErrors.Refused, NotHeld);
            var state = await inventory.GetInventory();
            return new GetInventoryResponseMessage { items = await Inventories.NamesAndCounts(state?.items) };
        }
    }
}
