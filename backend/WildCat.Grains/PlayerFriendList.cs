using System;
using System.Linq;
using System.Collections.Generic;
using System.Threading.Tasks;
using Orleans;
using Orleans.Runtime;
using WildCat.Interfaces;
using Orleans.Concurrency;
using Orleans.Streams;

namespace WildCat.Grains
{
    
    class PlayerFriendList : Grain, IPlayerFriendList
    {
        private readonly IPersistentState<FriendListState> state;

        public PlayerFriendList([PersistentState(GrainStateNames.PlayerFriendList, GrainStorageNames.Default)] IPersistentState<FriendListState> state)
        {
            this.state = state;
        }

        private FriendListState State => state.State;

        private IPlayer player;
        private Guid gameKey;
        private IAsyncStream<FriendListEventData> friendListEvents;
        private string name;

        public override async Task OnActivateAsync(CancellationToken cancellationToken)
        {
            string playerId;
            gameKey = this.GetPrimaryKey(out playerId);
            player = GrainFactory.GetGrain<IPlayer>(playerId);
            var provider = this.GetStreamProvider(StreamNames.PROVIDER_NAME);
            friendListEvents = provider.GetStream<FriendListEventData>(StreamNames.FriendEvents(gameKey, playerId));
            State.Friends.RemoveAll(x => x.Player.GetPrimaryKeyString() == player.GetPrimaryKeyString());
            await base.OnActivateAsync(cancellationToken);
        }

        public Task AddFriend(IPlayer player, string name)
        {
            if (this.player.GetPrimaryKeyString() != player.GetPrimaryKeyString())
            {
                State.AddFriendAndRemoveFromLists(player, name);
                return state.WriteStateAsync();
            }
            return Task.CompletedTask;
        }

        public async Task AcceptInvitation(IPlayer acceptee, byte[] data)
        {
            var accepteeName = await GrainFactory.GetGrain<IPlayerFriendList>(gameKey, acceptee.GetPrimaryKeyString(), null).InvitationAccepted(player, name, data);
            await AddFriend(acceptee, accepteeName);
        }

        public async Task<string> InvitationAccepted(IPlayer accepter, string accepterName, byte[] data)
        {
            // FirstOrDefault: First threw before the check below could answer a missing invitation.
            var sentInvitation = State.SentInvitations.FirstOrDefault(x => x.Receiver.GetPrimaryKeyString() == accepter.GetPrimaryKeyString());
            if (sentInvitation.Sender == null)
                throw new FriendListException(FriendListException.NoInvitation);
            //Add friends and save for both players
            var othersFriendList = GrainFactory.GetGrain<IPlayerFriendList>(gameKey, accepter.GetPrimaryKeyString(), null);
            await AddFriend(accepter, accepterName);

            //send streams
            FriendListEventData myStreamEventData = new FriendListEventData
            {
                Friend = accepter,
                FriendName = accepterName,
                GameKey = gameKey,
                Data = data,
                Type = FriendListEventData.EventType.Accept,
            };
            friendListEvents.OnNextAsync(myStreamEventData).Ignore();
            return name;
        }

        public async Task DenyInvitation(IPlayer other, byte[] data)
        {
            await GrainFactory.GetGrain<IPlayerFriendList>(gameKey, other.GetPrimaryKeyString(), null).InvitationDenied(player, data);
            State.ReceivedInvitations.RemoveAll(x => x.Sender.GetPrimaryKeyString() == other.GetPrimaryKeyString());
            await state.WriteStateAsync();
        }

        public async Task InvitationDenied(IPlayer denyer, byte[] data)
        {
            if (State.SentInvitations.RemoveAll(x => x.Receiver.GetPrimaryKeyString() == denyer.GetPrimaryKeyString()) > 0)
            {
                FriendListEventData myStreamEventData = new FriendListEventData
                {
                    Friend = denyer,
                    GameKey = gameKey,
                    FriendName = "",//##
                    Data = data,
                    Type = FriendListEventData.EventType.Deny
                };
                await state.WriteStateAsync();
                friendListEvents.OnNextAsync(myStreamEventData).Ignore();
            }
        }

