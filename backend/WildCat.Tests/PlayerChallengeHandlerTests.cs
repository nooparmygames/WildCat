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
    public class PlayerChallengeHandlerTests
    {
        private InProcessTestCluster cluster;
        public IGrainFactory GrainFactory => this.cluster.Client;
        public PlayerChallengeHandlerTests(ClusterFixture fixture)
        {
            this.cluster = fixture.Cluster;
        }

        [Fact]
        public async Task CreateAndAccepChallenge()
        {
            var provider = cluster.Client.GetStreamProvider(StreamNames.PROVIDER_NAME);
            bool received = false;
            bool accepted = false;
            //[sessionKey,DeveloperKey,GameKey]
            var sessionKeys = (await CreateDeveloperAndSession("challoper1", 2));
            var guid1 = "challengedevice01";
            var guid2 = "challengedevice02";
            var p1 = GrainFactory.GetGrain<IPlayer>(guid1);
            var p2 = GrainFactory.GetGrain<IPlayer>(guid2);
            var s1 = provider.GetStream<ChallengeEventData>(StreamNames.ChallengeEvents(sessionKeys[0].Item3, guid1));
            var s2 = provider.GetStream<ChallengeEventData>(StreamNames.ChallengeEvents(sessionKeys[1].Item3, guid2));
            await s1.SubscribeAsync<ChallengeEventData>((x, y) =>
            {
                switch (x.Type)
                {
                    case ChallengeEventData.EventType.Request:
                        Assert.False(true, "Should not receive");
                        break;
                    case ChallengeEventData.EventType.Started:
                        accepted = true;
                        break;
                    case ChallengeEventData.EventType.Deny:
                        Assert.False(true, "Should not deny");
                        break;
                }
                return Task.CompletedTask;
            });

            await s2.SubscribeAsync<ChallengeEventData>(async (x, y) =>
            {
                switch (x.Type)
                {
                    case ChallengeEventData.EventType.Request:
                        received = true;
                        string other = x.otherPlayer.GetPrimaryKeyString();
                        var otherHandler = cluster.Client.GetGrain<IPlayerChallengeHandler>(sessionKeys[0].Item3, other, null);
                        await otherHandler.AcceptChallenge(cluster.Client.GetGrain<IPlayer>(guid2), new byte[] { 14 });
                        break;
                    case ChallengeEventData.EventType.Started:
                        //Assert.False(true,"Should not accept");
                        break;
                    case ChallengeEventData.EventType.Deny:
                        Assert.False(true, "Should not deny");
                        break;
                }
            });

            await p1.CreateAccountWithDevice(p1.GetPrimaryKeyString());
            await p2.CreateAccountWithDevice(p2.GetPrimaryKeyString());
            Guid t = Guid.Empty;
            LoginResult res1 = await p1.LoginWithDeviceID(guid1, sessionKeys[0].Item1, t);
            LoginResult res2 = await p2.LoginWithDeviceID(guid2, sessionKeys[1].Item1, t);
            Assert.False(string.IsNullOrEmpty(res1.ticket));
            Assert.False(string.IsNullOrEmpty(res2.ticket));
            var c1 = cluster.Client.GetGrain<IPlayerChallengeHandler>(sessionKeys[0].Item3, p1.GetPrimaryKeyString(), null);
            var c2 = cluster.Client.GetGrain<IPlayerChallengeHandler>(sessionKeys[1].Item3, p2.GetPrimaryKeyString(), null);
            var requestResult = await c1.SendChallengeRequest(p2, new byte[] { 13 }, 13, true);
            Assert.Equal(ChallengeAcceptionStatus.Received, requestResult);
            while (!received || !accepted)
                await Task.Delay(100);
            Assert.True(received);
            Assert.True(accepted);
        }

        [Fact]
        public async Task CreateAndAccepChallengeWithOfflineOther()
        {
            var provider = cluster.Client.GetStreamProvider(StreamNames.PROVIDER_NAME);
            bool accepted = false;
            //[sessionKey,DeveloperKey,GameKey]
            var sessionKeys = (await CreateDeveloperAndSession("challoper4", 2));
            var guid1 = "challengedevice011";
            var guid2 = "challengedevice012";
            var p1 = GrainFactory.GetGrain<IPlayer>(guid1);
            var p2 = GrainFactory.GetGrain<IPlayer>(guid2);
            var s1 = provider.GetStream<ChallengeEventData>(StreamNames.ChallengeEvents(sessionKeys[0].Item3, guid1));
            await s1.SubscribeAsync<ChallengeEventData>((x, y) =>
            {
                switch (x.Type)
                {
                    case ChallengeEventData.EventType.Request:
                        Assert.False(true, "Should not receive");
                        break;
                    case ChallengeEventData.EventType.Started:
                        accepted = true;
                        break;
                    case ChallengeEventData.EventType.Deny:
                        Assert.False(true, "Should not deny");
                        break;
                }
                return Task.CompletedTask;
            });

            await p1.CreateAccountWithDevice(p1.GetPrimaryKeyString());
            await p2.CreateAccountWithDevice(p2.GetPrimaryKeyString());
            Guid t = Guid.NewGuid();
            LoginResult res1 = await p1.LoginWithDeviceID(guid1, sessionKeys[0].Item1,  t);
            LoginResult res2 = await p2.LoginWithDeviceID(guid2, sessionKeys[1].Item1,  t);
            Assert.False(string.IsNullOrEmpty(res1.ticket));
            Assert.False(string.IsNullOrEmpty(res2.ticket));
            await p1.SetPresenseStatus(OnlineStatus.Offline);
            await p2.SetPresenseStatus(OnlineStatus.Offline);
            var c1 = cluster.Client.GetGrain<IPlayerChallengeHandler>(sessionKeys[0].Item3, p1.GetPrimaryKeyString(), null);
            var c2 = cluster.Client.GetGrain<IPlayerChallengeHandler>(sessionKeys[1].Item3, p2.GetPrimaryKeyString(), null);
            var requestResult = await c1.SendChallengeRequest(p2, new byte[] { 13 }, 13, true);
            Assert.Equal(ChallengeAcceptionStatus.Received, requestResult);
            (List<ChallengeRequestState> sent, List<ChallengeRequestState> received) challenges = await c2.GetChallengeRequests();
            Assert.Empty(challenges.sent);
            Assert.Single(challenges.received);
            Assert.Equal(ChallengeRequestState.Status.Pending, challenges.received[0].State);
            string other = challenges.received[0].Player.GetPrimaryKeyString();
            var otherHandler = cluster.Client.GetGrain<IPlayerChallengeHandler>(sessionKeys[0].Item3, other, null);
            await otherHandler.AcceptChallenge(cluster.Client.GetGrain<IPlayer>(guid2), new byte[] { 14 });
            while (!accepted)
                await Task.Delay(100);
            Assert.True(accepted);
            var readChallenges1 = await c1.GetChallengeRequests();
            var readChallenges2 = await c2.GetChallengeRequests();
            Assert.Single(readChallenges2.received);
            Assert.Single(readChallenges1.sent);
            Assert.Empty(readChallenges2.sent);
            Assert.Empty(readChallenges1.received);
            Assert.NotEqual(Guid.Empty, readChallenges2.received[0].Game);
            Assert.NotEqual(Guid.Empty, readChallenges1.sent[0].Game);
            Assert.Equal(ChallengeRequestState.Status.Started, readChallenges2.received[0].State);
            Assert.Equal(ChallengeRequestState.Status.Started, readChallenges1.sent[0].State);
        }

        [Fact]
        public async Task CreateAndAccepChallengeAfterTimeout()
        {
            var provider = cluster.Client.GetStreamProvider(StreamNames.PROVIDER_NAME);
            bool received = false;
            bool timedOut = false;
            //[sessionKey,DeveloperKey,GameKey]
            var sessionKeys = (await CreateDeveloperAndSession("challoper3", 2));
            var guid1 = "challengedevice07";
            var guid2 = "challengedevice08";
            var p1 = GrainFactory.GetGrain<IPlayer>(guid1);
            var p2 = GrainFactory.GetGrain<IPlayer>(guid2);
            var s1 = provider.GetStream<ChallengeEventData>(StreamNames.ChallengeEvents(sessionKeys[0].Item3, guid1));
            var s2 = provider.GetStream<ChallengeEventData>(StreamNames.ChallengeEvents(sessionKeys[1].Item3, guid2));
            await s1.SubscribeAsync<ChallengeEventData>((x, y) =>
            {
                switch (x.Type)
                {
                    case ChallengeEventData.EventType.Request:
                        Assert.False(true, "Should not receive");
                        break;
                    case ChallengeEventData.EventType.Started:
                        Assert.False(true, "Should not be accepted");
                        break;
                    case ChallengeEventData.EventType.Deny:
                        Assert.False(true, "Should not deny");
                        break;
                }
                return Task.CompletedTask;
            });

            await s2.SubscribeAsync<ChallengeEventData>(async (x, y) =>
            {
                switch (x.Type)
                {
                    case ChallengeEventData.EventType.Request:
                        received = true;
                        string other = x.otherPlayer.GetPrimaryKeyString();
                        var otherHandler = cluster.Client.GetGrain<IPlayerChallengeHandler>(sessionKeys[0].Item3, other, null);
                        await Task.Delay(2000);
                        try
                        {
                            await otherHandler.AcceptChallenge(cluster.Client.GetGrain<IPlayer>(guid2), new byte[] { 14 });
                        }
                        catch (ChallengeTimeoutException)
                        {
                            timedOut = true;
                        }
                        break;
                    case ChallengeEventData.EventType.Started:
                        //Assert.False(true,"Should not accept");
                        break;
                    case ChallengeEventData.EventType.Deny:
                        Assert.False(true, "Should not deny");
                        break;
                }
            });

            await p1.CreateAccountWithDevice(p1.GetPrimaryKeyString());
            await p2.CreateAccountWithDevice(p2.GetPrimaryKeyString());
            Guid t = Guid.NewGuid();
            LoginResult res1 = await p1.LoginWithDeviceID(guid1, sessionKeys[0].Item1,  t);
            LoginResult res2 = await p2.LoginWithDeviceID(guid2, sessionKeys[1].Item1, t);
            Assert.False(string.IsNullOrEmpty(res1.ticket));
            Assert.False(string.IsNullOrEmpty(res2.ticket));
            var c1 = cluster.Client.GetGrain<IPlayerChallengeHandler>(sessionKeys[0].Item3, p1.GetPrimaryKeyString(), null);
            var c2 = cluster.Client.GetGrain<IPlayerChallengeHandler>(sessionKeys[1].Item3, p2.GetPrimaryKeyString(), null);
            var requestResult = await c1.SendChallengeRequest(p2, new byte[] { 13 }, 1, true);
            Assert.Equal(ChallengeAcceptionStatus.Received, requestResult);
            while (!received || !timedOut)
                await Task.Delay(100);
            Assert.True(received);
            Assert.True(timedOut);
        }

        [Fact]
        public async Task CreateAndAccepChallengeAfterTimeoutAndAcceptNextOne()
        {
            var provider = cluster.Client.GetStreamProvider(StreamNames.PROVIDER_NAME);
            bool received = false;
            bool timedOut = false;
            bool accepted = false;
            //[sessionKey,DeveloperKey,GameKey]
            var sessionKeys = (await CreateDeveloperAndSession("challoper41", 2));
            var guid1 = "challengedevice05";
            var guid2 = "challengedevice06";
            var p1 = GrainFactory.GetGrain<IPlayer>(guid1);
            var p2 = GrainFactory.GetGrain<IPlayer>(guid2);
            var s1 = provider.GetStream<ChallengeEventData>(StreamNames.ChallengeEvents(sessionKeys[0].Item3, guid1));
            var s2 = provider.GetStream<ChallengeEventData>(StreamNames.ChallengeEvents(sessionKeys[1].Item3, guid2));
            int state = 0;
            await s1.SubscribeAsync<ChallengeEventData>((x, y) =>
            {
                switch (x.Type)
                {
                    case ChallengeEventData.EventType.Request:
                        Assert.False(true, "Should not receive");
                        break;
                    case ChallengeEventData.EventType.Started:
                        break;
                    case ChallengeEventData.EventType.Deny:
                        Assert.False(true, "Should not deny");
                        break;
                }
                return Task.CompletedTask;
            });

            await s2.SubscribeAsync<ChallengeEventData>(async (x, y) =>
            {
                switch (x.Type)
                {
                    case ChallengeEventData.EventType.Request:
                        received = true;
                        string other = x.otherPlayer.GetPrimaryKeyString();
                        var otherHandler = cluster.Client.GetGrain<IPlayerChallengeHandler>(sessionKeys[0].Item3, other, null);
                        if (state == 0)
                        {
                            await Task.Delay(2000);
                            try
                            {
                                await otherHandler.AcceptChallenge(cluster.Client.GetGrain<IPlayer>(guid2), new byte[] { 14 });
                            }
                            catch (ChallengeTimeoutException)
                            {
                                timedOut = true;
                                state++;
                            }
                        }
                        else
                        {
                            await otherHandler.AcceptChallenge(cluster.Client.GetGrain<IPlayer>(guid2), new byte[] { 14 });
                        }
                        break;
                    case ChallengeEventData.EventType.Started:
                        if (state > 0)
                            accepted = true;
                        break;
                    case ChallengeEventData.EventType.Deny:
                        Assert.False(true, "Should not deny");
                        break;
                }
            });

            await p1.CreateAccountWithDevice(p1.GetPrimaryKeyString());
            await p2.CreateAccountWithDevice(p2.GetPrimaryKeyString());
            Guid t = Guid.NewGuid();
            LoginResult res1 = await p1.LoginWithDeviceID(guid1, sessionKeys[0].Item1, t);
            LoginResult res2 = await p2.LoginWithDeviceID(guid2, sessionKeys[1].Item1, t);
            Assert.False(string.IsNullOrEmpty(res1.ticket));
            Assert.False(string.IsNullOrEmpty(res2.ticket));
            var c1 = cluster.Client.GetGrain<IPlayerChallengeHandler>(sessionKeys[0].Item3, p1.GetPrimaryKeyString(), null);
            var c2 = cluster.Client.GetGrain<IPlayerChallengeHandler>(sessionKeys[1].Item3, p2.GetPrimaryKeyString(), null);
            var requestResult = await c1.SendChallengeRequest(p2, new byte[] { 13 }, 1, true);
            Assert.Equal(ChallengeAcceptionStatus.Received, requestResult);
            await Task.Delay(3000);
            requestResult = await c1.SendChallengeRequest(p2, new byte[] { 13 }, 10, true);
            Assert.Equal(ChallengeAcceptionStatus.Received, requestResult);
            while (!received || !timedOut || !accepted)
                await Task.Delay(100);
            Assert.True(received);
            Assert.True(timedOut);
            Assert.True(accepted);
        }

        [Fact]
        public async Task CreateAndDenyChallenge()
        {
            var provider = cluster.Client.GetStreamProvider(StreamNames.PROVIDER_NAME);
            bool received = false;
            bool denied = false;
            //[sessionKey,DeveloperKey,GameKey]
            var sessionKeys = await CreateDeveloperAndSession("challoper2", 2);
            var guid1 = "challengedevice03";
            var guid2 = "challengedevice04";
            var p1 = GrainFactory.GetGrain<IPlayer>(guid1);
            var p2 = GrainFactory.GetGrain<IPlayer>(guid2);
            var s1 = provider.GetStream<ChallengeEventData>(StreamNames.ChallengeEvents(sessionKeys[0].Item3, guid1));
            var s2 = provider.GetStream<ChallengeEventData>(StreamNames.ChallengeEvents(sessionKeys[1].Item3, guid2));
            await s1.SubscribeAsync<ChallengeEventData>((x, y) =>
            {
                switch (x.Type)
                {
                    case ChallengeEventData.EventType.Request:
                        Assert.False(true, "Should not receive");
                        break;
                    case ChallengeEventData.EventType.Started:
                        Assert.False(true, "Should not deny");
                        break;
                    case ChallengeEventData.EventType.Deny:
                        denied = true;
                        break;
                }
                return Task.CompletedTask;
            });

            await s2.SubscribeAsync<ChallengeEventData>(async (x, y) =>
            {
                switch (x.Type)
                {
                    case ChallengeEventData.EventType.Request:
                        received = true;
                        string other = x.otherPlayer.GetPrimaryKeyString();
                        var otherHandler = cluster.Client.GetGrain<IPlayerChallengeHandler>(sessionKeys[0].Item3, other, null);
                        await otherHandler.DenyChallenge(cluster.Client.GetGrain<IPlayer>(guid2), new byte[] { 14 });
                        break;
                    case ChallengeEventData.EventType.Started:
                        Assert.False(true, "Should not accept");
                        break;
                    case ChallengeEventData.EventType.Deny:
                        Assert.False(true, "Should not deny");
                        break;
                }
            });

            await p1.CreateAccountWithDevice(p1.GetPrimaryKeyString());
            await p2.CreateAccountWithDevice(p2.GetPrimaryKeyString());
            Guid t = Guid.NewGuid();
            LoginResult res1 = await p1.LoginWithDeviceID(guid1, sessionKeys[0].Item1, t);
            LoginResult res2 = await p2.LoginWithDeviceID(guid2, sessionKeys[1].Item1, t);
            var c1 = cluster.Client.GetGrain<IPlayerChallengeHandler>(sessionKeys[0].Item3, p1.GetPrimaryKeyString(), null);
            var c2 = cluster.Client.GetGrain<IPlayerChallengeHandler>(sessionKeys[1].Item3, p2.GetPrimaryKeyString(), null);
            var requestResult = await c1.SendChallengeRequest(p2, new byte[] { 13 }, 15, true);
            Assert.Equal(ChallengeAcceptionStatus.Received, requestResult);
            while (!received || !denied)
                await Task.Delay(100);
            Assert.True(received);
            Assert.True(denied);
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