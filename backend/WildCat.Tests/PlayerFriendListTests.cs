#if !MASTER
using WildCat.Interfaces;
using Orleans;
using Orleans.Streams;
using Orleans.TestingHost;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Xunit;

namespace WildCat.Tests
{
    //[Ignore]
    [Collection(ClusterCollection.Name)]
    public class PlayerFriendListTests
    {
        private InProcessTestCluster cluster;
        public IGrainFactory GrainFactory => this.cluster.Client;
        public PlayerFriendListTests(ClusterFixture fixture)
        {
            this.cluster = fixture.Cluster;
        }
        [Fact]
        public async Task InviteAndAcceptFriendship()
        {
            var provider = cluster.Client.GetStreamProvider(StreamNames.PROVIDER_NAME);
            bool received = false;
            bool accepted = false;
            //[sessionKey,DeveloperKey,GameKey]
            var sessionKeys = (await CreateDeveloperAndSession("friendloper1", 2));
            var guid1 = "friend01";
            var guid2 = "friend02";
            var p1 = GrainFactory.GetGrain<IPlayer>(guid1);
            var p2 = GrainFactory.GetGrain<IPlayer>(guid2);
            var s1 = provider.GetStream<FriendListEventData>(StreamNames.FriendEvents(sessionKeys[0].Item3, guid1));
            var s2 = provider.GetStream<FriendListEventData>(StreamNames.FriendEvents(sessionKeys[1].Item3, guid2));
            await s1.SubscribeAsync<FriendListEventData>((x, y) =>
            {
                switch (x.Type)
                {
                    case FriendListEventData.EventType.Deny:
                        Assert.False(true, "Should not receive");
                        break;
                    case FriendListEventData.EventType.Accept:
                        accepted = true;
                        break;
                    case FriendListEventData.EventType.Invite:
                        Assert.False(true, "Should not deny");
                        break;
                }
                return Task.CompletedTask;
            });

            await s2.SubscribeAsync<FriendListEventData>((x, y) =>
            {
                switch (x.Type)
                {
                    case FriendListEventData.EventType.Invite:
                        received = true;
                        cluster.Client.GetGrain<IPlayerFriendList>(sessionKeys[0].Item3, p2.GetPrimaryKeyString(), null).AcceptInvitation(x.Friend, new byte[] { });
                        break;
                    case FriendListEventData.EventType.Deny:
                        Assert.False(true, "Should not accept");
                        break;
                    case FriendListEventData.EventType.Accept:
                        Assert.False(true, "Should not deny");
                        break;
                }
                return Task.CompletedTask;
            });

            await p1.CreateAccountWithDevice(p1.GetPrimaryKeyString());
            await p2.CreateAccountWithDevice(p2.GetPrimaryKeyString());
            Guid t = Guid.NewGuid();
            LoginResult res1 = await p1.LoginWithDeviceID(guid1, sessionKeys[0].Item1, t);
            LoginResult res2 = await p2.LoginWithDeviceID(guid2, sessionKeys[1].Item1, t);
            Assert.False(string.IsNullOrEmpty(res1.ticket));
            Assert.False(string.IsNullOrEmpty(res2.ticket));
            var fl1 = cluster.Client.GetGrain<IPlayerFriendList>(sessionKeys[0].Item3, p1.GetPrimaryKeyString(), null);
            var fl2 = cluster.Client.GetGrain<IPlayerFriendList>(sessionKeys[1].Item3, p2.GetPrimaryKeyString(), null);
            await fl1.Invite(p2, await p2.GetPlayerName(), new byte[] { });

            while (!received || !accepted)
                await Task.Delay(100);
            Assert.True(received);
            Assert.True(accepted);
            var friends1 = await fl1.GetFriends(new string[] { });
            var friends2 = await fl2.GetFriends(new string[] { });
            Assert.True(friends1.Friends.Count == 1);
            Assert.True(friends2.Friends.Count == 1);
            Assert.True(friends1.Friends[0].Player.GetPrimaryKeyString() == p2.GetPrimaryKeyString());
            Assert.True(friends2.Friends[0].Player.GetPrimaryKeyString() == p1.GetPrimaryKeyString());
        }

