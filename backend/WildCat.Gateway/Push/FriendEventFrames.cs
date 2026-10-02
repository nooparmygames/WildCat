using System;
using WildCat.Interfaces;
using WildCat.Protocol;

namespace WildCat.Gateway
{
    /// <summary>
    /// Turns an event from a player's FriendEvents stream into the frame pushed to the client:
    /// FriendshipInvitationEvent (92), FriendshipAcceptedEvent (95), FriendshipDeniedEvent (98) or
    /// FriendStatusChangedEvent (99), each an OptionalResponse&lt;FriendlistMessage&gt;.
    /// </summary>
    internal static class FriendEventFrames
    {
        public static WildCatMessageBase Build(FriendListEventData data)
        {
            var friend = new FriendlistMessage
            {
                friend = data.Friend?.GetPrimaryKeyString() ?? "",
                friendName = data.FriendName ?? "",
                data = data.Data,
                status = (WildCat.Protocol.OnlineStatus)data.OnlineStatus,
            };
            WildCatMsgType type;
            switch (data.Type)
            {
                case FriendListEventData.EventType.Accept:
                    type = WildCatMsgType.FriendshipAcceptedEvent;
                    break;
                case FriendListEventData.EventType.Deny:
                    type = WildCatMsgType.FriendshipDeniedEvent;
                    break;
                case FriendListEventData.EventType.Invite:
                    type = WildCatMsgType.FriendshipInvitationEvent;
                    break;
                case FriendListEventData.EventType.StateChange:
                    type = WildCatMsgType.FriendStatusChangedEvent;
                    break;
                default:
                    throw new ArgumentException($"{data.Type} is not a friend event type.", nameof(data));
            }
            return WildCatMessageBase.Create(OptionalResponse<FriendlistMessage>.CreateSuccessful(friend), type,
                WildCatMsgHeader.normal);
        }
    }
}
