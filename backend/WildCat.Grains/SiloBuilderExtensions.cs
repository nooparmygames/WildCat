using System.Net;
using System.Net.NetworkInformation;
using System.Net.Sockets;
using WildCat.Interfaces;
using Orleans.Configuration;
using WildCat.Scripting;
using Orleans.Hosting;
using Microsoft.Extensions.DependencyInjection;
using Orleans.Serialization;
using Newtonsoft.Json;

namespace WildCat.Grains
{
    /// <summary>
    /// Silo configuration shared by the silo host (WildCat.Host/Program.cs) and the tests
    /// (WildCat.Tests/ClusterFixture.cs and the PostgreSql*Tests.cs files), so they stay in sync.
    /// </summary>
    public static class SiloBuilderExtensions
    {
        // Grain storage names (GrainStorageNames): "Default" for every grain state except the
        // leaderboards, "blob" for the leaderboards, and "PubSubStore" for the stream subscriptions
        // of the memory streams. The state names inside them are in GrainStateNames.
        private const string BlobStorageName = GrainStorageNames.Blob;
        private const string PubSubStorageName = GrainStorageNames.PubSub;

        /// <summary>
        /// Configures clustering, ids, endpoints, grain storage and reminders from
        /// <paramref name="options"/> by calling <see cref="AddWildCatClustering"/> and
        /// <see cref="AddWildCatStorage"/>. <paramref name="connectionString"/> is only used, and
        /// then required, by the PostgreSQL provider. Streams are configured separately with
        /// <see cref="AddGameStreams"/> and stay in memory with either provider.
        /// </summary>
        /// <exception cref="InvalidOperationException">The options are invalid, or the provider is
        /// PostgreSQL and the connection string is empty.</exception>
        public static ISiloBuilder AddWildCatProviders(this ISiloBuilder builder, OrleansHostingOptions options, string connectionString)
        {
            builder.AddWildCatClustering(options, connectionString);
            builder.AddWildCatStorage(options, connectionString);
            return builder;
        }

        /// <summary>
        /// Sets ClusterId, ServiceId and the silo and gateway ports, and picks the membership
        /// provider: localhost clustering on loopback for Memory, ADO.NET clustering for
        /// PostgreSQL (listening on every address and advertising
        /// <see cref="OrleansHostingOptions.AdvertisedHost"/> or the first non-loopback IPv4
        /// address). Test clusters that bring their own membership skip this and call
        /// <see cref="AddWildCatStorage"/> only.
        /// </summary>
        public static ISiloBuilder AddWildCatClustering(this ISiloBuilder builder, OrleansHostingOptions options, string connectionString)
        {
            Validate(options);
            builder.Configure<ClusterOptions>(o =>
            {
                o.ClusterId = options.ClusterId;
                o.ServiceId = options.ServiceId;
            });

            switch (options.Provider)
            {
                case OrleansProvider.Memory:
                    builder.UseLocalhostClustering(options.SiloPort, options.GatewayPort,
                        primarySiloEndpoint: null, serviceId: options.ServiceId, clusterId: options.ClusterId);
                    break;

                case OrleansProvider.PostgreSQL:
                    string cs = RequireConnectionString(connectionString);
                    IPAddress advertised = ResolveAdvertisedAddress(options.AdvertisedHost);
                    builder.ConfigureEndpoints(advertised, options.SiloPort, options.GatewayPort, listenOnAnyHostAddress: true);
                    builder.UseAdoNetClustering(o =>
                    {
                        o.Invariant = OrleansHostingOptions.PostgreSqlInvariant;
                        o.ConnectionString = cs;
                    });
                    break;
            }
            return builder;
        }

        /// <summary>
        /// Registers the grain storage providers ("Default", "blob", "PubSubStore") and the
        /// reminder service for the selected provider: memory for Memory, ADO.NET on PostgreSQL
        /// for PostgreSQL. The ADO.NET providers load the Npgsql factory by its invariant name
        /// themselves, so nothing has to be registered with DbProviderFactories. Both store state
        /// as JSON written with <see cref="WriteDefaultValues"/>.
        /// </summary>
        public static ISiloBuilder AddWildCatStorage(this ISiloBuilder builder, OrleansHostingOptions options, string connectionString)
        {
            Validate(options);
            // The memory storage serializes state with the same JSON settings as the ADO.NET
            // storage (it used to be configured for PostgreSQL only), so Memory mode needs it too.
            builder.Services.Configure<OrleansJsonSerializerOptions>(WriteDefaultValues);
            switch (options.Provider)
            {
                case OrleansProvider.Memory:
                    builder.AddMemoryGrainStorageAsDefault();
                    builder.AddMemoryGrainStorage(BlobStorageName);
                    builder.AddMemoryGrainStorage(PubSubStorageName);
                    builder.UseInMemoryReminderService();
                    break;

                case OrleansProvider.PostgreSQL:
                    string cs = RequireConnectionString(connectionString);
                    void Storage(AdoNetGrainStorageOptions o)
                    {
                        o.Invariant = OrleansHostingOptions.PostgreSqlInvariant;
                        o.ConnectionString = cs;
                    }
                    builder.AddAdoNetGrainStorageAsDefault(Storage);
                    builder.AddAdoNetGrainStorage(BlobStorageName, Storage);
                    builder.AddAdoNetGrainStorage(PubSubStorageName, Storage);
                    builder.UseAdoNetReminderService(o =>
                    {
                        o.Invariant = OrleansHostingOptions.PostgreSqlInvariant;
                        o.ConnectionString = cs;
                    });
                    break;
            }
            return builder;
        }

