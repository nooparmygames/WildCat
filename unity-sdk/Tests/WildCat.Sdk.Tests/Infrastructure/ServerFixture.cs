using System;
using System.Collections.Concurrent;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Orleans;
using Orleans.Hosting;
using Orleans.TestingHost;
using WildCat.Gateway;
using WildCat.Grains;
using WildCat.Interfaces;
using Xunit;

namespace WildCat.Sdk.Tests
{
    /// <summary>
    /// A real WildCat server in this process: one Orleans test silo with the memory providers
    /// (configured like WildCat.Host and the backend's ClusterFixture) and the WebSocket gateway on
    /// Kestrel at a free loopback port. A middleware in front of /ws lets tests drop every open
    /// socket from the server side and refuse new ones, to exercise reconnection.
    /// </summary>
    public sealed class ServerFixture : IAsyncLifetime
    {
        private readonly ConcurrentDictionary<HttpContext, byte> sockets = new ConcurrentDictionary<HttpContext, byte>();
        private WebApplication app;
        private int connectionAttempts;
        private volatile bool refuseConnections;

        public InProcessTestCluster Cluster { get; private set; }

        public IGrainFactory Grains
        {
            get { return Cluster.Client; }
        }

        /// <summary>Every email the grains sent.</summary>
        public CapturingEmailSender Emails { get; } = new CapturingEmailSender();

        /// <summary>The gateway's WebSocket URL.</summary>
        public Uri ServerUri { get; private set; }

        /// <summary>WebSocket requests that reached the gateway's path, refused ones included.</summary>
        public int ConnectionAttempts
        {
            get { return Volatile.Read(ref connectionAttempts); }
        }

        /// <summary>WebSockets open right now.</summary>
        public int OpenSockets
        {
            get { return sockets.Count; }
        }

        /// <summary>While true, new WebSocket requests get 503 instead of reaching the gateway.</summary>
        public bool RefuseConnections
        {
            get { return refuseConnections; }
            set { refuseConnections = value; }
        }

        public async Task InitializeAsync()
        {
            var builder = new InProcessTestClusterBuilder(initialSilosCount: 1);
            builder.ConfigureSilo((options, silo) =>
            {
                silo.AddWildCatStorage(new OrleansHostingOptions { Provider = OrleansProvider.Memory }, connectionString: null);
                silo.AddGameStreams();
                silo.AddLegacyCallChainReentrancy();
                silo.Services.AddSingleton<IEmailSender>(Emails);
            });
            builder.ConfigureClient(client => client.AddMemoryStreams(StreamNames.PROVIDER_NAME));
            Cluster = builder.Build();
            await Cluster.DeployAsync();

            var web = WebApplication.CreateSlimBuilder();
            web.WebHost.UseUrls("http://127.0.0.1:0");
            web.Logging.ClearProviders();
            web.Services.AddSingleton(Cluster.Client);
            web.Services.AddGameGateway();
            app = web.Build();
            app.UseWebSockets();
            app.Use(async (context, next) =>
            {
                if (!context.Request.Path.StartsWithSegments("/ws"))
                {
                    await next(context);
                    return;
                }
                Interlocked.Increment(ref connectionAttempts);
                if (refuseConnections)
                {
                    context.Response.StatusCode = StatusCodes.Status503ServiceUnavailable;
                    return;
                }
                sockets[context] = 0;
                try
                {
                    await next(context);
                }
                finally
                {
                    sockets.TryRemove(context, out _);
                }
            });
            app.MapGameGateway("/ws");
            await app.StartAsync().WaitAsync(Wait.Long);

            var address = new Uri(app.Urls.First());
            ServerUri = new Uri($"ws://{address.Host}:{address.Port}/ws");
        }

        /// <summary>Aborts every open WebSocket on the server side, as a crashed proxy or a lost network would.</summary>
        public int DropAllConnections()
        {
            int dropped = 0;
            foreach (HttpContext context in sockets.Keys.ToList())
            {
                context.Abort();
                dropped++;
            }
            return dropped;
        }

        /// <summary>A new developer and game, so every test has its own players, leaderboards and matchmaker.</summary>
        public async Task<(Guid DeveloperKey, Guid GameKey)> CreateGameAsync()
        {
            Guid developerKey = await Grains.GetGrain<IDeveloperManager>("main").AddDeveloper(new DeveloperState
            {
                FirstName = "Test",
                Email = $"{Unique.Id("dev")}@example.com",
                PasswordHash = "",
            });
            Guid gameKey = await Grains.GetGrain<IDeveloper>(developerKey).AddGame(new GameInfo { Name = "sdk-test" });
            return (developerKey, gameKey);
        }

        public async Task DisposeAsync()
        {
            if (app != null)
            {
                DropAllConnections();
                await app.StopAsync().WaitAsync(Wait.Long);
                await app.DisposeAsync();
            }
            if (Cluster != null)
                await Cluster.DisposeAsync();
        }
    }

    [CollectionDefinition(Name)]
    public sealed class ServerCollection : ICollectionFixture<ServerFixture>
    {
        public const string Name = "WildCat server";
    }
}
