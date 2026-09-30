using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Orleans;
using Orleans.Hosting;
using Orleans.TestingHost;
using System;
using Xunit;

namespace Tests
{
    public class ClusterFixture : IDisposable
    {
        public ClusterFixture()
        {
            var builder = new TestClusterBuilder();
            builder.AddSiloBuilderConfigurator<TestSiloConfigurations>();
            builder.AddClientBuilderConfigurator<TestClientConfigurations>();
            this.Cluster = builder.Build();
            this.Cluster.Deploy();
        }

        public void Dispose()
        {
            this.Cluster.StopAllSilos();
        }

        public TestCluster Cluster { get; private set; }
    }

    public class TestSiloConfigurations : ISiloBuilderConfigurator
    {
        public void Configure(ISiloHostBuilder hostBuilder)
        {
            hostBuilder.AddSimpleMessageStreamProvider("SMS");
            hostBuilder.AddMemoryGrainStorage("PubSubStore");
            hostBuilder.AddStartupTask<TestStartupTasks>();
            hostBuilder.AddMemoryGrainStorageAsDefault();
            hostBuilder.AddMemoryGrainStorage("blob");
            hostBuilder.ConfigureServices(services => {
                services.AddSingleton<Interfaces.IBackendStorageServer, Collections.PlayfabManager>();
            });
        }
    }

    public class TestClientConfigurations : IClientBuilderConfigurator
    {
        public void Configure(IConfiguration configuration, IClientBuilder clientBuilder)
        {
            clientBuilder.AddSimpleMessageStreamProvider("SMS");
        }
    }

    [CollectionDefinition(ClusterCollection.Name)]
    public class ClusterCollection : ICollectionFixture<ClusterFixture>
    {
        public const string Name = "ClusterCollection";
    }
}