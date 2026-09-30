using Microsoft.Extensions.Logging;
using Orleans;
using Orleans.TestingHost;
using WildCat.Gateway;
using WildCat.Interfaces;
using WildCat.Protocol;
using WildCat.Scripting;
using Xunit;
using static WildCat.Tests.GatewayProfileHandlerTests;
using ParamType = WildCat.Protocol.ParamType;

namespace WildCat.Tests
{
    /// <summary>
    /// ExecuteScriptEvent (66) over real WebSockets: return values of every type, the player and
    /// game a script runs for, and the error answers (unknown event, wrong arguments, a script
    /// that throws, one that runs too long, a bug in a script). Each test uses its own developer,
    /// game and player.
    /// </summary>
    [Collection(ClusterCollection.Name)]
    public class GatewayScriptEventHandlerTests
    {
        private readonly InProcessTestCluster cluster;
        private IGrainFactory GrainFactory => cluster.Client;

        public GatewayScriptEventHandlerTests(ClusterFixture fixture)
        {
            cluster = fixture.Cluster;
        }

        private async Task CreateEvent(Guid gameKey, string name, string script, params EventParameter.ParamType[] parameters)
        {
            var compiler = GrainFactory.GetGrain<IScriptEventCompiler>(ResourcesUtility.GetEventScriptKey(gameKey.ToString(), name));
            await compiler.Initialize(name, parameters.Select(p => new EventParameter { Type = p }).ToList(), script)
                .WaitAsync(TestSetup.Timeout);
        }

        private static ScriptEventExecutionRequestMessage Call(string eventName, params object[] args)
        {
            var request = new ScriptEventExecutionRequestMessage { eventName = eventName };
            request.SetParametersAndTypes(args);
            return request;
        }

        private static Task<OptionalResponse<ScriptEventExecutionResponseMessage>> Run(LoggedIn player, string eventName,
            params object[] args)
        {
            return player.Client.CallAsync<ScriptEventExecutionResponseMessage>(WildCatMsgType.ExecuteScriptEventRequest,
                Call(eventName, args), WildCatMsgType.ExecuteScriptEventResponse, player.Ticket);
        }

        private static async Task<ScriptEventExecutionResponseMessage> RunOk(LoggedIn player, string eventName, params object[] args)
        {
            var response = await Run(player, eventName, args);
            Assert.True(response.isSuccesful, response.errorMsg);
            return response.msg;
        }

        private async Task<(GatewayTestHost Host, LoggedIn Player, Guid GameKey, Guid DeveloperKey)> Start()
        {
            var (developerKey, gameKey) = await TestSetup.CreateDeveloperAndGame(GrainFactory);
            var host = await GatewayTestHost.StartAsync(cluster.Client);
            var player = await LogIn(host, developerKey, gameKey);
            return (host, player, gameKey, developerKey);
        }

        [Fact]
        public async Task AScriptRunsForThePlayerAndGameOfTheSession()
        {
            var (host, player, gameKey, _) = await Start();
            await using var _ = host;
            await using var __ = player.Client;
            await CreateEvent(gameKey, "add", "int add(int a, int b) { return a + b; }",
                EventParameter.ParamType.IntParam, EventParameter.ParamType.IntParam);
            await CreateEvent(gameKey, "remember",
                "async Task<string> remember(string value) { await WildCat.SetPlayerData(\"note\", value); return WildCat.CurrentPlayer; }",
                EventParameter.ParamType.StringParam);

            var sum = await RunOk(player, "add", 2, 3);
            Assert.True(sum.hasReturnValue);
            Assert.Equal(ParamType.IntParam, sum.returnType);
            Assert.Equal(5, sum.GetValue<int>());

            var who = await RunOk(player, "remember", GatewayProfileHandlerTests.Unicode);
            Assert.Equal(player.PlayerId, who.GetValue<string>());
            // The script wrote the private data the player reads with GetCloudData.
            var note = await player.Client.CallAsync<StringMessage>(WildCatMsgType.GetCloudDataRequest, new StringMessage { value = "note" },
                WildCatMsgType.GetCloudDataResponse, player.Ticket);
            Assert.Equal(GatewayProfileHandlerTests.Unicode, note.msg.value);
        }

