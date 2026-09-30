#if !MASTER
using Interfaces;
using Orleans;
using Orleans.Streams;
using Orleans.TestingHost;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Tests;
using Xunit;

namespace GrainUnitTests
{
    [Collection(ClusterCollection.Name)]
    public class PlayerTests
    {
        private InProcessTestCluster cluster;
        public IGrainFactory GrainFactory => this.cluster.Client;

        public PlayerTests(ClusterFixture fixture)
        {
            this.cluster = fixture.Cluster;
        }
        [Fact]
        public async Task TestCreateAccountAndLoginWithDevice()
        {
            var sessionKey = (await CreateDeveloperAndSession("developer666")).Item1;

            var guid = "device016";
            var player = GrainFactory.GetGrain<IPlayer>(guid);

            await player.CreateAccountWithDevice(player.GetPrimaryKeyString());
            Guid t = Guid.NewGuid();
            var res = await player.LoginWithDeviceID(guid, sessionKey, t);
            var res2 = await player.LoginWithDeviceID("device02", sessionKey, t);
            var session = GrainFactory.GetGrain<ISession>(System.Guid.Parse(res.ticket));
            var user = await session.TouchAndGetInfo();
            Assert.True(res != null);
            Assert.False(res2 != null);
            Assert.Equal(res.userID, user.UserID);
        }

        [Fact]
        public async Task TestCreateAccountAndLoginWithUserPass()
        {
            var sessionKey = (await CreateDeveloperAndSession("developer17")).Item1;
            var creationResult = await GrainFactory.GetGrain<IPlayerCreator>(0).CreateAccountWithUserAndPassword("player1", "pass");
            Assert.True(!string.IsNullOrWhiteSpace(creationResult.Account));
            var state = await GrainFactory.GetGrain<IPlayer>(creationResult.Account).GetState();
            Assert.True(state.IsCreated);
            Assert.False(state.IsLoggedIn);
            Guid t = Guid.NewGuid();
            var loginResult = await GrainFactory.GetGrain<IPlayer>(creationResult.Account).LoginWithUserNameAndPassword("player1", "pass", sessionKey, t);
            Assert.Equal(loginResult.userID, creationResult.Account);
        }


        [Fact]
        public async Task TestCreateAccountAndLoginWithEmailPass()
        {
            var developerData = (await CreateDeveloperAndSession("developer18"));
            var sessionKey = developerData.Item1;
            var creationResult = await GrainFactory.GetGrain<IPlayerCreator>(0).CreateAccountWithEmail("player1@example.com", "pass");
            Assert.True(!string.IsNullOrWhiteSpace(creationResult.Account));
            var state = await GrainFactory.GetGrain<IPlayer>(creationResult.Account).GetState();
            string code = state.Emails["player1@example.com"]["code"];
            Assert.True(state.IsCreated);
            Assert.False(state.IsLoggedIn);
            Guid t = Guid.NewGuid();
            var loginResult = await GrainFactory.GetGrain<IPlayer>(creationResult.Account).LoginWithEmail("player1@example.com", "pass", sessionKey, t);
            Assert.Null(loginResult);
            await GrainFactory.GetGrain<IPlayer>(creationResult.Account).Activate(code);
            loginResult = await GrainFactory.GetGrain<IPlayer>(creationResult.Account).LoginWithEmail("player1@example.com", "pass", sessionKey, t);
            Assert.Equal(loginResult.userID, creationResult.Account);
        }

        [Fact(Skip = "ignore")]
        public async Task AddEmailAndUserToAccount()
        {
            var developerData = (await CreateDeveloperAndSession("developer118"));
            var sessionKey = developerData.Item1;
            var creationResult = await GrainFactory.GetGrain<IPlayerCreator>(0).CreateAccountWithEmail("player1@example.com", "pass");
            Assert.True(!string.IsNullOrWhiteSpace(creationResult.Account));
            IPlayer player = GrainFactory.GetGrain<IPlayer>(creationResult.Account);
            var state = await player.GetState();
            string code = state.Emails["player1@example.com"]["code"];
            Assert.True(state.IsCreated);
            Assert.False(state.IsLoggedIn);
            Guid t = Guid.NewGuid();
            var loginResult = await player.LoginWithEmail("player1@example.com", "pass", sessionKey, t);
            Assert.Null(loginResult);
            await GrainFactory.GetGrain<IPlayer>(creationResult.Account).Activate(code);
            loginResult = await player.LoginWithEmail("player1@example.com", "pass", sessionKey, t);
            Assert.Equal(loginResult.userID, creationResult.Account);
            await player.Logout();
            state = await player.GetState();
            Assert.False(state.IsLoggedIn);
            await player.AddPlayerUserAndPassword("ash", "pass");
            await player.AddPlayerEmail("player2@example.com", "pass");
            sessionKey = await GrainFactory.GetGrain<IDeveloper>(developerData.Item2).InitializeSession(developerData.Item3);
            loginResult = await player.LoginWithUserNameAndPassword("ash", "pass", sessionKey, t);
            Assert.NotNull(loginResult);
            await player.Logout();
            sessionKey = await GrainFactory.GetGrain<IDeveloper>(developerData.Item2).InitializeSession(developerData.Item3);
            loginResult = await player.LoginWithEmail("player2@example.com", "pass", sessionKey, t);
            Assert.Null(loginResult);
            state = await player.GetState();
            code = state.Emails["player2@example.com"]["code"];
            await player.Activate(code);
            loginResult = await player.LoginWithEmail("player2@example.com", "pass", sessionKey, t);
            Assert.NotNull(loginResult);
        }

