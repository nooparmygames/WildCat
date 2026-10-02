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
            var before = DateTime.UtcNow;
            var result = await ts.GetServerTime();
            var after = DateTime.UtcNow;
            Assert.Equal(DateTimeKind.Utc, result.Kind);
            Assert.InRange(result, before, after);
        }
    }
}
