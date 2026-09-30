using System;
using System.Threading.Tasks;
using WildCat.Interfaces;
using WildCat.Protocol;

namespace WildCat.Gateway
{
    /// <summary>
    /// UnlockInventory (119 -> 120): opens a named inventory (a chest) and moves everything in it
    /// to the player's main inventory; answers with the items that were moved. An inventory with
    /// a size is a timed chest that unlocks three days after its first item arrived; one without
    /// a size unlocks at once. Error code 1 while the chest is still locked (the legacy code
    /// answered the grain's exception text with code -1).
    /// </summary>
    internal sealed class UnlockInventoryHandler : RequestHandler<StringMessage, GetInventoryResponseMessage>
    {
        public const string StillLocked = "The inventory cannot be unlocked yet.";

        public override WildCatMsgType RequestType => WildCatMsgType.UnlockInventoryRequest;
        public override WildCatMsgType ResponseType => WildCatMsgType.UnlockInventoryResponse;

        protected override async Task<GetInventoryResponseMessage> HandleAsync(MessageContext context, StringMessage request)
        {
            IPlayerInventory inventory = Inventories.Get(context, request.value);
            var (_, timeLeft) = await inventory.GetUnlocktime();
            if (timeLeft > TimeSpan.Zero)
                throw new RequestFailedException(GatewayErrors.Refused, StillLocked);

            PlayerInventoryState.InInventoryItemState[] unlocked;
            try
            {
                unlocked = await inventory.Unlock();
            }
            catch (InvalidOperationException)
            {
                // The grain's own check, for an item that restarted the timer since the one above.
                throw new RequestFailedException(GatewayErrors.Refused, StillLocked);
            }
            return new GetInventoryResponseMessage { items = await Inventories.NamesAndCounts(unlocked) };
        }
    }
}
