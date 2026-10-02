using System.Net;
using System.Net.Sockets;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Orleans;
using Orleans.Runtime;
using WildCat.Grains;
using WildCat.Interfaces;
using Xunit;

namespace WildCat.Tests
{
    /// <summary>
    /// Runs a real silo host the way WildCat.Host/Program.cs does (UseOrleans plus
    /// <see cref="SiloBuilderExtensions.AddWildCatProviders"/>) against PostgreSQL, so ADO.NET
    /// clustering is covered too, and restarts it to show that state survives the process.
    /// </summary>
    public class PostgreSqlHostTests
    {
        [PostgreSqlFact]
        public async Task SiloJoinsThroughPostgreSqlAndStateSurvivesARestart()
        {
            var options = new OrleansHostingOptions
            {
                Provider = OrleansProvider.PostgreSQL,
                ClusterId = $"wildcat-test-{Guid.NewGuid():N}",
                ServiceId = $"wildcat-test-{Guid.NewGuid():N}",
                SiloPort = FreePort(),
                GatewayPort = FreePort(),
                AdvertisedHost = "127.0.0.1",
            };
            string id = TestSetup.UniqueId("pg-player");

            using (var host = await StartHost(options))
            {
                var grains = host.Services.GetRequiredService<IGrainFactory>();
                var hosts = await grains.GetGrain<IManagementGrain>(0).GetHosts(onlyActive: true).WaitAsync(TestSetup.Timeout);
                Assert.Single(hosts);
                Assert.Equal(1, await CountActiveSilos(options.ClusterId));

                await grains.GetGrain<IPlayer>(id).CreateAccountWithDevice(id).WaitAsync(TestSetup.Timeout);
                await host.StopAsync().WaitAsync(TestSetup.Timeout);
            }

            // The stopped silo left the cluster, and a new one with the same ServiceId finds the state.
            Assert.Equal(0, await CountActiveSilos(options.ClusterId));
            using (var host = await StartHost(options))
            {
                var grains = host.Services.GetRequiredService<IGrainFactory>();
                Assert.Equal(1, await CountActiveSilos(options.ClusterId));
                var state = await grains.GetGrain<IPlayer>(id).GetState().WaitAsync(TestSetup.Timeout);
                Assert.True(state.IsCreated);
                await host.StopAsync().WaitAsync(TestSetup.Timeout);
            }
        }

        private static async Task<IHost> StartHost(OrleansHostingOptions options)
        {
            var builder = Host.CreateApplicationBuilder();
            builder.Logging.ClearProviders();
            builder.UseOrleans(silo =>
            {
                silo.AddWildCatProviders(options, PostgreSqlFactAttribute.ConnectionString);
                silo.AddGameStreams();
                silo.AddLegacyCallChainReentrancy();
            });
            builder.Services.AddSingleton<IEmailSender>(new CapturingEmailSender());
            var host = builder.Build();
            await host.StartAsync().WaitAsync(TimeSpan.FromMinutes(1));
            return host;
        }

        private static Task<long> CountActiveSilos(string clusterId)
        {
            return PostgreSqlFactAttribute.CountAsync(
                "SELECT count(*) FROM orleansmembershiptable WHERE deploymentid = @cluster AND status = @status",
                ("cluster", clusterId), ("status", (int)SiloStatus.Active));
        }

        private static int FreePort()
        {
            var listener = new TcpListener(IPAddress.Loopback, 0);
            listener.Start();
            int port = ((IPEndPoint)listener.LocalEndpoint).Port;
            listener.Stop();
            return port;
        }
    }
}
