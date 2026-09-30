using System.Threading.Tasks;
using WildCat.Protocol;

namespace WildCat.Gateway
{
    /// <summary>
    /// GetInventory (109 -> 110): the items and counts in one of the player's inventories; the
    /// payload is the inventory name, empty for the main inventory.
    /// </summary>
    internal sealed class GetInventoryHandler : RequestHandler<StringMessage, GetInventoryResponseMessage>
    {
        public override WildCatMsgType RequestType => WildCatMsgType.GetInventoryRequest;
        public override WildCatMsgType ResponseType => WildCatMsgType.GetInventoryResponse;

        protected override async Task<GetInventoryResponseMessage> HandleAsync(MessageContext context, StringMessage request)
        {
            var inventory = await Inventories.Get(context, request.value).GetInventory();
            return new GetInventoryResponseMessage { items = await Inventories.NamesAndCounts(inventory?.items) };
        }
    }
}
