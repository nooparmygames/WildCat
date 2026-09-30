using System.Threading.Tasks;
using WildCat.Protocol;

namespace WildCat.Gateway
{
    /// <summary>
    /// GetPublicCloudData (74 -> 75): the logged in player's own public value for a key in the
    /// session's game, with hasValue false when the key has none. Error code 1 for a key longer
    /// than <see cref="InputLimits.MaxKeyLength"/>.
    /// </summary>
    internal sealed class GetPublicCloudDataHandler : RequestHandler<StringMessage, GetPublicCloudDataResponse>
    {
        public override WildCatMsgType RequestType => WildCatMsgType.GetPublicCloudDataRequest;
        public override WildCatMsgType ResponseType => WildCatMsgType.GetPublicCloudDataResponse;

        protected override async Task<GetPublicCloudDataResponse> HandleAsync(MessageContext context, StringMessage request)
        {
            var (hasValue, value) = await PlayerData.Own(context).GetPlayerPublicString(InputLimits.Key(request.value));
            var response = new GetPublicCloudDataResponse { hasValue = hasValue };
            if (hasValue)
                response.value = value;
            return response;
        }
    }
}
