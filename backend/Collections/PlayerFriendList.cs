using System;
using System.Linq;
using System.Collections.Generic;
using System.Threading.Tasks;
using Orleans;
using Interfaces;
using Orleans.Concurrency;
using Orleans.Streams;

namespace Collections
{
    
    class PlayerFriendList : Grain<FriendListState>, IPlayerFriendList
    {
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
                return WriteStateAsync();
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
            var setnInv = State.SentInvitations.First(x => x.Receiver.GetPrimaryKeyString() == accepter.GetPrimaryKeyString());
            if (setnInv.Sender == null)
                throw new InvalidOperationException("No such friend request is sent");
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
            await WriteStateAsync();
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
                await WriteStateAsync();
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
                tasks.Add(friend.Player.GetPrimaryKeyString(), friend.Player.GetPlayerPublicStrings(dataKeys));
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
            if (invitee.GetPrimaryKeyString() == player.GetPrimaryKeyString() || !State.CanInvitationHappen(invitee))
                throw new InvalidOperationException($"The invitee {invitee.GetPrimaryKeyString()} is already a friend of yours");
            bool requestResult = await (GrainFactory.GetGrain<IPlayerFriendList>(gameKey, invitee.GetPrimaryKeyString(), null)).ReceiveInviteRequest(player, name, data);
            if (requestResult)
            {
                State.AddSentInvitation(player, invitee, await invitee.GetPlayerName(), data);
                await WriteStateAsync();
            }
        }

        public async Task<bool> ReceiveInviteRequest(IPlayer sender, string name, byte[] data)
        {
            if (!State.CanInvitationHappen(sender))
                throw new InvalidOperationException($"The sender {sender.GetPrimaryKeyString()} is already a friend of yours");
            bool isValidPlayer = true;
            if (!isValidPlayer)
            {
                throw new InvalidOperationException("Such player does not exist");
            }
            else
            {
                //if we sent challenge request to the other successfully as well.
                var sentInvitation = State.SentInvitations.FirstOrDefault(x => x.Receiver.GetPrimaryKeyString() == sender.GetPrimaryKeyString());
                if (sentInvitation.Sender != null)
                {
                    return false;
                }
                else
                {
                    State.AddReceivedInvitation(player, sender, name, data);
                    await WriteStateAsync();
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
        }

        public Task<List<FriendListInvitation>> GetSentInvitations()
        {
            return Task.FromResult(State.ReceivedInvitations);
        }

        public Task<List<FriendListInvitation>> GetReceivedInvitations()
        {
            return Task.FromResult(State.ReceivedInvitations);
        }

        // [AlwaysInterleave] used to sit on this class method. Orleans only honors it on the grain
        // interface method (3.x ignored it here, 10 rejects it with ORLEANS0001), so it was removed.
        public async Task SetFriendStatus(IPlayer friend, OnlineStatus status)
        {
            var foundFriend = State.Friends.FirstOrDefault(x => x.Player.GetPrimaryKeyString() == friend.GetPrimaryKeyString());
            if (foundFriend == null)
                throw new InvalidOperationException("Such Friend does not exist in the list");
            foundFriend.SetState(status);
            await friendListEvents.OnNextAsync(new FriendListEventData
            {
                Type = FriendListEventData.EventType.StateChange,
                OnlineStatus = status,
                Friend = friend,
                GameKey = gameKey
            });
        }

        // See SetFriendStatus about the removed [AlwaysInterleave].
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