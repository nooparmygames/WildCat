using System.Diagnostics;
using WildCat.Interfaces;
using WildCat.Scripting;
using Orleans;
using Orleans.TestingHost;
using Xunit;

namespace WildCat.Tests
{
    /// <summary>
    /// Script events through IScriptEventRunner, the path the gateway uses: per-player scoping,
    /// the time limit, the compiled script cache and inventory limits. Every test uses its own
    /// developer, game, session and players.
    /// </summary>
    [Collection(ClusterCollection.Name)]
    public class ScriptEventRunnerTests
    {
        private readonly InProcessTestCluster cluster;
        public IGrainFactory GrainFactory => this.cluster.Client;

        public ScriptEventRunnerTests(ClusterFixture fixture)
        {
            this.cluster = fixture.Cluster;
        }

        private sealed record Setup(Guid GameKey, Guid SessionKey, IPlayer Player, string PlayerId, Guid DeveloperKey);

        private async Task<Setup> CreateGameAndPlayer()
        {
            var (developerKey, gameKey, sessionKey) = await TestSetup.CreateDeveloperGameAndSession(GrainFactory);
            var player = await TestSetup.CreateLoggedInPlayer(GrainFactory, sessionKey, Guid.NewGuid());
            return new Setup(gameKey, sessionKey, player, player.GetPrimaryKeyString(), developerKey);
        }

        private async Task<IScriptEventCompiler> CreateEvent(Guid gameKey, string name, string script, params EventParameter.ParamType[] parameters)
        {
            var compiler = GrainFactory.GetGrain<IScriptEventCompiler>(ResourcesUtility.GetEventScriptKey(gameKey.ToString(), name));
            await compiler.Initialize(name, parameters.Select(p => new EventParameter { Type = p }).ToList(), script).WaitAsync(TestSetup.Timeout);
            return compiler;
        }

        private Task<object> Run(Setup setup, string eventName, params object[] args)
        {
            return RunAs(setup.GameKey, setup.PlayerId, setup.SessionKey, eventName, args);
        }

        private Task<object> RunAs(Guid gameKey, string playerId, Guid sessionKey, string eventName, params object[] args)
        {
            return GrainFactory.GetGrain<IScriptEventRunner>(gameKey, playerId, null)
                .Run(GrainFactory.GetGrain<ISession>(sessionKey), eventName, args)
                .WaitAsync(TestSetup.Timeout);
        }

        [Fact]
        public async Task RunScriptWithAPI()
        {
            var setup = await CreateGameAndPlayer();
            await CreateEvent(setup.GameKey, "s22",
                "private async Task<int> s22(int a,int b){await WildCat.SetPlayerData(WildCat.CurrentPlayer,\"n\",a.ToString());a = int.Parse((await WildCat.GetPlayerData(WildCat.CurrentPlayer,\"n\"))); return a+b;}",
                EventParameter.ParamType.IntParam, EventParameter.ParamType.IntParam);

            var res = await Run(setup, "s22", 1, 2);

            Assert.Equal(3, (int)res);
        }

        [Fact]
        public async Task AFastScriptReturnsWellUnderASecond()
        {
            var setup = await CreateGameAndPlayer();
            await CreateEvent(setup.GameKey, "add", "int add(int a, int b) { return a + b; }",
                EventParameter.ParamType.IntParam, EventParameter.ParamType.IntParam);
            await Run(setup, "add", 1, 1);

            var watch = Stopwatch.StartNew();
            var result = await Run(setup, "add", 2, 3);
            watch.Stop();

            Assert.Equal(5, (int)result);
            Assert.True(watch.Elapsed < TimeSpan.FromMilliseconds(500), $"Took {watch.Elapsed}.");
        }

        [Fact]
        public async Task AnEndlessLoopFailsWithScriptTimeoutExceptionAtTheLimit()
        {
            var setup = await CreateGameAndPlayer();
            await CreateEvent(setup.GameKey, "spin", "int spin() { long n = 0; while (true) { n++; } }");
            var limit = new ScriptOptions().Timeout;

            var watch = Stopwatch.StartNew();
            var ex = await Assert.ThrowsAsync<ScriptTimeoutException>(() => Run(setup, "spin"));
            watch.Stop();

            Assert.Contains("spin", ex.Message);
            Assert.True(watch.Elapsed >= limit - TimeSpan.FromMilliseconds(250), $"Stopped early, after {watch.Elapsed}.");
            Assert.True(watch.Elapsed < limit + TimeSpan.FromSeconds(2), $"Took {watch.Elapsed}.");

            // The runner is free again right away.
            await CreateEvent(setup.GameKey, "one", "int one() { return 1; }");
            Assert.Equal(1, (int)await Run(setup, "one"));
        }

