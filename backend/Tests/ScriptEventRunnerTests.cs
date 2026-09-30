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
    public class ScriptEventRunnerTests
    {
        private InProcessTestCluster cluster;
        public IGrainFactory GrainFactory => this.cluster.Client;
        public ScriptEventRunnerTests(ClusterFixture fixture)
        {
            this.cluster = fixture.Cluster;
        }

        [Fact(Skip = "temp already")]
        public async Task RunScriptWithAPI()
        {
            var devAndSession = await CreateDeveloperAndSession("rcriptrunner226");
            string player = "scriptPlayerx";
            await GrainFactory.GetGrain<IPlayer>(player).CreateAccountWithDevice(player);
            Guid t = Guid.NewGuid();
            var loginRes = await GrainFactory.GetGrain<IPlayer>(player).LoginWithDeviceID(player, devAndSession.Item1, t);

            var gamekey = devAndSession.Item3;
            string scriptKey = ResourcesUtility.GetEventScriptKey(gamekey.ToString(), "s22");
            var scriptCompiler = GrainFactory.GetGrain<IScriptEventCompiler>(scriptKey);
            await scriptCompiler.Initialize("s22", new List<EventParameter> {
                new EventParameter { Type = EventParameter.ParamType.IntParam},
                new EventParameter { Type = EventParameter.ParamType.IntParam}
            }, "private async Task<int> s22(int a,int b){await Apadana.SetPlayerData(Apadana.CurrentPlayer,\"n\",a.ToString());a = int.Parse((await Apadana.GetPlayerData(Apadana.CurrentPlayer,\"n\"))); return a+b;}");
            ISession session = GrainFactory.GetGrain<ISession>(devAndSession.Item1);
            var runner = GrainFactory.GetGrain<IScriptEventRunner>(gamekey, player, null);
            await Task.Delay(300);
            var res = await runner.Run(session, "s22", 1, 2);
            Assert.Equal(3, (int)res);
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
