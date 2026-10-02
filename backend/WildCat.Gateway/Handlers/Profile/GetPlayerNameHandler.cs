using System.Threading.Tasks;
using WildCat.Protocol;

namespace WildCat.Gateway
{
    /// <summary>
    /// GetPlayerName (68 -> 69): another player's display name by account id; "" when that player
    /// set none or does not exist. Error code 1 for an empty id.
    /// </summary>
    internal sealed class GetPlayerNameHandler : RequestHandler<StringMessage, StringMessage>
    {
        public override WildCatMsgType RequestType => WildCatMsgType.GetPlayerNameRequest;
        public override WildCatMsgType ResponseType => WildCatMsgType.GetPlayerNameResponse;

        protected override async Task<StringMessage> HandleAsync(MessageContext context, StringMessage request)
        {
            return new StringMessage { value = await RequestedPlayer.Get(context, request.value).GetPlayerName() };
        }
    }
}
