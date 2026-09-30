using System;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Orleans;
using Orleans.Configuration;
using Orleans.Hosting;
using Orleans.Providers.MongoDB.Configuration;
using Orleans.Runtime;
using SiloHost;

namespace Frontend
{
    /// <summary>
    /// The service which runs orleans client in the frontend
    /// </summary>
    public class ClusterClientHostedService
    {
        private const string CONFIG_PATH = "devsconfig.json";
        /// <summary>
        /// Gives access to this cluster client
        /// </summary>
        public ClusterClientHostedService()
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

            Client = new ClientBuilder()
                .AddSimpleMessageStreamProvider("SMS")
                .Configure<ClusterOptions>(options => {
                    options.ClusterId = "apadana";
                    options.ServiceId = "apadana";
                })
                .UseMongoDBClient(connectionString)
                .UseMongoDBClustering(options =>
                {
                    options.DatabaseName = dbName;
                    options.Strategy = MongoDBMembershipStrategy.SingleDocument;
                })
                .ConfigureLogging(builder => builder.AddConsole())
                .Build();
#else
            Client = new ClientBuilder()
                .UseLocalhostClustering()
                .AddSimpleMessageStreamProvider("SMS")
                .Configure<ClusterOptions>(options => {
                    options.ClusterId = "apadana";
                    options.ServiceId = "apadana";
                })
                .ConfigureLogging(builder => builder.AddConsole())

                .Build();
#endif
        }

        /// <summary>
        /// Starts cluster client connection
        /// </summary>
        /// <param name="cancellationToken"></param>
        /// <returns></returns>
        public Task StartAsync(CancellationToken cancellationToken)
        {
            var attempt = 0;
            var maxAttempts = 100;
            var delay = TimeSpan.FromSeconds(1);
            Console.WriteLine("************");
            return Client.Connect(async error =>
            {
                if (cancellationToken.IsCancellationRequested)
                {
                    return false;
                }

                if (++attempt < maxAttempts)
                {
                    Console.WriteLine(error.ToString() + ": " +
                        "Failed to connect to Orleans cluster on attempt {@Attempt} of {@MaxAttempts}.",
                        attempt, maxAttempts);

                    try
                    {
                        await Task.Delay(delay, cancellationToken);
                    }
                    catch (OperationCanceledException)
                    {
                        return false;
                    }

                    return true;
                }
                else
                {
                    Console.WriteLine(error.ToString() + ": " +
                        "Failed to connect to Orleans cluster on attempt {@Attempt} of {@MaxAttempts}.",
                        attempt, maxAttempts);

                    return false;
                }
            });
        }

        /// <summary>
        /// Stops the cluster client
        /// </summary>
        /// <param name="cancellationToken"></param>
        /// <returns></returns>
        public async Task StopAsync(CancellationToken cancellationToken)
        {
            try
            {
                await Client.Close();
            }
            catch (OrleansException error)
            {
                Console.WriteLine(error.ToString() + ": " + "Error while gracefully disconnecting from Orleans cluster. Will ignore and continue to shutdown.");
            }
        }

        /// <summary>
        /// The orleans client to use for getting grains and working with them
        /// </summary>
        public IClusterClient Client { get; }
    }
}