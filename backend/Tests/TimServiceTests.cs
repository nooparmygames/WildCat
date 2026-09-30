using Interfaces;
using Orleans;
using Orleans.Streams;
using Orleans.TestingHost;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Tests;
using Xunit;

namespace GrainUnitTests
{
    [Collection(ClusterCollection.Name)]
    public class TimeServiceTests
    {
        private TestCluster cluster;
        public IGrainFactory GrainFactory => this.cluster.GrainFactory;
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
