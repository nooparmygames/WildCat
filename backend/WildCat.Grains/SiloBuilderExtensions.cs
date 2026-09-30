using WildCat.Interfaces;
using Orleans.Hosting;

namespace WildCat.Grains
{
    /// <summary>
    /// Silo configuration shared by the silo host (WildCat.Host/Program.cs) and the test cluster
    /// (WildCat.Tests/ClusterFixture.cs), so the two stay in sync.
    /// </summary>
    public static class SiloBuilderExtensions
    {
        /// <summary>
        /// Memory streams for the game provider (<see cref="StreamNames.PROVIDER_NAME"/>).
        /// Memory streams deliver through pulling agents that poll their queues on a timer, 100 ms
        /// by default, which adds up to that much latency to every server-to-client push compared
        /// to the direct delivery of the legacy SMS provider. Polling every 10 ms is a stopgap until
        /// the gateway moves to a push mechanism. Clients that subscribe still need
        /// AddMemoryStreams(StreamNames.PROVIDER_NAME) on their own builder, and the silo needs a
        /// "PubSubStore" grain storage.
        /// </summary>
        public static ISiloBuilder AddGameStreams(this ISiloBuilder builder)
        {
            builder.AddMemoryStreams(StreamNames.PROVIDER_NAME, streams =>
                streams.ConfigurePullingAgent(options => options.Configure(o =>
                    o.GetQueueMsgsTimerPeriod = TimeSpan.FromMilliseconds(10))));
            return builder;
        }

        /// <summary>
        /// Turns call chain reentrancy on for every grain call, as Orleans 3.x did by default.
        /// See <see cref="CallChainReentrancyFilter"/>.
        /// </summary>
        public static ISiloBuilder AddLegacyCallChainReentrancy(this ISiloBuilder builder)
        {
            builder.AddOutgoingGrainCallFilter<CallChainReentrancyFilter>();
            return builder;
        }
    }
}
