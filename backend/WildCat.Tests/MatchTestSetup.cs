using System.Collections.Concurrent;
using Orleans;
using Orleans.Streams;
using WildCat.Interfaces;
using WildCat.Protocol;

namespace WildCat.Tests
{
    /// <summary>A player logged in on a session of its own, with the ticket its login used.</summary>
    internal sealed record MatchPlayer(IPlayer Player, Guid Ticket)
    {
        public string Id => Player.GetPrimaryKeyString();

        /// <summary>What the gateway does when this player's socket closes.</summary>
        public Task Disconnect() => Player.Disconnected(Ticket);
    }

    /// <summary>A started match and its players, for the grain level match tests.</summary>
    internal sealed record StartedMatch(IRelayServerGame Game, Guid DeveloperKey, Guid GameKey, MatchPlayer[] Players)
    {
        public MatchPlayer A => Players[0];
        public MatchPlayer B => Players[1];
        public Guid Id => Game.GetPrimaryKey(out string _);
    }

    /// <summary>
    /// Builds matches the way the matchmaker does (StartGame, then PlayerStarted for the players
    /// that join), with a new developer, game and logged in players each time.
    /// </summary>
    internal static class MatchTestSetup
    {
        /// <summary>
        /// A deadlock only ends with Orleans' 30 second response timeout. The call cycle tests
        /// give up long before that, so a regression fails fast and says what hung.
        /// </summary>
        public static readonly TimeSpan DeadlockLimit = TimeSpan.FromSeconds(10);

        /// <summary>A mode for <paramref name="players"/> players.</summary>
        public static GameMode Mode(bool finishWithOneDisconnect, int players = 2)
        {
            var mode = GameMode.CreateDefault();
            mode.Name = TestSetup.UniqueId("mode");
            mode.MinPlayers = players;
            mode.MaxPlayers = players;
            mode.FinishGameWithOneDisconnect = finishWithOneDisconnect;
            return mode;
        }

        /// <summary>
        /// Starts a match of <paramref name="players"/> logged in players in <paramref name="mode"/>
        /// (the default two player mode when null) and joins the first <paramref name="joined"/>
        /// of them (all when null).
        /// </summary>
        public static async Task<StartedMatch> StartAsync(IGrainFactory grains, GameMode mode = null, int players = 2, int? joined = null)
        {
            var (developerKey, gameKey) = await TestSetup.CreateDeveloperAndGame(grains);
            var developer = grains.GetGrain<IDeveloper>(developerKey);
            var logins = new MatchPlayer[players];
            for (int i = 0; i < players; ++i)
            {
                var session = await developer.InitializeSession(gameKey);
                var ticket = Guid.NewGuid();
                logins[i] = new MatchPlayer(await TestSetup.CreateLoggedInPlayer(grains, session, ticket), ticket);
            }

            var game = grains.GetGrain<IRelayServerGame>(Guid.NewGuid(), gameKey.ToString(), null);
            await game.StartGame(logins.Select(p => p.Player).ToArray(), mode);
            for (int i = 0; i < (joined ?? players); ++i)
                await game.PlayerStarted(logins[i].Player);
            return new StartedMatch(game, developerKey, gameKey, logins);
        }

        /// <summary>
        /// Waits at most <see cref="DeadlockLimit"/> for <paramref name="calls"/>. False when they
        /// did not finish in time; an exception they threw in time is thrown.
        /// </summary>
        public static async Task<bool> FinishInTime(Task calls)
        {
            if (await Task.WhenAny(calls, Task.Delay(DeadlockLimit)) != calls)
                return false;
            await calls;
            return true;
        }

        /// <summary>
        /// Runs <paramref name="work"/> over and over until <paramref name="duration"/> has passed.
        /// </summary>
        public static Task RepeatFor(TimeSpan duration, Func<int, Task> work)
        {
            return Task.Run(async () =>
            {
                var end = DateTime.UtcNow + duration;
                for (int i = 0; DateTime.UtcNow < end; ++i)
                    await work(i);
            });
        }

        /// <summary>The matches in the player's state (RunningGames), by id.</summary>
        public static async Task<List<Guid>> RunningMatches(IPlayer player)
        {
            var state = await player.GetState();
            return state.RunningGames.Select(g => g.GetPrimaryKey(out string _)).ToList();
        }
    }

    /// <summary>
    /// Records the frames published on a player's PlayerConnection stream, which the gateway
    /// would push to the player's socket.
    /// </summary>
    internal sealed class PushRecorder : IAsyncDisposable
    {
        private readonly ConcurrentQueue<WildCatMessageBase> frames = new();
        private StreamSubscriptionHandle<PlayerConnectionMessage> handle;

        private PushRecorder()
        {
        }

        public IReadOnlyList<WildCatMessageBase> Frames => frames.ToArray();

        public static async Task<PushRecorder> StartAsync(IClusterClient client, string playerId)
        {
            var recorder = new PushRecorder();
            var stream = client.GetStreamProvider(StreamNames.PROVIDER_NAME)
                .GetStream<PlayerConnectionMessage>(StreamNames.PlayerConnection(playerId));
            recorder.handle = await stream.SubscribeAsync((message, _) =>
            {
                recorder.frames.Enqueue(WireCodec.Decode(message.data));
                return Task.CompletedTask;
            });
            return recorder;
        }

        /// <summary>Waits, with the test time limit, for a frame that matches.</summary>
        public async Task<WildCatMessageBase> WaitForAsync(Func<WildCatMessageBase, bool> match, string description)
        {
            WildCatMessageBase found = null;
            await TestSetup.WaitUntilAsync(() => (found = frames.FirstOrDefault(match)) != null, description);
            return found;
        }

        public async ValueTask DisposeAsync()
        {
            if (handle != null)
                await handle.UnsubscribeAsync();
        }
    }
}
