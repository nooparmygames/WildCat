using System.Threading.Tasks;
using WildCat.Protocol;

namespace WildCat.Gateway
{
    /// <summary>
    /// GetPlayerPublicCloudData (70 -> 71): another player's public value for a key, with hasValue
    /// false when the key (or the player) has none. Error code 1 for an empty player id. The other
    /// player's grain reads the storage of the game that player last logged in to, as the old code
    /// did, not necessarily the game of the request.
    /// </summary>
    internal sealed class GetPlayerPublicCloudDataHandler
        : RequestHandler<GetPlayerPublicCloudDataRequest, GetPlayerPublicCloudDataResponse>
    {
        public override WildCatMsgType RequestType => WildCatMsgType.GetPlayerPublicCloudDataRequest;
        public override WildCatMsgType ResponseType => WildCatMsgType.GetPlayerPublicCloudDataResponse;

        protected override async Task<GetPlayerPublicCloudDataResponse> HandleAsync(MessageContext context,
            GetPlayerPublicCloudDataRequest request)
        {
            var (hasValue, value) = await RequestedPlayer.Get(context, request.player).GetPlayerPublicString(request.key);
            var response = new GetPlayerPublicCloudDataResponse { hasValue = hasValue };
            if (hasValue)
                response.value = value;
            return response;
        }
    }
}