        [Fact]
        public async Task EveryValueTypeRoundTripsThroughAScript()
        {
            var (host, player, gameKey, _) = await Start();
            await using var _ = host;
            await using var __ = player.Client;
            var cases = new (string Type, EventParameter.ParamType Parameter, ParamType Wire, object[] Values)[]
            {
                ("bool", EventParameter.ParamType.BoolParam, ParamType.BoolParam, new object[] { true, false }),
                ("byte", EventParameter.ParamType.ByteParam, ParamType.ByteParam, new object[] { (byte)0, (byte)255 }),
                ("short", EventParameter.ParamType.ShortParam, ParamType.ShortParam, new object[] { short.MinValue, (short)-1 }),
                ("int", EventParameter.ParamType.IntParam, ParamType.IntParam, new object[] { int.MaxValue, 0 }),
                ("long", EventParameter.ParamType.LongParam, ParamType.LongParam, new object[] { long.MinValue, 1L << 40 }),
                ("float", EventParameter.ParamType.FloatParam, ParamType.FloatParam, new object[] { 1.5f, float.MaxValue }),
                ("double", EventParameter.ParamType.DoubleParam, ParamType.DoubleParam, new object[] { Math.PI, -0.1 }),
                ("string", EventParameter.ParamType.StringParam, ParamType.StringParam,
                    new object[] { GatewayProfileHandlerTests.Unicode, "" }),
                ("byte[]", EventParameter.ParamType.ByteArrayParam, ParamType.ByteArrayParam,
                    new object[] { new byte[] { 0, 1, 127, 128, 255, 0 }, new byte[0], Enumerable.Range(0, 5000).Select(i => (byte)i).ToArray() }),
            };

            foreach (var (type, parameter, wire, values) in cases)
            {
                string name = "echo" + parameter;
                await CreateEvent(gameKey, name, $"{type} {name}({type} value) {{ return value; }}", parameter);
                foreach (var value in values)
                {
                    var echoed = await RunOk(player, name, value);
                    Assert.True(echoed.hasReturnValue, $"{type} {value}");
                    Assert.Equal(wire, echoed.returnType);
                    Assert.Equal(value, echoed.returnValue);
                }
            }
        }

        [Fact]
        public async Task AnEventWithoutAResultAnswersWithoutAValue()
        {
            var (host, player, gameKey, _) = await Start();
            await using var _ = host;
            await using var __ = player.Client;
            await CreateEvent(gameKey, "nothing", "void nothing() { }");
            await CreateEvent(gameKey, "later", "async Task later() { await WildCat.SetPlayerData(\"k\", \"v\"); }");
            await CreateEvent(gameKey, "none", "string none() { return null; }");

            Assert.False((await RunOk(player, "nothing")).hasReturnValue);
            Assert.False((await RunOk(player, "later")).hasReturnValue);
            Assert.False((await RunOk(player, "none")).hasReturnValue);
        }

        [Fact]
        public async Task AnUnknownEventIsRefused()
        {
            var (host, player, gameKey, developerKey) = await Start();
            await using var _ = host;
            await using var __ = player.Client;

            AssertFailed(await Run(player, "noSuchEvent"), GatewayErrors.Refused, "The script event 'noSuchEvent' does not exist.");
            AssertFailed(await Run(player, ""), GatewayErrors.Refused, "The script event '' does not exist.");

            // Events belong to one game: another game of the same developer does not see them.
            var otherGame = await GrainFactory.GetGrain<IDeveloper>(developerKey).AddGame(new GameInfo { Name = "game2" });
            await CreateEvent(otherGame, "elsewhere", "int elsewhere() { return 1; }");
            AssertFailed(await Run(player, "elsewhere"), GatewayErrors.Refused, "The script event 'elsewhere' does not exist.");

            // A deleted event is gone for clients too.
            await CreateEvent(gameKey, "gone", "int gone() { return 1; }");
            Assert.Equal(1, (await RunOk(player, "gone")).GetValue<int>());
            await GrainFactory.GetGrain<IScriptEventCompiler>(ResourcesUtility.GetEventScriptKey(gameKey.ToString(), "gone")).Delete();
            AssertFailed(await Run(player, "gone"), GatewayErrors.Refused, "The script event 'gone' does not exist.");

            Assert.DoesNotContain(host.Logs.Entries, e => e.Level >= LogLevel.Error);
        }

