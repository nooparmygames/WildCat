using WildCat.Interfaces;
using Orleans;
using Orleans.Streams;
using Orleans.TestingHost;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Xunit;

namespace WildCat.Tests
{
    [Collection(ClusterCollection.Name)]
    public class TimeServiceTests
    {
        private InProcessTestCluster cluster;
        public IGrainFactory GrainFactory => this.cluster.Client;
        public TimeServiceTests(ClusterFixture fixture)
        {
            this.cluster = fixture.Cluster;
        }

        [Fact]
        public async Task GetServerTime()
        {
            var ts = GrainFactory.GetGrain<ITimeService>(Guid.Empty);
            var result = await ts.GetServerTime();
            Assert.NotNull(result);
            var dt = DateTime.Parse(result);
            Assert.Equal(dt.Year, DateTime.Now.Year);
        }
    }
}
