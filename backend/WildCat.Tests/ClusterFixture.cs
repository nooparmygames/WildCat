using Microsoft.Extensions.DependencyInjection;
using WildCat.Grains;
using WildCat.Interfaces;
using Orleans.TestingHost;
using Xunit;

namespace WildCat.Tests
{
    /// <summary>
    /// One in-process silo plus client shared by every test class in <see cref="ClusterCollection"/>.
    /// Mirrors the silo configuration in WildCat.Host/Program.cs with the Memory provider, except
    /// that the test cluster brings its own membership, emails go to <see cref="Emails"/>, and
    /// <see cref="FailingPushFilter"/> can make pushes to chosen players fail.
    /// </summary>
    public class ClusterFixture : IAsyncLifetime
    {
        public InProcessTestCluster Cluster { get; private set; }

        /// <summary>Every email the grains sent through <see cref="IEmailSender"/>.</summary>
        public CapturingEmailSender Emails { get; } = new CapturingEmailSender();

        public async Task InitializeAsync()
        {
            var builder = new InProcessTestClusterBuilder(initialSilosCount: 1);
            builder.ConfigureSilo((options, silo) =>
            {
                silo.AddWildCatStorage(new OrleansHostingOptions { Provider = OrleansProvider.Memory }, connectionString: null);
                silo.AddGameStreams();
                silo.AddLegacyCallChainReentrancy();
                silo.AddIncomingGrainCallFilter<FailingPushFilter>();
                silo.Services.AddSingleton<IEmailSender>(Emails);
            });
            builder.ConfigureClient(client => client.AddMemoryStreams(StreamNames.PROVIDER_NAME));
            Cluster = builder.Build();
            await Cluster.DeployAsync();
        }

        public async Task DisposeAsync()
        {
            if (Cluster != null)
            {
                await Cluster.DisposeAsync();
            }
        }
    }

    [CollectionDefinition(ClusterCollection.Name)]
    public class ClusterCollection : ICollectionFixture<ClusterFixture>
    {
        public const string Name = "ClusterCollection";
    }
}