        [Fact]
        public async Task ArgumentsThatDoNotFitAreRefused()
        {
            var (host, player, gameKey, _) = await Start();
            await using var _ = host;
            await using var __ = player.Client;
            await CreateEvent(gameKey, "add", "int add(int a, int b) { return a + b; }",
                EventParameter.ParamType.IntParam, EventParameter.ParamType.IntParam);

            AssertFailed(await Run(player, "add", 1), GatewayErrors.Refused, "The script event 'add' takes 2 arguments, but 1 were sent.");
            AssertFailed(await Run(player, "add", 1, 2, 3), GatewayErrors.Refused, "The script event 'add' takes 2 arguments, but 3 were sent.");

            var wrongType = await Run(player, "add", 1, "two");
            Assert.False(wrongType.isSuccesful);
            Assert.Equal(GatewayErrors.Refused, wrongType.errorCode);
            Assert.Contains("cast", wrongType.errorMsg);
            var wrongWidth = await Run(player, "add", 1, 2L);
            Assert.False(wrongWidth.isSuccesful);
            Assert.Equal(GatewayErrors.Refused, wrongWidth.errorCode);

            Assert.Equal(3, (await RunOk(player, "add", 1, 2)).GetValue<int>());
            Assert.DoesNotContain(host.Logs.Entries, e => e.Level >= LogLevel.Error);
        }

        [Fact]
        public async Task AScriptThatThrowsIsAnsweredWithItsMessage()
        {
            var (host, player, gameKey, _) = await Start();
            await using var _ = host;
            await using var __ = player.Client;
            await CreateEvent(gameKey, "buy", "int buy(int coins) { if (coins < 10) throw new InvalidOperationException(\"Not enough coins\"); return coins - 10; }",
                EventParameter.ParamType.IntParam);
            await CreateEvent(gameKey, "plain", "int plain() { throw new Exception(\"Plain \" + \"failure\"); }");
            await CreateEvent(gameKey, "argument", "int argument(int n) { throw new ArgumentException(\"n must be even\", \"n\"); }",
                EventParameter.ParamType.IntParam);
            await CreateEvent(gameKey, "other", "async Task<string> other(string id) { return await WildCat.GetPlayerData(id, \"secret\"); }",
                EventParameter.ParamType.StringParam);
            await CreateEvent(gameKey, "deep", "int deep(int n) { return deep(n + 1) + 1; }", EventParameter.ParamType.IntParam);

            Assert.Equal(5, (await RunOk(player, "buy", 15)).GetValue<int>());
            AssertFailed(await Run(player, "buy", 3), GatewayErrors.Refused, "Not enough coins");
            AssertFailed(await Run(player, "plain"), GatewayErrors.Refused, "Plain failure");
            AssertFailed(await Run(player, "argument", 1), GatewayErrors.Refused, "n must be even");

            // The script API refuses another player's private data.
            var refused = await Run(player, "other", TestSetup.UniqueId("other"));
            Assert.False(refused.isSuccesful);
            Assert.Equal(GatewayErrors.Refused, refused.errorCode);
            Assert.NotEqual(GatewayErrors.InternalError, refused.errorMsg);

            // The sandbox stops unbounded recursion before the stack runs out.
            var deep = await Run(player, "deep", 0);
            Assert.False(deep.isSuccesful);
            Assert.Equal(GatewayErrors.Refused, deep.errorCode);
            Assert.StartsWith("The script event 'deep' nested calls deeper than", deep.errorMsg);

            Assert.DoesNotContain(host.Logs.Entries, e => e.Level >= LogLevel.Error);
        }

