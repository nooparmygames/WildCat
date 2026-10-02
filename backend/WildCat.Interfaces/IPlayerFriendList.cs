using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using System.Linq;
using Orleans;
using Orleans.Concurrency;

namespace WildCat.Interfaces
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

        /// <summary>
        /// A friend's status changed. Interleaves with every other call: it only updates the
        /// friend's entry in memory and pushes the change, and it arrives while this list may be
        /// waiting on that friend (GetFriends asks the friend's player for public data, SetStatus
        /// tells the friend's list), which deadlocked both.
        /// </summary>
        [AlwaysInterleave]
        Task SetFriendStatus(IPlayer friend, OnlineStatus status);
    }

    [Serializable]
    [GenerateSerializer]
    [Alias("wildcat.FriendListState")]
    public class FriendListState
    {
        [Id(0)] public List<FriendListEntry> Friends { get; set; }
        [Id(1)] public List<FriendListInvitation> SentInvitations { get; set; }
        [Id(2)] public List<FriendListInvitation> ReceivedInvitations { get; set; }
        [Id(3)] public OnlineStatus Status { get; set; }

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
            // Keyed by the sender: every received invitation has this player as its receiver, so
            // the old match on Receiver never found one and repeated invitations piled up.
            var invitation = ReceivedInvitations.FirstOrDefault(x => x.Sender.GetPrimaryKeyString() == other.GetPrimaryKeyString());
            if (invitation.Sender == null)
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

    [GenerateSerializer]
    [Alias("wildcat.FriendListData")]
    public class FriendListData
    {
        [Id(0)] public List<FriendListEntry> Friends { get; set; }
        [Id(1)] public List<FriendListInvitation> SentInvitations { get; set; }
        [Id(2)] public List<FriendListInvitation> ReceivedInvitations { get; set; }
        [Id(3)] public Dictionary<string, Dictionary<string, string>> publicData { get; set; }

        public FriendListData()
        {
            publicData = new Dictionary<string, Dictionary<string, string>>();
        }
    }

    [Serializable]
    [GenerateSerializer]
    [Alias("wildcat.FriendListEntry")]
    public class FriendListEntry
    {
        [Id(0)] public IPlayer Player { get; set; }
        [Id(1)] public string Name { get; set; }
        [Id(2)] public OnlineStatus OnlineStatus;
        [Id(3)] public Dictionary<string, byte[]> AdditionalData { get; set; }
        [Id(4)] public Dictionary<string, string> AdditionalStringData { get; set; }

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
    [GenerateSerializer, Alias("wildcat.OnlineStatus")]
    public enum OnlineStatus : byte { Online, Offline }

    [Serializable]
    [GenerateSerializer]
    [Alias("wildcat.FriendListEventData")]
    public struct FriendListEventData
    {
        [GenerateSerializer, Alias("wildcat.FriendListEventData.EventType")]
        public enum EventType { Invite, Accept, Deny, StateChange }
        [Id(0)] public IPlayer Friend { get; set; }
        [Id(1)] public EventType Type { get; set; }
        [Id(2)] public byte[] Data { get; set; }
        [Id(3)] public OnlineStatus OnlineStatus { get; set; }
        [Id(4)] public string FriendName { get; set; }
        [Id(5)] public Guid GameKey { get; set; }

        public override string ToString()
        {
            return $"{Type} {Data} {Friend} {FriendName}";
        }
    }

    [Serializable]
    [GenerateSerializer]
    [Alias("wildcat.FriendListInvitation")]
    public struct FriendListInvitation
    {
        [Id(0)] public byte[] data { get; set; }
        [Id(1)] public IPlayer Sender { get; set; }
        [Id(2)] public IPlayer Receiver { get; set; }
        [Id(3)] public DateTime CreationDate { get; set; }
        [Id(4)] public string SenderName { get; set; }
        [Id(5)] public string ReceiverName { get; set; }
    }
}