        [Fact]
        public async Task ScriptsCannotTouchAnotherPlayersPrivateData()
        {
            var setup = await CreateGameAndPlayer();
            var otherSession = await GrainFactory.GetGrain<IDeveloper>(setup.DeveloperKey).InitializeSession(setup.GameKey);
            var other = await TestSetup.CreateLoggedInPlayer(GrainFactory, otherSession, Guid.NewGuid());
            string otherId = other.GetPrimaryKeyString();
            var otherStorage = GrainFactory.GetGrain<IPlayerStorage>(ResourcesUtility.GetPlayPlayerStorageKey(setup.GameKey.ToString(), otherId));
            await otherStorage.SetPlayerString("secret", "hidden");
            await otherStorage.SetPlayerPublicString("title", "champion");

            await CreateEvent(setup.GameKey, "peek", "async Task<string> peek(string other) { return await WildCat.GetPlayerData(other, \"secret\"); }",
                EventParameter.ParamType.StringParam);
            await CreateEvent(setup.GameKey, "poke", "async Task poke(string other) { await WildCat.SetPlayerData(other, \"secret\", \"changed\"); }",
                EventParameter.ParamType.StringParam);
            await CreateEvent(setup.GameKey, "look", "async Task<string> look(string other) { return await WildCat.GetPublicPlayerData(other, \"title\"); }",
                EventParameter.ParamType.StringParam);
            await CreateEvent(setup.GameKey, "mine", "async Task<string> mine() { await WildCat.SetPlayerData(\"secret\", \"own\"); return await WildCat.GetPlayerData(\"secret\"); }");

            AssertScriptFailure<UnauthorizedAccessException>(await Assert.ThrowsAsync<ScriptFailedException>(() => Run(setup, "peek", otherId)));
            AssertScriptFailure<UnauthorizedAccessException>(await Assert.ThrowsAsync<ScriptFailedException>(() => Run(setup, "poke", otherId)));
            Assert.Equal("hidden", (await otherStorage.GetPlayerString("secret")).Item2);
            Assert.Equal("champion", (string)await Run(setup, "look", otherId));
            Assert.Equal("own", (string)await Run(setup, "mine"));
            Assert.Equal("hidden", (await otherStorage.GetPlayerString("secret")).Item2);
        }

        [Fact]
        public async Task ScriptsRejectASessionOfAnotherPlayerOrGame()
        {
            var setup = await CreateGameAndPlayer();
            var otherSession = await GrainFactory.GetGrain<IDeveloper>(setup.DeveloperKey).InitializeSession(setup.GameKey);
            await TestSetup.CreateLoggedInPlayer(GrainFactory, otherSession, Guid.NewGuid());
            await CreateEvent(setup.GameKey, "mine", "async Task<string> mine() { return await WildCat.GetPlayerData(\"secret\"); }");
            var otherGame = await CreateGameAndPlayer();
            await CreateEvent(otherGame.GameKey, "mine", "async Task<string> mine() { return await WildCat.GetPlayerData(\"secret\"); }");

            // This player's runner with another player's session.
            AssertScriptFailure<UnauthorizedAccessException>(
                await Assert.ThrowsAsync<ScriptFailedException>(() => RunAs(setup.GameKey, setup.PlayerId, otherSession, "mine")));
            // Another game's event with this player's session.
            AssertScriptFailure<UnauthorizedAccessException>(
                await Assert.ThrowsAsync<ScriptFailedException>(() => RunAs(otherGame.GameKey, setup.PlayerId, setup.SessionKey, "mine")));
        }

        [Fact]
        public async Task CompiledScriptsAreReusedAndRecompiledOnlyForANewVersion()
        {
            var setup = await CreateGameAndPlayer();
            var compiler = await CreateEvent(setup.GameKey, "version", "int version() { return 1; }");
            var info = await compiler.GetInfo();
            var key = new ScriptCacheKey(info.GameKey, info.Name, info.ScriptHash);
            Assert.True(ScriptCache.Shared.Contains(key));

            long compilations = ScriptCache.Shared.Compilations;
            for (int i = 0; i < 3; i++)
                Assert.Equal(1, (int)await Run(setup, "version"));
            Assert.Equal(compilations, ScriptCache.Shared.Compilations);

            // A silo that has not seen the script compiles it from the compiler grain's source once.
            ScriptCache.Shared.Remove(key);
            Assert.Equal(1, (int)await Run(setup, "version"));
            Assert.Equal(1, (int)await Run(setup, "version"));
            Assert.Equal(compilations + 1, ScriptCache.Shared.Compilations);

            // An edit is a new hash, so no silo can run the old version.
            await compiler.Edit(new List<EventParameter>(), "int version() { return 2; }");
            Assert.Equal(2, (int)await Run(setup, "version"));
            Assert.NotEqual(info.ScriptHash, (await compiler.GetInfo()).ScriptHash);
        }