        [Fact]
        public async Task ABugInAScriptIsAnInternalError()
        {
            var (host, player, gameKey, _) = await Start();
            await using var _ = host;
            await using var __ = player.Client;
            await CreateEvent(gameKey, "oops", "int oops(string s) { return s.Length / (s.Length - s.Length); }",
                EventParameter.ParamType.StringParam);

            AssertFailed(await Run(player, "oops", "abc"), GatewayErrors.Internal, GatewayErrors.InternalError);

            var logged = Assert.Single(host.Logs.Entries, e => e.Level == LogLevel.Error);
            Assert.IsType<DivideByZeroException>(logged.Exception);
        }

        [Fact]
        public async Task AFailureOfAGrainTheScriptCallsIsAnInternalError()
        {
            var (host, player, gameKey, _) = await Start();
            await using var _ = host;
            await using var __ = player.Client;
            // A negative offset makes the leaderboard grain throw ArgumentOutOfRangeException, an
            // ArgumentException like the ones scripts throw, but with the server's own text. The
            // handler used to forward the text of any such exception.
            await CreateEvent(gameKey, "top",
                "async Task<int> top() { var page = await WildCat.GetTopEntriesInLeaderBoard(\"board\", -5, 10); return page.count; }");
            await CreateEvent(gameKey, "caught",
                "async Task<int> caught() { try { var page = await WildCat.GetTopEntriesInLeaderBoard(\"board\", -5, 10); return page.count; } " +
                "catch (ArgumentException) { throw new InvalidOperationException(\"The board is not available.\"); } }");

            AssertFailed(await Run(player, "top"), GatewayErrors.Internal, GatewayErrors.InternalError);
            var logged = Assert.Single(host.Logs.Entries, e => e.Level == LogLevel.Error);
            Assert.IsType<ArgumentOutOfRangeException>(logged.Exception);

            // A script that catches it and throws its own refuses the request with its own text.
            AssertFailed(await Run(player, "caught"), GatewayErrors.Refused, "The board is not available.");
            Assert.Single(host.Logs.Entries, e => e.Level == LogLevel.Error);

            // Script event names are grain keys, so they have a length limit.
            AssertFailed(await Run(player, new string('e', InputLimits.MaxNameLength + 1)), GatewayErrors.Refused,
                InputLimits.TooLong("script event name", InputLimits.MaxNameLength));
        }

        [Fact]
        public async Task AScriptThatRunsTooLongIsStoppedWhileOtherRequestsGoOn()
        {
            var (host, player, gameKey, _) = await Start();
            await using var _ = host;
            await using var __ = player.Client;
            await CreateEvent(gameKey, "spin", "int spin() { long n = 0; while (true) { n++; } }");
            await CreateEvent(gameKey, "one", "int one() { return 1; }");

            var spin = player.Client.Request(WildCatMsgType.ExecuteScriptEventRequest, Call("spin"), player.Ticket);
            await player.Client.SendAsync(spin);

            // The connection keeps serving requests while the script runs.
            var name = await player.Client.CallAsync<StringMessage>(WildCatMsgType.GetNameRequest, new EmptyMessage(),
                WildCatMsgType.GetNameResponse, player.Ticket);
            Assert.True(name.isSuccesful, name.errorMsg);
            Assert.DoesNotContain(player.Client.Received, m => m.hasRequestID && m.requestID == spin.requestID);

            var reply = await player.Client.WaitForAsync(m => m.hasRequestID && m.requestID == spin.requestID, "the spin reply");
            Assert.Equal(WildCatMsgType.ExecuteScriptEventResponse, reply.msgType);
            var stopped = reply.GetPayload<OptionalResponse<ScriptEventExecutionResponseMessage>>();
            Assert.False(stopped.isSuccesful);
            Assert.Equal(GatewayErrors.Refused, stopped.errorCode);
            Assert.Equal($"The script event 'spin' did not finish within {new ScriptOptions().Timeout.TotalSeconds:0.###} seconds and was stopped.",
                stopped.errorMsg);

            // The player's scripts run again right away.
            Assert.Equal(1, (await RunOk(player, "one")).GetValue<int>());
            Assert.DoesNotContain(host.Logs.Entries, e => e.Level >= LogLevel.Error);
        }
    }
}