        [Fact]
        public async Task InviteAndDenyFriendship()
        {
            var provider = cluster.Client.GetStreamProvider(StreamNames.PROVIDER_NAME);
            bool received = false;
            bool denied = false;
            //[sessionKey,DeveloperKey,GameKey]
            var sessionKeys = (await CreateDeveloperAndSession("friendloper2", 2));
            var guid1 = "friend03";
            var guid2 = "friend04";
            var p1 = GrainFactory.GetGrain<IPlayer>(guid1);
            var p2 = GrainFactory.GetGrain<IPlayer>(guid2);
            var s1 = provider.GetStream<FriendListEventData>(StreamNames.FriendEvents(sessionKeys[0].Item3, guid1));
            var s2 = provider.GetStream<FriendListEventData>(StreamNames.FriendEvents(sessionKeys[1].Item3, guid2));
            await s1.SubscribeAsync<FriendListEventData>((x, y) =>
            {
                switch (x.Type)
                {
                    case FriendListEventData.EventType.Deny:
                        denied = true;
                        break;
                    case FriendListEventData.EventType.Accept:
                        Assert.False(true, "Should not receive acceptance");
                        break;
                    case FriendListEventData.EventType.Invite:
                        Assert.False(true, "Should receive invitation");
                        break;
                }
                return Task.CompletedTask;
            });

            await s2.SubscribeAsync<FriendListEventData>((x, y) =>
            {
                switch (x.Type)
                {
                    case FriendListEventData.EventType.Invite:
                        received = true;
                        cluster.Client.GetGrain<IPlayerFriendList>(sessionKeys[0].Item3, p2.GetPrimaryKeyString(), null).DenyInvitation(x.Friend, new byte[] { });
                        break;
                    case FriendListEventData.EventType.Deny:
                        Assert.False(true, "Should not receive denial");
                        break;
                    case FriendListEventData.EventType.Accept:
                        Assert.False(true, "Should not receive acceptance");
                        break;
                }
                return Task.CompletedTask;
            });

            await p1.CreateAccountWithDevice(p1.GetPrimaryKeyString());
            await p2.CreateAccountWithDevice(p2.GetPrimaryKeyString());
            Guid t = Guid.NewGuid();
            LoginResult res1 = await p1.LoginWithDeviceID(guid1, sessionKeys[0].Item1, t);
            LoginResult res2 = await p2.LoginWithDeviceID(guid2, sessionKeys[1].Item1, t);
            Assert.False(string.IsNullOrEmpty(res1.ticket));
            Assert.False(string.IsNullOrEmpty(res2.ticket));
            var fl1 = cluster.Client.GetGrain<IPlayerFriendList>(sessionKeys[0].Item3, p1.GetPrimaryKeyString(), null);
            var fl2 = cluster.Client.GetGrain<IPlayerFriendList>(sessionKeys[1].Item3, p2.GetPrimaryKeyString(), null);
            await fl1.Invite(p2, await p2.GetPlayerName(), new byte[] { });

            while (!received || !denied)
                await Task.Delay(100);
            Assert.True(received);
            Assert.True(denied);
            var friends1 = await fl1.GetFriends(new string[] { });
            var friends2 = await fl2.GetFriends(new string[] { });
            Assert.True(friends1.Friends.Count == 0);
            Assert.True(friends2.Friends.Count == 0);
        }