        [Fact]
        public async Task InventoryGrantsAreBoundedAndNeverOverflow()
        {
            var setup = await CreateGameAndPlayer();
            await CreateEvent(setup.GameKey, "grant",
                "async Task<int> grant(int count) { bool added = await WildCat.AddInventoryItems(WildCat.CurrentPlayer, \"\", new string[] {\"coin\"}, new int[] {count}); return added ? 1 : 0; }",
                EventParameter.ParamType.IntParam);
            string game = setup.GameKey.ToString();
            var inventory = GrainFactory.GetGrain<IPlayerInventory>(ResourcesUtility.GetPlayerInventoryKey(game, setup.PlayerId));
            var coin = GrainFactory.GetGrain<IInventoryItem>(ResourcesUtility.GetItemKey(game, "coin"), ResourcesUtility.GetGameItemsClassName());

            Assert.Equal(1, (int)await Run(setup, "grant", WildCatScriptApi.MaxItemCount));
            AssertScriptFailure<ArgumentException>(await Assert.ThrowsAsync<ScriptFailedException>(() => Run(setup, "grant", WildCatScriptApi.MaxItemCount + 1)));
            AssertScriptFailure<ArgumentException>(await Assert.ThrowsAsync<ScriptFailedException>(() => Run(setup, "grant", -5)));

            // The inventory grain's own failure is not the script's: it keeps its type.
            await inventory.SetItem(new[] { coin }, new[] { int.MaxValue - 10 });
            await Assert.ThrowsAsync<OverflowException>(() => Run(setup, "grant", 11));
            var items = (await inventory.GetInventory()).items;
            Assert.Equal(int.MaxValue - 10, Assert.Single(items).Count);
        }

        [Fact]
        public async Task ArgumentsMustMatchTheEventParameters()
        {
            var setup = await CreateGameAndPlayer();
            await CreateEvent(setup.GameKey, "add", "int add(int a, int b) { return a + b; }",
                EventParameter.ParamType.IntParam, EventParameter.ParamType.IntParam);

            var wrongCount = await Assert.ThrowsAsync<ScriptFailedException>(() => Run(setup, "add", 1));
            AssertScriptFailure<ArgumentException>(wrongCount);
            Assert.Equal("The script event 'add' takes 2 arguments, but 1 were sent.", wrongCount.Message);
            AssertScriptFailure<InvalidCastException>(await Assert.ThrowsAsync<ScriptFailedException>(() => Run(setup, "add", 1, "two")));
        }

        private static void AssertScriptFailure<T>(ScriptFailedException failure) where T : Exception
        {
            Assert.Equal(typeof(T).FullName, failure.ScriptExceptionType);
            Assert.Null(failure.InnerException);
        }

        [Fact]
        public async Task ExceptionsOfGrainsAScriptCallsAreNotTakenForTheScripts()
        {
            var setup = await CreateGameAndPlayer();
            // A negative offset makes the leaderboard grain itself throw ArgumentOutOfRangeException:
            // an ArgumentException like the script API's own refusals, with a text from the server.
            await CreateEvent(setup.GameKey, "top",
                "async Task<int> top() { var page = await WildCat.GetTopEntriesInLeaderBoard(\"board\", -5, 10); return page.count; }");
            await CreateEvent(setup.GameKey, "caught",
                "async Task<int> caught() { try { var page = await WildCat.GetTopEntriesInLeaderBoard(\"board\", -5, 10); return page.count; } " +
                "catch (ArgumentException) { throw new InvalidOperationException(\"The board is not available.\"); } }");
            await CreateEvent(setup.GameKey, "rethrown",
                "async Task<int> rethrown() { try { var page = await WildCat.GetTopEntriesInLeaderBoard(\"board\", -5, 10); return page.count; } " +
                "catch (ArgumentException) { throw; } }");
            await CreateEvent(setup.GameKey, "own", "int own() { throw new ArgumentException(\"Pick a number\", \"n\"); }");

            await Assert.ThrowsAsync<ArgumentOutOfRangeException>(() => Run(setup, "top"));
            await Assert.ThrowsAsync<ArgumentOutOfRangeException>(() => Run(setup, "rethrown"));

            // The script's own exceptions are its failures, with their messages.
            var caught = await Assert.ThrowsAsync<ScriptFailedException>(() => Run(setup, "caught"));
            AssertScriptFailure<InvalidOperationException>(caught);
            Assert.Equal("The board is not available.", caught.Message);
            var own = await Assert.ThrowsAsync<ScriptFailedException>(() => Run(setup, "own"));
            AssertScriptFailure<ArgumentException>(own);
            Assert.Equal("Pick a number", own.Message);
        }
    }
}
