using System.Threading.Tasks;
using WildCat.Protocol;

namespace WildCat.Gateway
{
    /// <summary>
    /// SetPublicCloudData (72 -> 73): stores a public value of the logged in player in the
    /// session's game (<see cref="PlayerData"/>), which other players of that game read with
    /// GetPlayerPublicCloudData and see in their friend lists. Error code 1 for a key longer than
    /// <see cref="InputLimits.MaxKeyLength"/> or a value longer than
    /// <see cref="InputLimits.MaxValueLength"/> characters.
    /// </summary>
    internal sealed class SetPublicCloudDataHandler : RequestHandler<SetCloudDataRequest, EmptyMessage>
    {
        public override WildCatMsgType RequestType => WildCatMsgType.SetPublicCloudDataRequest;
        public override WildCatMsgType ResponseType => WildCatMsgType.SetPublicCloudDataResponse;

        protected override async Task<EmptyMessage> HandleAsync(MessageContext context, SetCloudDataRequest request)
        {
            await PlayerData.Own(context).SetPlayerPublicString(InputLimits.Key(request.key), InputLimits.Value(request.value));
            return new EmptyMessage();
        }
    }
}
