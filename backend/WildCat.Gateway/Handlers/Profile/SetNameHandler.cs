using System.Threading.Tasks;
using WildCat.Protocol;

namespace WildCat.Gateway
{
    /// <summary>
    /// SetName (38 -> 39): sets the logged in player's display name. Any string is accepted, ""
    /// included, and names are not unique (the name to account map is never written, see bug 20
    /// in docs/legacy-backend-notes.md).
    /// </summary>
    internal sealed class SetNameHandler : RequestHandler<StringMessage, EmptyMessage>
    {
        public override WildCatMsgType RequestType => WildCatMsgType.SetNameRequest;
        public override WildCatMsgType ResponseType => WildCatMsgType.SetNameResponse;

        protected override async Task<EmptyMessage> HandleAsync(MessageContext context, StringMessage request)
        {
            await context.Player.SetPlayerName(request.value);
            return new EmptyMessage();
        }
    }
}
