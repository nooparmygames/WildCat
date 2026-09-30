using System.Threading.Tasks;
using WildCat.Protocol;

namespace WildCat.Gateway
{
    /// <summary>
    /// GetCloudData (30 -> 31): the logged in player's private value for a key in the session's
    /// game (<see cref="PlayerData"/>), "" when the key has none (the answer does not tell a
    /// missing value from an empty one). Error code 1 for a key longer than
    /// <see cref="InputLimits.MaxKeyLength"/>.
    /// </summary>
    internal sealed class GetCloudDataHandler : RequestHandler<StringMessage, StringMessage>
    {
        public override WildCatMsgType RequestType => WildCatMsgType.GetCloudDataRequest;
        public override WildCatMsgType ResponseType => WildCatMsgType.GetCloudDataResponse;

        protected override async Task<StringMessage> HandleAsync(MessageContext context, StringMessage request)
        {
            var (_, value) = await PlayerData.Own(context).GetPlayerString(InputLimits.Key(request.value));
            return new StringMessage { value = value };
        }
    }
}
