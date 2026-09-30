using System.Threading.Tasks;
using WildCat.Protocol;

namespace WildCat.Gateway
{
    /// <summary>
    /// GetCloudData (30 -> 31): the logged in player's private value for a key, "" when the key
    /// has none (the answer does not tell a missing value from an empty one). The Player grain
    /// reads the storage of the game it last logged in to.
    /// </summary>
    internal sealed class GetCloudDataHandler : RequestHandler<StringMessage, StringMessage>
    {
        public override WildCatMsgType RequestType => WildCatMsgType.GetCloudDataRequest;
        public override WildCatMsgType ResponseType => WildCatMsgType.GetCloudDataResponse;

        protected override async Task<StringMessage> HandleAsync(MessageContext context, StringMessage request)
        {
            var (_, value) = await context.Player.GetPlayerString(request.value);
            return new StringMessage { value = value };
        }
    }
}
