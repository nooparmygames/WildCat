using Microsoft.Extensions.DependencyInjection;
using Orleans;
using Orleans.Runtime;
using Orleans.Storage;
using Orleans.TestingHost;
using WildCat.Grains;
using WildCat.Interfaces;
using Xunit;

namespace WildCat.Tests
{
    /// <summary>
    /// Session persistence and idle expiry. Instead of waiting out the 60 minute idle window, the
    /// expiry tests deactivate the session and move its stored last call time into the past, which
    /// is what a session left alone for that long looks like when it is next activated.
    /// </summary>
    [Collection(ClusterCollection.Name)]
    public class SessionTests
    {
        // The state name the Session grain passes to [PersistentState].
        private const string StateName = GrainStateNames.Session;
        private readonly InProcessTestCluster cluster;
        private IGrainFactory GrainFactory => cluster.Client;

        public SessionTests(ClusterFixture fixture)
        {
            cluster = fixture.Cluster;
        }

        [Fact]
        public async Task SessionSurvivesDeactivation()
        {
            var (developerKey, gameKey, sessionKey) = await TestSetup.CreateDeveloperGameAndSession(GrainFactory);
            var player = await TestSetup.CreateLoggedInPlayer(GrainFactory, sessionKey, Guid.NewGuid());
            var session = GrainFactory.GetGrain<ISession>(sessionKey);

            await cluster.Deactivate(session);

            Assert.True(await session.IsInitialized());
            var info = await session.TouchAndGetInfo();
            Assert.Equal(developerKey, info.DeveloperKey);
            Assert.Equal(gameKey, info.GameKey);
            Assert.Equal(player.GetPrimaryKeyString(), info.UserID);
        }

        [Fact]
        public async Task SessionIdleLongerThanTheWindowIsRejectedAndCleared()
        {
            var (_, _, sessionKey) = await TestSetup.CreateDeveloperGameAndSession(GrainFactory);
            var session = GrainFactory.GetGrain<ISession>(sessionKey);
            await session.TouchAndGetInfo();

            await SetStoredLastCallTime(session, DateTime.UtcNow - TimeSpan.FromMinutes(61));

            Assert.True(await session.IsExpired());
            await Assert.ThrowsAsync<SessionExpiredException>(() => session.TouchAndGetInfo());

            // The call that found the session expired cleared it instead of reviving it.
            Assert.False(await session.IsInitialized());
            await Assert.ThrowsAsync<SessionNotInitializedException>(() => session.Touch());
            var stored = await ReadStoredState(session);
            Assert.False(stored.RecordExists);
        }

        [Fact]
        public async Task PlainTouchAlsoRejectsAnExpiredSession()
        {
            var (_, _, sessionKey) = await TestSetup.CreateDeveloperGameAndSession(GrainFactory);
            var session = GrainFactory.GetGrain<ISession>(sessionKey);

            await SetStoredLastCallTime(session, DateTime.UtcNow - TimeSpan.FromMinutes(61));

            await Assert.ThrowsAsync<SessionExpiredException>(() => session.Touch());
            Assert.False(await session.IsInitialized());
        }

        [Fact]
        public async Task SessionUsedWithinTheWindowStaysAlive()
        {
            var (_, gameKey, sessionKey) = await TestSetup.CreateDeveloperGameAndSession(GrainFactory);
            var session = GrainFactory.GetGrain<ISession>(sessionKey);

            await SetStoredLastCallTime(session, DateTime.UtcNow - TimeSpan.FromMinutes(59));

            Assert.False(await session.IsExpired());
            var info = await session.TouchAndGetInfo();
            Assert.Equal(gameKey, info.GameKey);
            // The touch restarted the idle clock and, being over a minute after the last write,
            // wrote it through.
            var stored = await ReadStoredState(session);
            Assert.True(DateTime.UtcNow - stored.State.LastCallTime < TimeSpan.FromMinutes(1));
        }

        [Fact]
        public async Task EndedSessionIsGoneFromStorage()
        {
            var (_, _, sessionKey) = await TestSetup.CreateDeveloperGameAndSession(GrainFactory);
            var session = GrainFactory.GetGrain<ISession>(sessionKey);

            await session.EndSession();
            await cluster.Deactivate(session);

            Assert.False(await session.IsInitialized());
            Assert.False((await ReadStoredState(session)).RecordExists);
        }

        [Fact]
        public async Task SessionErrorsAreInterfaceTypesWithRealMessages()
        {
            // The gateway catches these by type, so they live next to ISession.
            Assert.Equal("WildCat.Interfaces", typeof(SessionNotInitializedException).Namespace);
            Assert.Equal("WildCat.Interfaces", typeof(SessionExpiredException).Namespace);
            Assert.Equal("WildCat.Interfaces", typeof(InvalidGameException).Namespace);
            Assert.DoesNotContain("Exception of type", new InvalidGameException().Message);

            var unknown = GrainFactory.GetGrain<ISession>(Guid.NewGuid());
            var notInitialized = await Assert.ThrowsAsync<SessionNotInitializedException>(() => unknown.TouchAndGetInfo());
            Assert.Equal(SessionNotInitializedException.DefaultMessage, notInitialized.Message);

            var (_, _, sessionKey) = await TestSetup.CreateDeveloperGameAndSession(GrainFactory);
            var session = GrainFactory.GetGrain<ISession>(sessionKey);
            await SetStoredLastCallTime(session, DateTime.UtcNow - TimeSpan.FromMinutes(61));
            var expired = await Assert.ThrowsAsync<SessionExpiredException>(() => session.Touch());
            Assert.Equal(SessionExpiredException.DefaultMessage, expired.Message);
        }

        private IGrainStorage GetStorage()
        {
            var services = cluster.GetSiloServiceProvider(cluster.Silos[0].SiloAddress);
            return services.GetRequiredKeyedService<IGrainStorage>("Default");
        }

        private async Task<GrainState<SessionState>> ReadStoredState(ISession session)
        {
            var state = new GrainState<SessionState>();
            await GetStorage().ReadStateAsync(StateName, session.GetGrainId(), state).WaitAsync(TestSetup.Timeout);
            return state;
        }

        /// <summary>
        /// Deactivates the session and rewrites its stored last call time. The next call activates
        /// the session from the rewritten state.
        /// </summary>
        private async Task SetStoredLastCallTime(ISession session, DateTime lastCallUtc)
        {
            await cluster.Deactivate(session);
            var state = await ReadStoredState(session);
            Assert.True(state.RecordExists);
            state.State.LastCallTime = lastCallUtc;
            await GetStorage().WriteStateAsync(StateName, session.GetGrainId(), state).WaitAsync(TestSetup.Timeout);
        }
    }
}
