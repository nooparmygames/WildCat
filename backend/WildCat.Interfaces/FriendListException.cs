using System;

namespace WildCat.Interfaces
{
    /// <summary>
    /// Thrown by <see cref="IPlayerFriendList"/> for a request it refuses: inviting yourself or a
    /// friend, or accepting an invitation that was never sent. The message is written for clients;
    /// the gateway answers it with error code 1.
    /// </summary>
    [Serializable]
    [GenerateSerializer]
    [Alias("wildcat.FriendListException")]
    public class FriendListException : Exception
    {
        public const string CannotInviteYourself = "You cannot invite yourself.";
        public const string AlreadyFriends = "That player is already your friend.";
        public const string NoInvitation = "That player has not invited you.";

        public FriendListException()
        {
        }

        public FriendListException(string message) : base(message)
        {
        }

        public FriendListException(string message, Exception innerException) : base(message, innerException)
        {
        }
    }
}
