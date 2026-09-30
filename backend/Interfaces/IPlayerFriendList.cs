using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using System.Linq;
using Orleans;

namespace Interfaces
{
    public interface IPlayerFriendList : IGrainWithGuidCompoundKey
    {
        Task<FriendListData> GetFriends(string[] dataKeys);
        Task Invite(IPlayer player, string name, byte[] data);
        Task<bool> ReceiveInviteRequest(IPlayer player, string name, byte[] data);
        Task<string> InvitationAccepted(IPlayer accepter, string name, byte[] data);
        Task AcceptInvitation(IPlayer acceptee, byte[] data);
        Task InvitationDenied(IPlayer player, byte[] data);
        Task DenyInvitation(IPlayer other, byte[] data);
        Task AddFriend(IPlayer player, string name);
        Task<List<FriendListInvitation>> GetSentInvitations();
        Task<List<FriendListInvitation>> GetReceivedInvitations();
        Task SetStatus(string name, OnlineStatus status);
        Task<OnlineStatus> GetStatus();
        Task SetFriendStatus(IPlayer friend, OnlineStatus status);
    }

    [Serializable]
    public class FriendListState
    {
        public List<FriendListEntry> Friends { get; set; }
        public List<FriendListInvitation> SentInvitations { get; set; }
        public List<FriendListInvitation> ReceivedInvitations { get; set; }
        public OnlineStatus Status { get; set; }

        public FriendListState()
        {
            Friends = new List<FriendListEntry>();
            SentInvitations = new List<FriendListInvitation>();
            ReceivedInvitations = new List<FriendListInvitation>();
        }

        public bool AddFriendAndRemoveFromLists(IPlayer player, string name)
        {
            var currentEntry = Friends.FirstOrDefault(x => x.Player.GetPrimaryKeyString() == player.GetPrimaryKeyString());
            if (currentEntry != null)
            {
                currentEntry.Name = name;
                return false;
            }
            else
            {
                Friends.Add(new FriendListEntry
                {
                    Player = player,
                    Name = name
                });
                SentInvitations.RemoveAll(x => x.Receiver.GetPrimaryKeyString() == player.GetPrimaryKeyString());
                ReceivedInvitations.RemoveAll(x => x.Sender.GetPrimaryKeyString() == player.GetPrimaryKeyString());
                return true;
            }
        }

        public bool CanInvitationHappen(IPlayer otherPlayer)
        {
            string playerKey = otherPlayer.GetPrimaryKeyString();
            return !Friends.Any(x => x.Player.GetPrimaryKeyString() == playerKey);
        }

        public bool AddSentInvitation(IPlayer self, IPlayer other, string otherName, byte[] data)
        {
            var invitation = SentInvitations.FirstOrDefault(x => x.Receiver.GetPrimaryKeyString() == other.GetPrimaryKeyString());
            if (invitation.Sender == null)
            {
                SentInvitations.Add(new FriendListInvitation
                {
                    Receiver = other,
                    ReceiverName = otherName,
                    Sender = self,
                    CreationDate = DateTime.UtcNow,
                    data = data
                });
                return true;
            }
            else
            {
                return false;
            }
        }

        public bool AddReceivedInvitation(IPlayer self, IPlayer other, string otherName, byte[] data)
        {
            var invitation = ReceivedInvitations.FirstOrDefault(x => x.Receiver.GetPrimaryKeyString() == other.GetPrimaryKeyString());
            if (invitation.Receiver == null)
            {
                ReceivedInvitations.Add(new FriendListInvitation
                {
                    Sender = other,
                    SenderName = otherName,
                    Receiver = self,
                    CreationDate = DateTime.UtcNow,
                    data = data
                });
                return true;
            }
            else
            {
                return false;
            }
        }
    }

    public class FriendListData
    {
        public List<FriendListEntry> Friends { get; set; }
        public List<FriendListInvitation> SentInvitations { get; set; }
        public List<FriendListInvitation> ReceivedInvitations { get; set; }
        public Dictionary<string, Dictionary<string, string>> publicData { get; set; }

        public FriendListData()
        {
            publicData = new Dictionary<string, Dictionary<string, string>>();
        }
    }

    [Serializable]
    public class FriendListEntry
    {
        public IPlayer Player { get; set; }
        public string Name { get; set; }
        public OnlineStatus OnlineStatus;
        public Dictionary<string, byte[]> AdditionalData { get; set; }
        public Dictionary<string, string> AdditionalStringData { get; set; }

        public Dictionary<string, string> GetStrings()
        {
            return AdditionalStringData;
        }

        public void SetState(OnlineStatus status)
        {
            OnlineStatus = status;
        }

        public OnlineStatus GetStatus()
        {
            return OnlineStatus;
        }


    }

    /// <summary>
    /// A user's online status
    /// </summary>
    public enum OnlineStatus : byte { Online, Offline }

    [Serializable]
    public struct FriendListEventData
    {
        public enum EventType { Invite, Accept, Deny, StateChange }
        public IPlayer Friend { get; set; }
        public EventType Type { get; set; }
        public byte[] Data { get; set; }
        public OnlineStatus OnlineStatus { get; set; }
        public string FriendName { get; set; }
        public Guid GameKey { get; set; }

        public override string ToString()
        {
            return $"{Type} {Data} {Friend} {FriendName}";
        }
    }

    [Serializable]
    public struct FriendListInvitation
    {
        public byte[] data { get; set; }
        public IPlayer Sender { get; set; }
        public IPlayer Receiver { get; set; }
        public DateTime CreationDate { get; set; }
        public string SenderName { get; set; }
        public string ReceiverName { get; set; }
    }
}
