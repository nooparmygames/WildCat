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
    [Collection(ClusterCollection.Name)]
    public class ScriptEventCompilerTests
    {
        private InProcessTestCluster cluster;
        public IGrainFactory GrainFactory => this.cluster.Client;
        public ScriptEventCompilerTests(ClusterFixture fixture)
        {
            this.cluster = fixture.Cluster;
        }

        [Fact]
        public async Task RunScript()
        {
            var gamekey = Guid.NewGuid().ToString();
            string scriptKey = ResourcesUtility.GetEventScriptKey(gamekey, "s1");
            var script = GrainFactory.GetGrain<IScriptEventCompiler>(scriptKey);
            await script.Initialize("s1", new List<EventParameter> {
                new EventParameter { Type = EventParameter.ParamType.IntParam},
                new EventParameter { Type = EventParameter.ParamType.IntParam}
            }, "async Task<int> s1(int a,int b){return a+b;}");
            var res = await script.Run(null, "", 1, 2);
            Assert.Equal(3, (int)res);
        }

        [Fact]
        public async Task RunScriptWithAPI()
        {
            var devAndSession = await CreateDeveloperAndSession("ScriptDeveloper");
            string player = "scriptPlayer";
            await GrainFactory.GetGrain<IPlayer>(player).CreateAccountWithDevice(player);
            Guid t = Guid.NewGuid();
            var loginRes = await GrainFactory.GetGrain<IPlayer>(player).LoginWithDeviceID(player, devAndSession.Item1, t);

            var gamekey = devAndSession.Item3;
            string scriptKey = ResourcesUtility.GetEventScriptKey(gamekey.ToString(), "s1");
            var boardKey = ResourcesUtility.GetLeaderBoardKey(gamekey.ToString(), "global");
            await GrainFactory.GetGrain<ILeaderBoard>(boardKey).Initialize("global", DateTime.Now, DateTime.Now, false);
            var script = GrainFactory.GetGrain<IScriptEventCompiler>(scriptKey);
            await script.Initialize("s1", new List<EventParameter> {
                new EventParameter { Type = EventParameter.ParamType.IntParam},
                new EventParameter { Type = EventParameter.ParamType.IntParam}
            }, "private async Task<int> s1(int a,int b){var dummy = await WildCat.GetTopEntriesInLeaderBoard(\"global\",1,1);; await WildCat.SetPlayerData(WildCat.CurrentPlayer,\"n\",a.ToString());a = int.Parse((await WildCat.GetPlayerData(WildCat.CurrentPlayer,\"n\"))); return a+b;}");
            ISession session = GrainFactory.GetGrain<ISession>(devAndSession.Item1);
            var res = await script.Run(session, player, 1, 2);
            Assert.Equal(3, (int)res);
        }

        [Fact]
        public async Task ValidateScript()
        {
            var gamekey = Guid.NewGuid().ToString();
            string scriptKey = ResourcesUtility.GetEventScriptKey(gamekey, "s1");
            var script = GrainFactory.GetGrain<IScriptEventCompiler>(scriptKey);
            await script.Initialize("s1", new List<EventParameter> {
                new EventParameter { Type = EventParameter.ParamType.IntParam},
                new EventParameter { Type = EventParameter.ParamType.IntParam}
            }, "int s1(int a,int b){return a+b;}");
            var validationResult = await script.Validate("int x=12; float g=1.0 async Task s1(){}");
            Assert.True(validationResult.IsValid);
        }

        [Fact]
        public async Task ValidateScriptWithTask()
        {
            var gamekey = Guid.NewGuid().ToString();
            string scriptKey = ResourcesUtility.GetEventScriptKey(gamekey, "s1");
            var script = GrainFactory.GetGrain<IScriptEventCompiler>(scriptKey);
            await script.Initialize("s1", new List<EventParameter> {
                new EventParameter { Type = EventParameter.ParamType.IntParam},
                new EventParameter { Type = EventParameter.ParamType.IntParam}
            }, "int s1(int a,int b){return a+b;}");
            var validationResult = await script.Validate("public async Task<bool> s1(){return false;}");
            Assert.True(validationResult.IsValid);
        }

        [Fact]
        public async Task ValidateScriptWithArray()
        {
            var gamekey = Guid.NewGuid().ToString();
            string scriptKey = ResourcesUtility.GetEventScriptKey(gamekey, "s1");
            var script = GrainFactory.GetGrain<IScriptEventCompiler>(scriptKey);
            await script.Initialize("s1", new List<EventParameter> {
                new EventParameter { Type = EventParameter.ParamType.IntParam},
                new EventParameter { Type = EventParameter.ParamType.IntParam}
            }, "int s1(int a,int b){return a+b;}");
            var validationResult = await script.Validate("int[Fact] i = new int[10];bool[,] bools; async Task s1(){}");
            Assert.True(validationResult.IsValid);
        }

        [Fact]
        public async Task ValidateScriptWithOwnType()
        {
            var gamekey = Guid.NewGuid().ToString();
            string scriptKey = ResourcesUtility.GetEventScriptKey(gamekey, "s1");
            var script = GrainFactory.GetGrain<IScriptEventCompiler>(scriptKey);
            await script.Initialize("s1", new List<EventParameter> {
                new EventParameter { Type = EventParameter.ParamType.IntParam},
                new EventParameter { Type = EventParameter.ParamType.IntParam}
            }, "int s1(int a,int b){return a+b;}");
            var validationResult = await script.Validate("public class F{} F a = new F(); async Task s1(){}");
            Assert.True(validationResult.IsValid);
        }

        [Fact]
        public async Task ValidateReturnValueCheckTest()
        {
            var gamekey = Guid.NewGuid().ToString();
            string scriptKey = ResourcesUtility.GetEventScriptKey(gamekey, "s1");
            var script = GrainFactory.GetGrain<IScriptEventCompiler>(scriptKey);
            await script.Initialize("s1", new List<EventParameter> {
                new EventParameter { Type = EventParameter.ParamType.IntParam},
                new EventParameter { Type = EventParameter.ParamType.IntParam}
            }, "int s1(int a,int b){return a+b;}");
            var validationResult = await script.Validate("async Task A(){}");
            var validationResult2 = await script.Validate("async Task<int> s1(){return 2;}");
            Assert.False(validationResult.HasReturnValue);
            Assert.True(validationResult2.HasReturnValue);
        }

        [Fact]
        public async Task InvalidateScriptWithProcess()
        {
            var gamekey = Guid.NewGuid().ToString();
            string scriptKey = ResourcesUtility.GetEventScriptKey(gamekey, "s1");
            var script = GrainFactory.GetGrain<IScriptEventCompiler>(scriptKey);
            await script.Initialize("s1", new List<EventParameter> {
                new EventParameter { Type = EventParameter.ParamType.IntParam},
                new EventParameter { Type = EventParameter.ParamType.IntParam}
            }, "int s1(int a,int b){return a+b;}");
            var validationResult = await script.Validate("public async Task<bool> s1(){System.Diagnostics.Process p=null; return false;}");
            Assert.False(validationResult.IsValid);
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
    }
}
