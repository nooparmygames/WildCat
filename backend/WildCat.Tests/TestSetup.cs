using Orleans;
using Orleans.Runtime;
using Orleans.TestingHost;
using WildCat.Interfaces;

namespace WildCat.Tests
{
    /// <summary>
    /// Helpers for tests that need their own developer, game, session or player. Everything they
    /// create has a unique key, so tests do not depend on each other or on the order they run in.
    /// </summary>
    internal static class TestSetup
    {
        /// <summary>
        /// Upper bound for a single wait in a test, so a hung call fails the test instead of the run.
        /// </summary>
        public static readonly TimeSpan Timeout = TimeSpan.FromSeconds(30);

        /// <summary>
        /// A unique id without underscores (inventory and storage keys split on them).
        /// </summary>
        public static string UniqueId(string prefix) => $"{prefix}-{Guid.NewGuid():N}";

        public static Task<Guid> CreateDeveloper(IGrainFactory grains, string password = "")
        {
            return grains.GetGrain<IDeveloperManager>("main").AddDeveloper(new DeveloperState
            {
                FirstName = "Test",
                Email = $"{UniqueId("dev")}@example.com",
                PasswordHash = password
            });
        }

        public static async Task<(Guid DeveloperKey, Guid GameKey)> CreateDeveloperAndGame(IGrainFactory grains)
        {
            var developerKey = await CreateDeveloper(grains);
            var gameKey = await grains.GetGrain<IDeveloper>(developerKey).AddGame(new GameInfo { Name = "game1" });
            return (developerKey, gameKey);
        }

        public static async Task<(Guid DeveloperKey, Guid GameKey, Guid SessionKey)> CreateDeveloperGameAndSession(IGrainFactory grains)
        {
            var developerKey = await CreateDeveloper(grains);
            var developer = grains.GetGrain<IDeveloper>(developerKey);
            var gameKey = await developer.AddGame(new GameInfo { Name = "game1" });
            var sessionKey = await developer.InitializeSession(gameKey);
            return (developerKey, gameKey, sessionKey);
        }

        /// <summary>
        /// Creates a device account and logs it in on the given session.
        /// </summary>
        public static async Task<IPlayer> CreateLoggedInPlayer(IGrainFactory grains, Guid sessionKey, Guid loginTicket)
        {
            string id = UniqueId("player");
            var player = grains.GetGrain<IPlayer>(id);
            await player.CreateAccountWithDevice(id);
            var login = await player.LoginWithDeviceID(id, sessionKey, loginTicket);
            if (login == null)
                throw new InvalidOperationException("The test player could not log in.");
            return player;
        }

        /// <summary>
        /// Checks <paramref name="condition"/> every 50 ms until it holds, for at most
        /// <see cref="Timeout"/>.
        /// </summary>
        public static async Task WaitUntilAsync(Func<Task<bool>> condition, string description)
        {
            var deadline = DateTime.UtcNow + Timeout;
            while (!await condition())
            {
                if (DateTime.UtcNow > deadline)
                    throw new TimeoutException($"Gave up waiting for {description}.");
                await Task.Delay(50);
            }
        }

        public static Task WaitUntilAsync(Func<bool> condition, string description)
        {
            return WaitUntilAsync(() => Task.FromResult(condition()), description);
        }

        /// <summary>
        /// Deactivates a grain and waits, with <see cref="Timeout"/>, until it is gone. The next
        /// call activates it again from storage.
        /// </summary>
        public static Task Deactivate(this InProcessTestCluster cluster, IAddressable grain)
        {
            return cluster.DeactivateAsync(grain).WaitAsync(Timeout);
        }
    }
}
