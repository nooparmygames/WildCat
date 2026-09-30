using Collections;
using Interfaces;
using Orleans.TestingHost;
using Xunit;

namespace Tests
{
    /// <summary>
    /// One in-process silo plus client shared by every test class in <see cref="ClusterCollection"/>.
    /// Mirrors the silo configuration in Silo/Program.cs with memory providers.
    /// </summary>
    public class ClusterFixture : IAsyncLifetime
    {
        public InProcessTestCluster Cluster { get; private set; }

        public async Task InitializeAsync()
        {
            var builder = new InProcessTestClusterBuilder(initialSilosCount: 1);
            builder.ConfigureSilo((options, silo) =>
            {
                silo.AddMemoryGrainStorageAsDefault();
                silo.AddMemoryGrainStorage("blob");
                silo.AddMemoryGrainStorage("PubSubStore");
                silo.AddGameStreams();
                silo.UseInMemoryReminderService();
                silo.AddLegacyCallChainReentrancy();
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
