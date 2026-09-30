using System.Threading.Tasks;
using WildCat.Interfaces;
using WildCat.Protocol;

namespace WildCat.Gateway
{
    /// <summary>
    /// DenyFriend (96 -> 97): turns down the friend invitation of the player whose account id is
    /// the request's string. The inviter gets a FriendshipDeniedEvent (98) with the request's
    /// bytes. Denying when there is no invitation succeeds and tells nobody; an empty id is error
    /// code 1.
    /// </summary>
    internal sealed class DenyFriendHandler : RequestHandler<StringAndByteArrayMessage, EmptyMessage>
    {
        public override WildCatMsgType RequestType => WildCatMsgType.DenyFriendRequest;
        public override WildCatMsgType ResponseType => WildCatMsgType.DenyFriendResponse;

        protected override async Task<EmptyMessage> HandleAsync(MessageContext context, StringAndByteArrayMessage request)
        {
            if (string.IsNullOrWhiteSpace(request.stringValue))
                throw new RequestFailedException(GatewayErrors.Refused, FriendListException.NoInvitation);
            var inviter = context.Grains.GetGrain<IPlayer>(request.stringValue);
            await Friends.RefusalsAsErrors(Friends.ListOf(context).DenyInvitation(inviter, request.byteArrayValue));
            return new EmptyMessage();
        }
    }
}
