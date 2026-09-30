using System.Threading.Tasks;
using WildCat.Interfaces;
using WildCat.Protocol;

namespace WildCat.Gateway
{
    /// <summary>
    /// AcceptFriend (93 -> 94): accepts the friend invitation of the player whose account id is
    /// the request's string, with its bytes passed on to them. Both players become friends; the
    /// inviter gets a FriendshipAcceptedEvent (95). Error code 1 when that player has not invited
    /// the logged in player.
    /// </summary>
    internal sealed class AcceptFriendHandler : RequestHandler<StringAndByteArrayMessage, EmptyMessage>
    {
        public override WildCatMsgType RequestType => WildCatMsgType.AcceptFriendRequest;
        public override WildCatMsgType ResponseType => WildCatMsgType.AcceptFriendResponse;

        protected override async Task<EmptyMessage> HandleAsync(MessageContext context, StringAndByteArrayMessage request)
        {
            if (string.IsNullOrWhiteSpace(request.stringValue))
                throw new RequestFailedException(GatewayErrors.Refused, FriendListException.NoInvitation);
            var inviter = context.Grains.GetGrain<IPlayer>(InputLimits.PlayerId(request.stringValue));
            await Friends.RefusalsAsErrors(Friends.ListOf(context).AcceptInvitation(inviter, request.byteArrayValue));
            return new EmptyMessage();
        }
    }
}
