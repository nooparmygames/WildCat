using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using WildCat.Protocol;

namespace WildCat
{
    /// <summary>The friend list, invitations and the friend events the server pushes.</summary>
    public sealed class FriendsClient : FeatureClient
    {
        internal FriendsClient(WildCatConnection connection)
            : base(connection)
        {
            connection.RegisterHandler(WildCatMsgType.FriendshipInvitationEvent, frame => OnFriendEvent(frame, InvitationReceived));
            connection.RegisterHandler(WildCatMsgType.FriendshipAcceptedEvent, frame => OnFriendEvent(frame, InvitationAccepted));
            connection.RegisterHandler(WildCatMsgType.FriendshipDeniedEvent, frame => OnFriendEvent(frame, InvitationDenied));
            connection.RegisterHandler(WildCatMsgType.FriendStatusChangedEvent, frame => OnFriendEvent(frame, StatusChanged));
        }

        /// <summary>Another player invited the logged in player.</summary>
        public event Action<FriendEvent> InvitationReceived;

        /// <summary>A player accepted the logged in player's invitation.</summary>
        public event Action<FriendEvent> InvitationAccepted;

        /// <summary>A player denied the logged in player's invitation.</summary>
        public event Action<FriendEvent> InvitationDenied;

        /// <summary>A friend came online or went offline.</summary>
        public event Action<FriendEvent> StatusChanged;

        /// <summary>
        /// The friend list and the invitations waiting for an answer. <paramref name="publicDataKeys"/>
        /// names public cloud data keys to read for every friend (keep the list short for big lists).
        /// </summary>
        public async Task<FriendList> GetAsync(string[] publicDataKeys = null, CancellationToken cancellationToken = default(CancellationToken))
        {
            FriendlistCollection answer = await Request<FriendlistCollection>(WildCatMsgType.GetFriendsRequest,
                new GetFriendsRequestMessage { publicKeys = publicDataKeys ?? new string[0] },
                WildCatMsgType.GetFriendsResponse, cancellationToken).ConfigureAwait(false);

            List<Friend> friends = new List<Friend>();
            foreach (FriendlistEntry entry in answer.friends)
                friends.Add(new Friend(entry.userId, entry.name, entry.status, entry.data));
            return new FriendList(friends, Invitations(answer.sentInvitation), Invitations(answer.receivedInvitations));
        }

        private static List<FriendInvitationInfo> Invitations(FriendInvitationCollection collection)
        {
            List<FriendInvitationInfo> result = new List<FriendInvitationInfo>();
            if (collection == null)
                return result;
            foreach (FriendInvitation invitation in collection.invitations)
                result.Add(new FriendInvitationInfo(invitation.OtherPlayerId, invitation.otherPlayerName, invitation.creationTime, invitation.data));
            return result;
        }

        /// <summary>Invites a player by account id. Error code 1 when no such player exists.</summary>
        public Task InviteByPlayerIdAsync(string playerId, byte[] data = null, CancellationToken cancellationToken = default(CancellationToken))
        {
            return InviteAsync(InvitationType.AccountId, playerId, data, cancellationToken);
        }

        /// <summary>Invites a player by the user name of their user name login.</summary>
        public Task InviteByUserNameAsync(string userName, byte[] data = null, CancellationToken cancellationToken = default(CancellationToken))
        {
            return InviteAsync(InvitationType.UserId, userName, data, cancellationToken);
        }

        /// <summary>Invites a player by the email address of their email login.</summary>
        public Task InviteByEmailAsync(string email, byte[] data = null, CancellationToken cancellationToken = default(CancellationToken))
        {
            return InviteAsync(InvitationType.Email, email, data, cancellationToken);
        }

        /// <summary>Invites a player by display name.</summary>
        public Task InviteByDisplayNameAsync(string displayName, byte[] data = null, CancellationToken cancellationToken = default(CancellationToken))
        {
            return InviteAsync(InvitationType.playerName, displayName, data, cancellationToken);
        }

        private Task InviteAsync(InvitationType type, string value, byte[] data, CancellationToken cancellationToken)
        {
            RequireText(value, "value");
            return Request<EmptyMessage>(WildCatMsgType.InviteFriendRequest,
                new InviteFriendRequest { type = type, value = value, data = data ?? new byte[0] },
                WildCatMsgType.InviteFriendResponse, cancellationToken);
        }

        /// <summary>Accepts the invitation from <paramref name="playerId"/>.</summary>
        public Task AcceptAsync(string playerId, byte[] data = null, CancellationToken cancellationToken = default(CancellationToken))
        {
            RequireText(playerId, "playerId");
            return Request<EmptyMessage>(WildCatMsgType.AcceptFriendRequest,
                new StringAndByteArrayMessage { stringValue = playerId, byteArrayValue = data ?? new byte[0] },
                WildCatMsgType.AcceptFriendResponse, cancellationToken);
        }

        /// <summary>Denies the invitation from <paramref name="playerId"/>.</summary>
        public Task DenyAsync(string playerId, byte[] data = null, CancellationToken cancellationToken = default(CancellationToken))
        {
            RequireText(playerId, "playerId");
            return Request<EmptyMessage>(WildCatMsgType.DenyFriendRequest,
                new StringAndByteArrayMessage { stringValue = playerId, byteArrayValue = data ?? new byte[0] },
                WildCatMsgType.DenyFriendResponse, cancellationToken);
        }

        private void OnFriendEvent(WildCatMessageBase frame, Action<FriendEvent> handler)
        {
            OptionalResponse<FriendlistMessage> pushed = frame.GetPayload<OptionalResponse<FriendlistMessage>>();
            if (!pushed.isSuccesful || pushed.msg == null)
                return;
            FriendlistMessage message = pushed.msg;
            Raise(handler, new FriendEvent(message.friend, message.friendName, message.status, message.data));
        }
    }
}
