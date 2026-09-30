using System.Threading.Tasks;
using WildCat.Protocol;

namespace WildCat.Gateway
{
    /// <summary>
    /// GetPublicCloudData (74 -> 75): the logged in player's own public value for a key, with
    /// hasValue false when the key has none.
    /// </summary>
    internal sealed class GetPublicCloudDataHandler : RequestHandler<StringMessage, GetPublicCloudDataResponse>
    {
        public override WildCatMsgType RequestType => WildCatMsgType.GetPublicCloudDataRequest;
        public override WildCatMsgType ResponseType => WildCatMsgType.GetPublicCloudDataResponse;

        protected override async Task<GetPublicCloudDataResponse> HandleAsync(MessageContext context, StringMessage request)
        {
            var (hasValue, value) = await context.Player.GetPlayerPublicString(request.value);
            var response = new GetPublicCloudDataResponse { hasValue = hasValue };
            if (hasValue)
                response.value = value;
            return response;
        }
    }
}
