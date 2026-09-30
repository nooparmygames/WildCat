using System.Threading.Tasks;
using WildCat.Protocol;

namespace WildCat.Gateway
{
    /// <summary>
    /// InviteFriend (90 -> 91): invites the player named by account id, email or user name (player
    /// names never match yet) to the logged in player's friend list in the session's game. The
    /// invitee gets a FriendshipInvitationEvent (92) with the inviter and the request's data.
    /// Error code 1 when nobody matches ("Player not found."), for the player themselves and for a
    /// friend. Inviting a player who has invited you already records nothing and succeeds.
    /// </summary>
    internal sealed class InviteFriendHandler : RequestHandler<InviteFriendRequest, EmptyMessage>
    {
        public override WildCatMsgType RequestType => WildCatMsgType.InviteFriendRequest;
        public override WildCatMsgType ResponseType => WildCatMsgType.InviteFriendResponse;

        protected override async Task<EmptyMessage> HandleAsync(MessageContext context, InviteFriendRequest request)
        {
            var invitee = await Friends.FindAsync(context.Grains, request.type, request.value);
            if (invitee == null)
                throw new RequestFailedException(GatewayErrors.Refused, Friends.PlayerNotFound);
            string inviterName = await context.Player.GetPlayerName();
            await Friends.RefusalsAsErrors(Friends.ListOf(context).Invite(invitee, inviterName, request.data));
            return new EmptyMessage();
        }
    }
}
