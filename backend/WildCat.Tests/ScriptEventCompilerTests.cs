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
            var validationResult = await script.Validate("int x=12; float g=1.0f; async Task s1(int a,int b){}");
            Assert.True(validationResult.IsValid, validationResult.ErrorMsg);
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
            var validationResult = await script.Validate("public async Task<bool> s1(int a,int b){return false;}");
            Assert.True(validationResult.IsValid, validationResult.ErrorMsg);
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
            var validationResult = await script.Validate("int[] i = new int[10];bool[,] bools; async Task s1(int a,int b){}");
            Assert.True(validationResult.IsValid, validationResult.ErrorMsg);
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
            var validationResult = await script.Validate("public class F{} F a = new F(); async Task s1(int a,int b){}");
            Assert.True(validationResult.IsValid, validationResult.ErrorMsg);
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
            var validationResult = await script.Validate("public async Task<bool> s1(int a,int b){System.Diagnostics.Process p=null; return false;}");
            Assert.False(validationResult.IsValid);
            Assert.Contains(validationResult.Diagnostics, d => d.Id == "WC1001" && d.Message.Contains("System.Diagnostics.Process") && d.Line == 1);
        }

        [Fact]
        public async Task ScriptGrantsAndSetsInventoryItems()
        {
            var (_, gameKey, sessionKey) = await TestSetup.CreateDeveloperGameAndSession(GrainFactory);
            var player = await TestSetup.CreateLoggedInPlayer(GrainFactory, sessionKey, Guid.NewGuid());
            string playerId = player.GetPrimaryKeyString();
            var script = GrainFactory.GetGrain<IScriptEventCompiler>(ResourcesUtility.GetEventScriptKey(gameKey.ToString(), "grant"));
            // The chest number comes from the client, so the script only uses it to pick a reward
            // from its own table; the amount itself is decided on the server.
            await script.Initialize("grant", new List<EventParameter> {
                new EventParameter { Type = EventParameter.ParamType.IntParam }
            }, "private async Task<int> grant(int chest){" +
               "int coins = chest == 2 ? 5 : 1;" +
               "bool added = await WildCat.AddInventoryItems(WildCat.CurrentPlayer, \"\", new string[] {\"coin\", \"gem\"}, new int[] {coins, 1});" +
               "await WildCat.SetInventoryItems(WildCat.CurrentPlayer, \"\", new string[] {\"gem\"}, new int[] {7});" +
               "return added ? 1 : 0;}");

            var result = await script.Run(GrainFactory.GetGrain<ISession>(sessionKey), playerId, 2);

            Assert.Equal(1, (int)result);
            var inventory = await GrainFactory.GetGrain<IPlayerInventory>(
                ResourcesUtility.GetPlayerInventoryKey(gameKey.ToString(), playerId)).GetInventory();
            Assert.Equal(2, inventory.items.Count);
            Assert.Equal(ResourcesUtility.GetItemKey(gameKey.ToString(), "coin"), inventory.items[0].Item.GetPrimaryKeyString());
            Assert.Equal(5, inventory.items[0].Count);
            Assert.Equal(ResourcesUtility.GetItemKey(gameKey.ToString(), "gem"), inventory.items[1].Item.GetPrimaryKeyString());
            Assert.Equal(7, inventory.items[1].Count);
        }

        [Fact]
        public async Task InvalidScriptsAreRejectedWithDiagnostics()
        {
            var gamekey = Guid.NewGuid().ToString();
            var script = GrainFactory.GetGrain<IScriptEventCompiler>(ResourcesUtility.GetEventScriptKey(gamekey, "d1"));
            var parameters = new List<EventParameter> { new EventParameter { Type = EventParameter.ParamType.IntParam } };

            var ex = await Assert.ThrowsAsync<InvalidOperationException>(() => script.Initialize("d1", parameters, "int d1(int a)\n{\n  return a + missing;\n}"));
            Assert.Contains("CS0103", ex.Message);
            var diagnostics = await script.GetDiagnostics();
            Assert.False(diagnostics.Success);
            var error = Assert.Single(diagnostics.Diagnostics, d => d.Severity == ScriptDiagnosticSeverity.Error);
            Assert.Equal(("CS0103", 3, 14), (error.Id, error.Line, error.Column));

            await script.Initialize("d1", parameters, "int d1(int a) { return a * 2; }");
            Assert.True((await script.GetDiagnostics()).Success);

            // A rejected edit keeps the working version and reports why.
            await Assert.ThrowsAsync<InvalidOperationException>(() => script.Edit(parameters, "int d1(int a)\n{\n  return System.IO.File.Exists(\"x\") ? 1 : 0;\n}"));
            var failedEdit = await script.GetDiagnostics();
            Assert.False(failedEdit.Success);
            Assert.Contains(failedEdit.Diagnostics, d => d.Id == "WC1001" && d.Line == 3 && d.Message.Contains("System.IO.File"));
            Assert.Equal("int d1(int a) { return a * 2; }", (await script.GetInfo()).Script);
            Assert.Equal(8, (int)await script.Run(null, "", 4));
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
