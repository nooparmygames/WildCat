using System.Threading.Tasks;
using WildCat.Protocol;

namespace WildCat.Gateway
{
    /// <summary>
    /// SetCloudData (32 -> 33): stores a private value of the logged in player in the session's
    /// game (<see cref="PlayerData"/>), read back with GetCloudData and by the game's script events.
    /// Error code 1 for a key longer than <see cref="InputLimits.MaxKeyLength"/> or a value longer
    /// than <see cref="InputLimits.MaxValueLength"/> characters.
    /// </summary>
    internal sealed class SetCloudDataHandler : RequestHandler<SetCloudDataRequest, EmptyMessage>
    {
        public override WildCatMsgType RequestType => WildCatMsgType.SetCloudDataRequest;
        public override WildCatMsgType ResponseType => WildCatMsgType.SetCloudDataResponse;

        protected override async Task<EmptyMessage> HandleAsync(MessageContext context, SetCloudDataRequest request)
        {
            await PlayerData.Own(context).SetPlayerString(InputLimits.Key(request.key), InputLimits.Value(request.value));
            return new EmptyMessage();
        }
    }
}
