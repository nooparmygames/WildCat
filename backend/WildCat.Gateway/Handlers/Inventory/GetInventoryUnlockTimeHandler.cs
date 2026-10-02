using System;
using System.Threading.Tasks;
using WildCat.Protocol;

namespace WildCat.Gateway
{
    /// <summary>
    /// GetInventoryUnlockTime (121 -> 122): how long until a named inventory (a timed chest) can
    /// be unlocked with UnlockInventory; zero when it can be now. (The legacy answer could be
    /// negative, the time since it became unlockable.)
    /// </summary>
    internal sealed class GetInventoryUnlockTimeHandler : RequestHandler<StringMessage, GetInventoryUnlockTimeResponseMessage>
    {
        public override WildCatMsgType RequestType => WildCatMsgType.GetInventoryUnlockTimeRequest;
        public override WildCatMsgType ResponseType => WildCatMsgType.GetInventoryUnlockTimeResponse;

        protected override async Task<GetInventoryUnlockTimeResponseMessage> HandleAsync(MessageContext context, StringMessage request)
        {
            var (_, timeLeft) = await Inventories.Get(context, request.value).GetUnlocktime();
            return new GetInventoryUnlockTimeResponseMessage { time = timeLeft > TimeSpan.Zero ? timeLeft : TimeSpan.Zero };
        }
    }
}
