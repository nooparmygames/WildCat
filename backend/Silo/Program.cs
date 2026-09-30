using Collections;
using Interfaces;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Orleans;
using Orleans.Configuration;
using Orleans.Hosting;
using System.Net;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.Hosting;
using Newtonsoft.Json;
using Orleans.Providers.MongoDB.Configuration;


namespace SiloHost
{
    class Program
    {
        private static readonly ManualResetEvent _siloStopped = new ManualResetEvent(false);
        private static object syncLock;
        private static bool siloStopping;
        private static ISiloHost silo;
        private const string CONFIG_PATH = "devsconfig.json";

        public static Task Main(string[] args)
        {
#if !DEBUG
            var serverAddress = "localhost";
            var dbName = "Apadana";
            var userName = "admin";
            var password = "";
            var createShardKey = false;   
            if (System.IO.File.Exists(CONFIG_PATH))
            {
                string json = System.IO.File.ReadAllText(CONFIG_PATH);
                var devs = Newtonsoft.Json.JsonConvert.DeserializeObject<DeveloperConfigCollection>(json);
                serverAddress = devs.dbAddress;
                dbName = devs.dbName;
                userName = devs.dbUser;
                password = devs.dbPassword;
            }
            var connectionString = $"mongodb://{userName}:{password}@{serverAddress}";
#endif
            
            return new HostBuilder()
                .UseOrleans((ctx, builder) =>
                {
                    builder
                        .AddSimpleMessageStreamProvider("SMS", x => x.Configure(y => y.FireAndForgetDelivery = true))
                        .Configure<ClusterOptions>(o =>
                        {
                            o.ClusterId = "apadana";
                            o.ServiceId = "apadana";
                        })

                        .AddMemoryGrainStorage("PubSubStore")
                        .AddStartupTask<StartupTasks>()
                        .UseTransactions()
#if DEBUG
                        .UseInMemoryReminderService()
                        .UseLocalhostClustering()
                        .AddMemoryGrainStorageAsDefault()
                        .AddMemoryGrainStorage("blob");

#else
                        .UseMongoDBClient(connectionString)
                        .UseMongoDBClustering(options =>
                        {
                            options.DatabaseName = dbName;
                            options.Strategy = MongoDBMembershipStrategy.SingleDocument;
                        })
                        .UseMongoDBReminders(options =>
                        {
                            options.DatabaseName = dbName;
                            options.CreateShardKeyForCosmos = createShardKey;
                        })
                        .AddMongoDBGrainStorage("Default",
                            options =>
                            {
                                options.DatabaseName = dbName;
                                options.CreateShardKeyForCosmos = createShardKey;

                                options.ConfigureJsonSerializerSettings = settings =>
                                {
                                    settings.NullValueHandling = NullValueHandling.Include;
                                    settings.ObjectCreationHandling = ObjectCreationHandling.Replace;
                                    settings.DefaultValueHandling = DefaultValueHandling.Populate;
                                };
                            })
                        .AddMongoDBGrainStorage("blob",
                            options =>
                            {
                                options.DatabaseName = dbName;
                                options.CreateShardKeyForCosmos = createShardKey;

                                options.ConfigureJsonSerializerSettings = settings =>
                                {
                                    settings.NullValueHandling = NullValueHandling.Include;
                                    settings.ObjectCreationHandling = ObjectCreationHandling.Replace;
                                    settings.DefaultValueHandling = DefaultValueHandling.Populate;
                                };
                            })
                        .ConfigureEndpoints(IPAddress.Loopback, 11111, 30000);
#endif
                }).ConfigureServices(services =>
                {
                    services.AddSingleton<IBackendStorageServer, PlayfabManager>();
                    services.Configure<ConsoleLifetimeOptions>(options => { options.SuppressStatusMessages = true; });
                }).ConfigureLogging(logging =>
                {
#if DEBUG
                    logging.AddConsole();
#else
                    logging.AddConsole(); logging.AddFilter((s1, s2, level) => level >= LogLevel.Warning);
#endif
                }).RunConsoleAsync();
        }
        
    }
}