        [Fact]
        public async Task InviteMultipleTimesAndAcceptFriendship()
        {
            var provider = cluster.Client.GetStreamProvider(StreamNames.PROVIDER_NAME);
            int received = 0;
            bool accepted = false;
            //[sessionKey,DeveloperKey,GameKey]
            var sessionKeys = (await CreateDeveloperAndSession("friendloper3", 2));
            var guid1 = "friend05";
            var guid2 = "friend06";
            var p1 = GrainFactory.GetGrain<IPlayer>(guid1);
            var p2 = GrainFactory.GetGrain<IPlayer>(guid2);
            var s1 = provider.GetStream<FriendListEventData>(StreamNames.FriendEvents(sessionKeys[0].Item3, guid1));
            var s2 = provider.GetStream<FriendListEventData>(StreamNames.FriendEvents(sessionKeys[1].Item3, guid2));
            await s1.SubscribeAsync<FriendListEventData>((x, y) =>
            {
                switch (x.Type)
                {
                    case FriendListEventData.EventType.Deny:
                        Assert.False(true, "Should not receive acceptance");
                        break;
                    case FriendListEventData.EventType.Accept:
                        accepted = true;
                        break;
                    case FriendListEventData.EventType.Invite:
                        Assert.False(true, "Should receive invitation");
                        break;
                }
                return Task.CompletedTask;
            });

            await s2.SubscribeAsync<FriendListEventData>((x, y) =>
            {
                switch (x.Type)
                {
                    case FriendListEventData.EventType.Invite:
                        received++;
                        cluster.Client.GetGrain<IPlayerFriendList>(sessionKeys[0].Item3, p2.GetPrimaryKeyString(), null).AcceptInvitation(x.Friend, new byte[] { });
                        break;
                    case FriendListEventData.EventType.Deny:
                        Assert.False(true, "Should not receive denial");
                        break;
                    case FriendListEventData.EventType.Accept:
                        Assert.False(true, "Should not receive acceptance");
                        break;
                }
                return Task.CompletedTask;
            });

            await p1.CreateAccountWithDevice(p1.GetPrimaryKeyString());
            await p2.CreateAccountWithDevice(p2.GetPrimaryKeyString());
            Guid t = Guid.NewGuid();
            LoginResult res1 = await p1.LoginWithDeviceID(guid1, sessionKeys[0].Item1, t);
            LoginResult res2 = await p2.LoginWithDeviceID(guid2, sessionKeys[1].Item1, t);
            Assert.False(string.IsNullOrEmpty(res1.ticket));
            Assert.False(string.IsNullOrEmpty(res2.ticket));
            var fl1 = cluster.Client.GetGrain<IPlayerFriendList>(sessionKeys[0].Item3, p1.GetPrimaryKeyString(), null);
            var fl2 = cluster.Client.GetGrain<IPlayerFriendList>(sessionKeys[1].Item3, p2.GetPrimaryKeyString(), null);
            await fl1.Invite(p2, await p2.GetPlayerName(), new byte[] { });
            await fl1.Invite(p2, await p2.GetPlayerName(), new byte[] { });
            while (received == 0 || !accepted)
                await Task.Delay(100);

            Assert.True(accepted);
            Assert.True(received == 2, received.ToString());
            var friends1 = await fl1.GetFriends(new string[] { });
            var friends2 = await fl2.GetFriends(new string[] { });
            Assert.True(friends1.Friends.Count == 1);
            Assert.True(friends2.Friends.Count == 1);
        }

