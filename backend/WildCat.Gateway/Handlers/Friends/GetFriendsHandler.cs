using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using WildCat.Protocol;

namespace WildCat.Gateway
{
    /// <summary>
    /// GetFriends (88 -> 89): the logged in player's friends in the session's game, each with
    /// their online status and the public data keys the request names (at most
    /// <see cref="WireLimits.MaxPublicDataKeys"/>, each up to <see cref="InputLimits.MaxKeyLength"/>
    /// characters, or error code 1), plus the friend invitations the player sent and received.
    /// </summary>
    /// <remarks>
    /// The public data comes from each friend's storage in the session's game
    /// (<see cref="PlayerData"/>). The friend list grain used to read it through the friend's
    /// Player grain, which took the game that friend had last logged in to, possibly one of
    /// another developer; the list is now asked for no keys and only gives the statuses.
    /// </remarks>
    internal sealed class GetFriendsHandler : RequestHandler<GetFriendsRequestMessage, FriendlistCollection>
    {
        public override WildCatMsgType RequestType => WildCatMsgType.GetFriendsRequest;
        public override WildCatMsgType ResponseType => WildCatMsgType.GetFriendsResponse;

        protected override async Task<FriendlistCollection> HandleAsync(MessageContext context, GetFriendsRequestMessage request)
        {
            string[] keys = (request.publicKeys ?? new string[0]).Distinct().ToArray();
            foreach (var key in keys)
                InputLimits.Key(key);

            var list = await Friends.ListOf(context).GetFriends(new string[0]);
            var friendIds = list.Friends.Select(f => f.Player.GetPrimaryKeyString()).Distinct().ToList();
            var publicData = new Dictionary<string, Dictionary<string, string>>();
            if (keys.Length > 0)
            {
                var reads = friendIds.Select(async id => (Id: id, Data: await PlayerData.Of(context, id).GetPlayerPublicStrings(keys)));
                foreach (var (id, data) in await Task.WhenAll(reads))
                    publicData[id] = data;
            }

            var answer = new FriendlistCollection();
            foreach (var friend in list.Friends)
            {
                string friendId = friend.Player.GetPrimaryKeyString();
                answer.friends.Add(new FriendlistEntry
                {
                    userId = friendId,
                    name = friend.Name ?? "",
                    status = (OnlineStatus)friend.OnlineStatus,
                    data = publicData.TryGetValue(friendId, out var data) && data != null
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
