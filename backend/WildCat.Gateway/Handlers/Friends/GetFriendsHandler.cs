using System.Collections.Generic;
using System.Threading.Tasks;
using WildCat.Protocol;

namespace WildCat.Gateway
{
    /// <summary>
    /// GetFriends (88 -> 89): the logged in player's friends in the session's game, each with
    /// their online status and the public data keys the request names, plus the friend
    /// invitations the player sent and received.
    /// </summary>
    internal sealed class GetFriendsHandler : RequestHandler<GetFriendsRequestMessage, FriendlistCollection>
    {
        public override WildCatMsgType RequestType => WildCatMsgType.GetFriendsRequest;
        public override WildCatMsgType ResponseType => WildCatMsgType.GetFriendsResponse;

        protected override async Task<FriendlistCollection> HandleAsync(MessageContext context, GetFriendsRequestMessage request)
        {
            var list = await Friends.ListOf(context).GetFriends(request.publicKeys ?? new string[0]);
            var answer = new FriendlistCollection();
            foreach (var friend in list.Friends)
            {
                string friendId = friend.Player.GetPrimaryKeyString();
                answer.friends.Add(new FriendlistEntry
                {
                    userId = friendId,
                    name = friend.Name ?? "",
                    status = (OnlineStatus)friend.OnlineStatus,
                    data = list.publicData.TryGetValue(friendId, out var data) && data != null
                        ? data
                        : new Dictionary<string, string>(),
                });
            }

            foreach (var invitation in list.ReceivedInvitations)
            {
                answer.receivedInvitations.invitations.Add(new FriendInvitation
                {
                    creationTime = invitation.CreationDate,
                    data = invitation.data ?? new byte[0],
                    OtherPlayerId = invitation.Sender.GetPrimaryKeyString(),
                    otherPlayerName = invitation.SenderName ?? "",
                });
            }

            foreach (var invitation in list.SentInvitations)
            {
                answer.sentInvitation.invitations.Add(new FriendInvitation
                {
                    creationTime = invitation.CreationDate,
                    data = invitation.data ?? new byte[0],
                    OtherPlayerId = invitation.Receiver.GetPrimaryKeyString(),
                    otherPlayerName = invitation.ReceiverName ?? "",
                });
            }
            return answer;
        }
    }
}
