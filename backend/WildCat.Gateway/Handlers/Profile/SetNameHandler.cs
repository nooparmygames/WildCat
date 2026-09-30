using System.Threading.Tasks;
using WildCat.Protocol;

namespace WildCat.Gateway
{
    /// <summary>
    /// SetName (38 -> 39): sets the logged in player's display name. Any string up to
    /// <see cref="InputLimits.MaxNameLength"/> characters is accepted, "" included (a longer one is
    /// error code 1), and names are not unique (the name to account map is never written, see bug
    /// 20 in docs/legacy-backend-notes.md).
    /// </summary>
    internal sealed class SetNameHandler : RequestHandler<StringMessage, EmptyMessage>
    {
        public override WildCatMsgType RequestType => WildCatMsgType.SetNameRequest;
        public override WildCatMsgType ResponseType => WildCatMsgType.SetNameResponse;

        protected override async Task<EmptyMessage> HandleAsync(MessageContext context, StringMessage request)
        {
            await context.Player.SetPlayerName(InputLimits.Name(request.value, "name"));
            return new EmptyMessage();
        }
    }
}
