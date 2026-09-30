using System.Threading.Tasks;
using WildCat.Protocol;

namespace WildCat.Gateway
{
    /// <summary>
    /// SetPublicCloudData (72 -> 73): stores a public value of the logged in player, which other
    /// players read with GetPlayerPublicCloudData and see in their friend lists.
    /// </summary>
    internal sealed class SetPublicCloudDataHandler : RequestHandler<SetCloudDataRequest, EmptyMessage>
    {
        public override WildCatMsgType RequestType => WildCatMsgType.SetPublicCloudDataRequest;
        public override WildCatMsgType ResponseType => WildCatMsgType.SetPublicCloudDataResponse;

        protected override async Task<EmptyMessage> HandleAsync(MessageContext context, SetCloudDataRequest request)
        {
            await context.Player.SetPlayerPublicString(request.key, request.value);
            return new EmptyMessage();
        }
    }
}