        public async Task<FriendListData> GetFriends(string[] dataKeys)
        {
            FriendListData result = new FriendListData();
            result.Friends = State.Friends;
            result.SentInvitations = State.SentInvitations;
            result.ReceivedInvitations = State.ReceivedInvitations;

            Dictionary<string, Task<Tuple<OnlineStatus, Dictionary<string, string>>>> tasks = new Dictionary<string, Task<Tuple<OnlineStatus, Dictionary<string, string>>>>();
            foreach (var friend in State.Friends)
            {
                tasks.Add(friend.Player.GetPrimaryKeyString(), friend.Player.GetPublicStringsInGame(gameKey, dataKeys));
            }
            await Task.WhenAll(tasks.Values);
            foreach (var t in tasks)
            {
                var publicStrings = await t.Value;
                result.publicData.Add(t.Key, publicStrings.Item2);
                result.Friends.FirstOrDefault(x => x.Player.GetPrimaryKeyString() == t.Key).SetState(publicStrings.Item1);
            }
            return result;
        }

        public async Task Invite(IPlayer invitee, string name, byte[] data)
        {
            if (invitee.GetPrimaryKeyString() == player.GetPrimaryKeyString())
                throw new FriendListException(FriendListException.CannotInviteYourself);
            if (!State.CanInvitationHappen(invitee))
                throw new FriendListException(FriendListException.AlreadyFriends);
            bool requestResult = await (GrainFactory.GetGrain<IPlayerFriendList>(gameKey, invitee.GetPrimaryKeyString(), null)).ReceiveInviteRequest(player, name, data);
            if (requestResult)
            {
                State.AddSentInvitation(player, invitee, await invitee.GetPlayerName(), data);
                await state.WriteStateAsync();
            }
        }

        public async Task<bool> ReceiveInviteRequest(IPlayer sender, string name, byte[] data)
        {
            if (!State.CanInvitationHappen(sender))
                throw new FriendListException(FriendListException.AlreadyFriends);
            // This player invited the sender already: the sender's invitation is not recorded
            // (the sender's Invite records nothing either), and the answer is still a success.
            var sentInvitation = State.SentInvitations.FirstOrDefault(x => x.Receiver.GetPrimaryKeyString() == sender.GetPrimaryKeyString());
            if (sentInvitation.Sender != null)
            {
                return false;
            }
            else
            {
                // A repeated invitation keeps the first one and is pushed again.
                State.AddReceivedInvitation(player, sender, name, data);
                await state.WriteStateAsync();
                FriendListEventData eventData = new FriendListEventData
                {
                    Friend = sender,
                    FriendName = name,
                    GameKey = gameKey,
                    Data = data,
                    Type = FriendListEventData.EventType.Invite
                };
                friendListEvents.OnNextAsync(eventData).Ignore();
                return true;
            }
        }

        public Task<List<FriendListInvitation>> GetSentInvitations()
        {
            return Task.FromResult(State.SentInvitations);
        }

        public Task<List<FriendListInvitation>> GetReceivedInvitations()
        {
            return Task.FromResult(State.ReceivedInvitations);
        }

        // Interleaves with every other call ([AlwaysInterleave] on IPlayerFriendList.SetFriendStatus;
        // the legacy code had it on this class method, which 3.x ignored and 10 rejects), so it
        // must not write state or change the lists.
        public async Task SetFriendStatus(IPlayer friend, OnlineStatus status)
        {
            var foundFriend = State.Friends.FirstOrDefault(x => x.Player.GetPrimaryKeyString() == friend.GetPrimaryKeyString());
            // A one-sided entry used to throw here, which failed the friend's login or logout.
            if (foundFriend == null)
                return;
            foundFriend.SetState(status);
            await friendListEvents.OnNextAsync(new FriendListEventData
            {
                Type = FriendListEventData.EventType.StateChange,
                OnlineStatus = status,
                Friend = friend,
                GameKey = gameKey
            });
        }

        // The legacy code had [AlwaysInterleave] here too; SetFriendStatus interleaving is enough
        // to break the cycle of two friends changing status at once.
        public async Task SetStatus(string name,OnlineStatus status)
        {
            this.name = name;
            List<Task> tasks = new List<Task>();
            State.Status = status;
            for (int i = 0; i < State.Friends.Count; ++i)
            {
                string pk = State.Friends[i].Player.GetPrimaryKeyString();
                tasks.Add(GrainFactory.GetGrain<IPlayerFriendList>(gameKey, pk, null).SetFriendStatus(player, status));
            }
            await Task.WhenAll(tasks);
        }

        public Task<OnlineStatus> GetStatus()
        {
            return Task.FromResult(State.Status);
        }
    }
}