        [Fact]
        public async Task ChangeStatus()
        {
            var provider = cluster.Client.GetStreamProvider(StreamNames.PROVIDER_NAME);
            bool received = false;
            bool changedState = false;
            //[sessionKey,DeveloperKey,GameKey]
            var sessionKeys = (await CreateDeveloperAndSession("friendloper4", 2));
            var guid1 = "friend07";
            var guid2 = "friend08";
            var p1 = GrainFactory.GetGrain<IPlayer>(guid1);
            var p2 = GrainFactory.GetGrain<IPlayer>(guid2);
            var s1 = provider.GetStream<FriendListEventData>(StreamNames.FriendEvents(sessionKeys[0].Item3, guid1));
            var s2 = provider.GetStream<FriendListEventData>(StreamNames.FriendEvents(sessionKeys[1].Item3, guid2));
            await s1.SubscribeAsync<FriendListEventData>((x, y) =>
            {
                switch (x.Type)
                {
                    case FriendListEventData.EventType.Deny:
                        Assert.False(true, "Should not receive");
                        break;
                    case FriendListEventData.EventType.StateChange:
                        changedState = x.OnlineStatus == OnlineStatus.Offline;
                        break;
                    case FriendListEventData.EventType.Invite:
                        Assert.False(true, "Should not deny");
                        break;
                }
                return Task.CompletedTask;
            });

            await s2.SubscribeAsync<FriendListEventData>((x, y) =>
            {
                switch (x.Type)
                {
                    case FriendListEventData.EventType.Invite:
                        received = true;
                        cluster.Client.GetGrain<IPlayerFriendList>(sessionKeys[0].Item3, p2.GetPrimaryKeyString(), null).AcceptInvitation(x.Friend, new byte[] { });
                        break;
                    case FriendListEventData.EventType.Deny:
                        Assert.False(true, "Should not accept");
                        break;
                    case FriendListEventData.EventType.Accept:
                        Assert.False(true, "Should not deny");
                        break;
                }
                return Task.CompletedTask;
            });

            await p1.CreateAccountWithDevice(p1.GetPrimaryKeyString());
            await p2.CreateAccountWithDevice(p2.GetPrimaryKeyString());
            Guid t = Guid.NewGuid();
            LoginResult res1 = await p1.LoginWithDeviceID(guid1, sessionKeys[0].Item1, t);
            LoginResult res2 = await p2.LoginWithDeviceID(guid2, sessionKeys[1].Item1, t);
            Assert.False(string.IsNullOrEmpty(res1.ticket));
            Assert.False(string.IsNullOrEmpty(res2.ticket));
            var fl1 = cluster.Client.GetGrain<IPlayerFriendList>(sessionKeys[0].Item3, p1.GetPrimaryKeyString(), null);
            var fl2 = cluster.Client.GetGrain<IPlayerFriendList>(sessionKeys[1].Item3, p2.GetPrimaryKeyString(), null);
            await fl1.Invite(p2, await p2.GetPlayerName(), new byte[] { });

            while (!received)
                await Task.Delay(100);

            Assert.True(received);
            var friends1 = await fl1.GetFriends(new string[] { });
            var friends2 = await fl2.GetFriends(new string[] { });
            await fl2.SetStatus("", OnlineStatus.Offline);
            Assert.True(friends1.Friends.Count == 1);
            Assert.True(friends2.Friends.Count == 1);
            Assert.True(friends1.Friends[0].Player.GetPrimaryKeyString() == p2.GetPrimaryKeyString());
            Assert.True(friends2.Friends[0].Player.GetPrimaryKeyString() == p1.GetPrimaryKeyString());
            while (!changedState)
                await Task.Delay(100);

        }

