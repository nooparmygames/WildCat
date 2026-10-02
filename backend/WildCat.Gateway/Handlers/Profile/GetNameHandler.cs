using System.Threading.Tasks;
using WildCat.Protocol;

namespace WildCat.Gateway
{
    /// <summary>
    /// GetName (36 -> 37): the logged in player's display name, "" when none was set.
    /// </summary>
    internal sealed class GetNameHandler : RequestHandler<EmptyMessage, StringMessage>
    {
        public override WildCatMsgType RequestType => WildCatMsgType.GetNameRequest;
        public override WildCatMsgType ResponseType => WildCatMsgType.GetNameResponse;

        protected override async Task<StringMessage> HandleAsync(MessageContext context, EmptyMessage request)
        {
            return new StringMessage { value = await context.Player.GetPlayerName() };
        }
    }
}