        /// <summary>
        /// Memory streams for the game provider (<see cref="StreamNames.PROVIDER_NAME"/>).
        /// Memory streams deliver through pulling agents that poll their queues on a timer, 100 ms
        /// by default, which adds up to that much latency to every server-to-client push compared
        /// to the direct delivery of the legacy SMS provider. Polling every 10 ms is a stopgap until
        /// the gateway moves to a push mechanism. Clients that subscribe still need
        /// AddMemoryStreams(StreamNames.PROVIDER_NAME) on their own builder, and the silo needs a
        /// "PubSubStore" grain storage (see <see cref="AddWildCatStorage"/>).
        /// </summary>
        public static ISiloBuilder AddGameStreams(this ISiloBuilder builder)
        {
            builder.AddMemoryStreams(StreamNames.PROVIDER_NAME, streams =>
                streams.ConfigurePullingAgent(options => options.Configure(o =>
                    o.GetQueueMsgsTimerPeriod = TimeSpan.FromMilliseconds(10))));
            return builder;
        }

        /// <summary>
        /// Changes the script event limits (<see cref="ScriptOptions"/>). Optional: without it
        /// the defaults apply (3 second runs, call depth 256, 500 cached scripts per silo, one
        /// script worker thread per processor, new runs of a game refused while more than 2 of its
        /// runs are still going past their time limit).
        /// </summary>
        public static ISiloBuilder ConfigureScriptEvents(this ISiloBuilder builder, Action<ScriptOptions> configure)
        {
            builder.Configure(configure);
            return builder;
        }

        /// <summary>
        /// Turns call chain reentrancy on for every grain call, as Orleans 3.x did by default.
        /// See <see cref="CallChainReentrancyFilter"/>.
        /// </summary>
        public static ISiloBuilder AddLegacyCallChainReentrancy(this ISiloBuilder builder)
        {
            builder.AddOutgoingGrainCallFilter<CallChainReentrancyFilter>();
            return builder;
        }

        /// <summary>
        /// The ADO.NET and memory grain storages write state as JSON with the Orleans Newtonsoft
        /// settings, which leave out properties that hold their type's default value (false, 0,
        /// null). A state class whose constructor sets another value then gets that value back on
        /// the next activation:
        /// a leaderboard created with AcceptBetterScoresOnly = false, or a game mode with
        /// FinishGameWithOneDisconnect = false, came back as true. Writing every value makes the
        /// stored JSON round-trip exactly.
        /// </summary>
        internal static void WriteDefaultValues(OrleansJsonSerializerOptions options)
        {
            options.JsonSerializerSettings.DefaultValueHandling = DefaultValueHandling.Include;
            options.JsonSerializerSettings.NullValueHandling = NullValueHandling.Include;
        }

        private static void Validate(OrleansHostingOptions options)
        {
            ArgumentNullException.ThrowIfNull(options);
            if (!Enum.IsDefined(options.Provider))
                throw new InvalidOperationException(
                    $"{OrleansHostingOptions.SectionName}:Provider has the unknown value {options.Provider}; use Memory or PostgreSQL.");
            if (string.IsNullOrWhiteSpace(options.ClusterId) || string.IsNullOrWhiteSpace(options.ServiceId))
                throw new InvalidOperationException(
                    $"{OrleansHostingOptions.SectionName}:ClusterId and ServiceId must not be empty.");
            if (options.SiloPort is < 1 or > 65535 || options.GatewayPort is < 1 or > 65535 || options.SiloPort == options.GatewayPort)
                throw new InvalidOperationException(
                    $"{OrleansHostingOptions.SectionName}:SiloPort ({options.SiloPort}) and GatewayPort ({options.GatewayPort}) " +
                    "must be two different ports between 1 and 65535.");
        }

        private static string RequireConnectionString(string connectionString)
        {
            if (string.IsNullOrWhiteSpace(connectionString))
                throw new InvalidOperationException(
                    $"{OrleansHostingOptions.SectionName}:Provider is PostgreSQL but the connection string " +
                    $"ConnectionStrings:{OrleansHostingOptions.ConnectionStringName} is empty. Set it with user secrets or " +
                    $"the ConnectionStrings__{OrleansHostingOptions.ConnectionStringName} environment variable, for example " +
                    "\"Host=localhost;Port=5432;Database=wildcat;Username=wildcat;Password=...\".");
            return connectionString;
        }

        /// <summary>
        /// Returns the address other silos should use to reach this one: the configured host
        /// (an IP address, or a name resolved through DNS to its first IPv4 address), or the first
        /// non-loopback IPv4 address of an interface that is up.
        /// </summary>
        private static IPAddress ResolveAdvertisedAddress(string advertisedHost)
        {
            if (!string.IsNullOrWhiteSpace(advertisedHost))
            {
                if (IPAddress.TryParse(advertisedHost, out var parsed))
                    return parsed;
                var resolved = Dns.GetHostAddresses(advertisedHost)
                    .FirstOrDefault(a => a.AddressFamily == AddressFamily.InterNetwork);
                return resolved ?? throw new InvalidOperationException(
                    $"{OrleansHostingOptions.SectionName}:AdvertisedHost '{advertisedHost}' did not resolve to an IPv4 address.");
            }

            var address = NetworkInterface.GetAllNetworkInterfaces()
                .Where(n => n.OperationalStatus == OperationalStatus.Up && n.NetworkInterfaceType != NetworkInterfaceType.Loopback)
                .SelectMany(n => n.GetIPProperties().UnicastAddresses)
                .Select(a => a.Address)
                .FirstOrDefault(a => a.AddressFamily == AddressFamily.InterNetwork && !IPAddress.IsLoopback(a));
            return address ?? throw new InvalidOperationException(
                "No non-loopback IPv4 address was found to advertise to other silos. " +
                $"Set {OrleansHostingOptions.SectionName}:AdvertisedHost.");
        }
    }
}
