using System.Threading.Tasks;
using WildCat.Protocol;

namespace WildCat.Gateway
{
    /// <summary>
    /// GetPlayerPublicCloudData (70 -> 71): another player's public value for a key in the
    /// session's game, with hasValue false when the key (or the player) has none. Error code 1 for
    /// an empty player id, and for an id or key longer than 128 characters. (The old code read the
    /// storage of the game the other player had last logged in to, which could belong to another
    /// developer.)
    /// </summary>
    internal sealed class GetPlayerPublicCloudDataHandler
        : RequestHandler<GetPlayerPublicCloudDataRequest, GetPlayerPublicCloudDataResponse>
    {
        public override WildCatMsgType RequestType => WildCatMsgType.GetPlayerPublicCloudDataRequest;
        public override WildCatMsgType ResponseType => WildCatMsgType.GetPlayerPublicCloudDataResponse;

        protected override async Task<GetPlayerPublicCloudDataResponse> HandleAsync(MessageContext context,
            GetPlayerPublicCloudDataRequest request)
        {
            string playerId = RequestedPlayer.Get(context, request.player).GetPrimaryKeyString();
            var (hasValue, value) = await PlayerData.Of(context, playerId).GetPlayerPublicString(InputLimits.Key(request.key));
            var response = new GetPlayerPublicCloudDataResponse { hasValue = hasValue };
            if (hasValue)
                response.value = value;
            return response;
        }
    }
}