        [Fact]
        public async Task TestCreateMultipleAccountWithSameDevice()
        {
            var sessionKey = (await CreateDeveloperAndSession("developer16")).Item1;

            var guid = Guid.NewGuid().ToString();
            var guid2 = Guid.NewGuid().ToString();
            var player = GrainFactory.GetGrain<IPlayer>(guid);
            var player2 = GrainFactory.GetGrain<IPlayer>(guid2);
            bool created = (await player.CreateAccountWithDevice("samedevice")) == AdditionResult.Success;
            bool created2 = (await player2.CreateAccountWithDevice("samedevice")) == AdditionResult.Success;
            Assert.True(created);
            Assert.False(created2);
        }

        private async Task<Tuple<Guid, Guid, Guid>> CreateDeveloperAndSession(string developerName)
        {
            var developerKey = await GrainFactory.GetGrain<IDeveloperManager>("main").AddDeveloper(CreateDeveloper(developerName));
            var developer = GrainFactory.GetGrain<IDeveloper>(developerKey);
            var gameKey = await developer.AddGame(new GameInfo { Name = "game1" });
            var sessionKey = await developer.InitializeSession(gameKey);
            return Tuple.Create(sessionKey, developerKey, gameKey);
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

        [Fact]
        public async Task FailToCreateMultipleAccountsPerDevice()
        {
            string guid = "multipledevice03";
            IPlayer p = GrainFactory.GetGrain<IPlayer>(guid);
            bool r1 = (await p.CreateAccountWithDevice(p.GetPrimaryKeyString())) == AdditionResult.Success;
            try
            {
                bool r2 = (await p.CreateAccountWithDevice(p.GetPrimaryKeyString())) == AdditionResult.Success;
                Assert.True(false);
            }
            catch (InvalidOperationException)
            {
                Assert.True(true);
            }
            

        }

        [Fact]
        public async Task SetNameAndGetName()
        {
            string guid = "device04";
            IPlayer p = GrainFactory.GetGrain<IPlayer>(guid);
            await p.SetPlayerName("Ashkan");
            string name = await p.GetPlayerName();
            Assert.Equal("Ashkan", name);
        }

        [Fact]
        public async Task SetPersianNameAndGetPersianName()
        {
            string guid = "device44";
            IPlayer p = GrainFactory.GetGrain<IPlayer>(guid);
            await p.SetPlayerName("مهدی");
            string name = await p.GetPlayerName();
            Assert.Equal("مهدی", name);
        }


        [Fact]
        public async Task SetPlayerIntAndGetPlayerInt()
        {

            string guid = "device05";
            IPlayer p = GrainFactory.GetGrain<IPlayer>(guid);
            await p.CreateAccountWithDevice(p.GetPrimaryKeyString());
            await p.SetPlayerString("coin", 100.ToString());
            var coin = int.Parse((await p.GetPlayerString("coin")).Item2);
            Assert.Equal(100, coin);
        }

        [Fact]
        public async Task SetPlayerStringAndGetPlayerString()
        {
            string guid = "device10";
            IPlayer p = GrainFactory.GetGrain<IPlayer>(guid);
            await p.CreateAccountWithDevice(p.GetPrimaryKeyString());
            await p.SetPlayerString("profile", "{user:\"Yaser\"}");
            string data = (await p.GetPlayerString("profile")).Item2;
            Assert.Equal("{user:\"Yaser\"}", data);
        }

        [Fact]
        public async Task LogoutTest()
        {
            var sessionKey = (await CreateDeveloperAndSession("developer4")).Item1;
            string guid = "device06";
            IPlayer player = GrainFactory.GetGrain<IPlayer>(guid);

            await player.CreateAccountWithDevice(player.GetPrimaryKeyString());
            bool loggedIn = await player.IsLoggedIn();
            Assert.False(loggedIn);
            Guid t = Guid.NewGuid();
            await player.LoginWithDeviceID(guid, sessionKey, t);
            loggedIn = await player.IsLoggedIn();
            await player.Logout();
            loggedIn = await player.IsLoggedIn();
            Assert.False(loggedIn);
        }

        [Fact]
        public async Task DisconnectPlayer()
        {
            var sessionKey = (await CreateDeveloperAndSession("developer1")).Item1;
            string guid = "device07";
            IPlayer player = GrainFactory.GetGrain<IPlayer>(guid);

            await player.CreateAccountWithDevice(player.GetPrimaryKeyString());
            bool loggedIn = await player.IsLoggedIn();
            Assert.False(loggedIn);
            Guid t = Guid.NewGuid();
            await player.LoginWithDeviceID(guid, sessionKey, t);
            loggedIn = await player.IsLoggedIn();
            await player.Disconnected(t);
            loggedIn = await player.IsLoggedIn();
            Assert.False(loggedIn);
        }
    }
}
#endif