        [Fact]
        public async Task GetFriendsAndTheirData()
        {
            var provider = cluster.Client.GetStreamProvider(StreamNames.PROVIDER_NAME);
            bool received = false;
            bool accepted = false;
            //[sessionKey,DeveloperKey,GameKey]
            var sessionKeys = (await CreateDeveloperAndSession("friendloper5", 2));
            var guid1 = "friend09";
            var guid2 = "friend10";
            var p1 = GrainFactory.GetGrain<IPlayer>(guid1);
            var p2 = GrainFactory.GetGrain<IPlayer>(guid2);
            var s1 = provider.GetStream<FriendListEventData>(StreamNames.FriendEvents(sessionKeys[0].Item3, guid1));
            var s2 = provider.GetStream<FriendListEventData>(StreamNames.FriendEvents(sessionKeys[1].Item3, guid2));
            await s1.SubscribeAsync<FriendListEventData>((x, y) =>
            {
                switch (x.Type)
                {
                    case FriendListEventData.EventType.Deny:
                        Assert.False(true, "Should not receive");
                        break;
                    case FriendListEventData.EventType.Accept:
                        accepted = true;
                        break;
                    case FriendListEventData.EventType.Invite:
                        Assert.False(true, "Should not deny");
                        break;
                }
                return Task.CompletedTask;
            });

            await s2.SubscribeAsync<FriendListEventData>((x, y) =>
            {
                switch (x.Type)
                {
                    case FriendListEventData.EventType.Invite:
                        received = true;
                        cluster.Client.GetGrain<IPlayerFriendList>(sessionKeys[0].Item3, p2.GetPrimaryKeyString(), null).AcceptInvitation(x.Friend, new byte[] { });
                        break;
                    case FriendListEventData.EventType.Deny:
                        Assert.False(true, "Should not accept");
                        break;
                    case FriendListEventData.EventType.Accept:
                        Assert.False(true, "Should not deny");
                        break;
                }
                return Task.CompletedTask;
            });

            await p1.CreateAccountWithDevice(p1.GetPrimaryKeyString());
            await p2.CreateAccountWithDevice(p2.GetPrimaryKeyString());
            Guid t = Guid.NewGuid();
            LoginResult res1 = await p1.LoginWithDeviceID(guid1, sessionKeys[0].Item1, t);
            LoginResult res2 = await p2.LoginWithDeviceID(guid2, sessionKeys[1].Item1, t);
            await p1.SetPlayerPublicString("s1", "Ashkan");
            Assert.False(string.IsNullOrEmpty(res1.ticket));
            Assert.False(string.IsNullOrEmpty(res2.ticket));
            var fl1 = cluster.Client.GetGrain<IPlayerFriendList>(sessionKeys[0].Item3, p1.GetPrimaryKeyString(), null);
            var fl2 = cluster.Client.GetGrain<IPlayerFriendList>(sessionKeys[1].Item3, p2.GetPrimaryKeyString(), null);
            await fl1.Invite(p2, await p2.GetPlayerName(), new byte[] { });

            while (!received || !accepted)
                await Task.Delay(100);
            Assert.True(received);
            Assert.True(accepted);
            var friends1 = await fl1.GetFriends(new string[] { "s1" });
            var friends2 = await fl2.GetFriends(new string[] { "s1" });
            Assert.True(friends1.Friends.Count == 1);
            Assert.True(friends2.Friends.Count == 1);
            Assert.True(friends1.Friends[0].Player.GetPrimaryKeyString() == p2.GetPrimaryKeyString());
            Assert.True(friends2.Friends[0].Player.GetPrimaryKeyString() == p1.GetPrimaryKeyString());
            Assert.True(friends2.publicData[friends2.Friends[0].Player.GetPrimaryKeyString()]["s1"] == "Ashkan");
            Assert.True(friends1.publicData[friends1.Friends[0].Player.GetPrimaryKeyString()].Count == 0);
        }

        private async Task<Tuple<Guid, Guid, Guid>[]> CreateDeveloperAndSession(string developerName, int sessionCount = 1)
        {
            var developerKey = await GrainFactory.GetGrain<IDeveloperManager>("main").AddDeveloper(CreateDeveloper(developerName));
            var developer = GrainFactory.GetGrain<IDeveloper>(developerKey);
            var gameKey = await developer.AddGame(new GameInfo { Name = "game1" });
            Guid[] sessionKeys = new Guid[sessionCount];
            Tuple<Guid, Guid, Guid>[] results = new Tuple<Guid, Guid, Guid>[sessionCount];
            for (int i = 0; i < sessionCount; ++i)
            {
                sessionKeys[i] = await developer.InitializeSession(gameKey);
                results[i] = Tuple.Create(sessionKeys[i], developerKey, gameKey);
            }

            return results;
        }

        private DeveloperState CreateDeveloper(string name)
        {
            DeveloperState data = new DeveloperState
            {
                FirstName = name,
                Email = $"{name}@{name}.com"
            };
            return data;
        }
    }
}
#endif