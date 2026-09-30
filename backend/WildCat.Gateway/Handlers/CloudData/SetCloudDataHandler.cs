using System.Threading.Tasks;
using WildCat.Protocol;

namespace WildCat.Gateway
{
    /// <summary>
    /// SetCloudData (32 -> 33): stores a private value of the logged in player, read back with
    /// GetCloudData and by the player's own script events.
    /// </summary>
    internal sealed class SetCloudDataHandler : RequestHandler<SetCloudDataRequest, EmptyMessage>
    {
        public override WildCatMsgType RequestType => WildCatMsgType.SetCloudDataRequest;
        public override WildCatMsgType ResponseType => WildCatMsgType.SetCloudDataResponse;

        protected override async Task<EmptyMessage> HandleAsync(MessageContext context, SetCloudDataRequest request)
        {
            await context.Player.SetPlayerString(request.key, request.value);
            return new EmptyMessage();
        }
    }
}
