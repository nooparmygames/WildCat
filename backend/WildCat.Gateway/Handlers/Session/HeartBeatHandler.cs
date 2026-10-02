using System.Threading.Tasks;
using WildCat.Protocol;

namespace WildCat.Gateway
{
    /// <summary>
    /// HeartBeat (100 -> 101): the client says it is still there. Keeps the player grain's
    /// presence (it marks the player offline after 20 seconds without one) and, through the
    /// dispatcher's session check, the session alive.
    /// </summary>
    internal sealed class HeartBeatHandler : RequestHandler<EmptyMessage, EmptyMessage>
    {
        public override WildCatMsgType RequestType => WildCatMsgType.HeartBeatRequest;
        public override WildCatMsgType ResponseType => WildCatMsgType.HeartBeatResponse;

        protected override async Task<EmptyMessage> HandleAsync(MessageContext context, EmptyMessage request)
        {
            await context.Player.HeartBeat();
            return new EmptyMessage();
        }
    }
}
