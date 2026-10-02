using WildCat.Interfaces;
using Orleans;
using Orleans.Streams;
using Orleans.TestingHost;
using System;
using System.Linq;
using System.Threading.Tasks;
using Xunit;

namespace WildCat.Tests
{
    [Collection(ClusterCollection.Name)]
    public class DeveloperManagerTests
    {
        private InProcessTestCluster cluster;
        public IGrainFactory GrainFactory => this.cluster.Client;
        public DeveloperManagerTests(ClusterFixture fixture)
        {
            this.cluster = fixture.Cluster;
        }
        
        [Fact]
        public async Task GetNoneExistingDeveloper()
        {
            var devKey = await GrainFactory.GetGrain<IDeveloperManager>("main").GetDeveloper("s@s.com");
            Assert.Null(devKey);
        }
    }
}
