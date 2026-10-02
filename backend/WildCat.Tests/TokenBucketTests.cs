using WildCat.Gateway;
using Xunit;

namespace WildCat.Tests
{
    /// <summary>
    /// The token bucket behind the gateway's per-connection sign-in limit.
    /// </summary>
    public class TokenBucketTests
    {
        private static readonly TimeSpan TenSeconds = TimeSpan.FromSeconds(10);

        [Fact]
        public void FullBucketAllowsTheBurstThenRefuses()
        {
            var bucket = new TokenBucket(5, TenSeconds, new ManualTimeProvider());

            for (int i = 0; i < 5; ++i)
                Assert.True(bucket.TryTake(), $"attempt {i + 1}");
            Assert.False(bucket.TryTake());
            Assert.False(bucket.TryTake());
        }

        [Fact]
        public void OneTokenComesBackPerInterval()
        {
            var time = new ManualTimeProvider();
            var bucket = new TokenBucket(5, TenSeconds, time);
            for (int i = 0; i < 5; ++i)
                bucket.TryTake();

            time.Advance(TimeSpan.FromSeconds(9.9));
            Assert.False(bucket.TryTake());
            time.Advance(TimeSpan.FromSeconds(0.1));
            Assert.True(bucket.TryTake());
            Assert.False(bucket.TryTake());

            // Time spent refused still counts towards the next token.
            time.Advance(TimeSpan.FromSeconds(5));
            Assert.False(bucket.TryTake());
            time.Advance(TimeSpan.FromSeconds(5));
            Assert.True(bucket.TryTake());
        }

        [Fact]
        public void RefillStopsAtTheCapacity()
        {
            var time = new ManualTimeProvider();
            var bucket = new TokenBucket(5, TenSeconds, time);
            for (int i = 0; i < 5; ++i)
                bucket.TryTake();

            time.Advance(TimeSpan.FromHours(1));

            for (int i = 0; i < 5; ++i)
                Assert.True(bucket.TryTake(), $"attempt {i + 1}");
            Assert.False(bucket.TryTake());
        }

        [Fact]
        public void ConcurrentTakersNeverGetMoreThanTheCapacity()
        {
            var bucket = new TokenBucket(10, TenSeconds, new ManualTimeProvider());
            int granted = 0;

            Parallel.For(0, 200, _ =>
            {
                if (bucket.TryTake())
                    Interlocked.Increment(ref granted);
            });

            Assert.Equal(10, granted);
        }

        [Fact]
        public void InvalidSettingsAreRejected()
        {
            var time = new ManualTimeProvider();
            Assert.Throws<ArgumentOutOfRangeException>(() => new TokenBucket(0, TenSeconds, time));
            Assert.Throws<ArgumentOutOfRangeException>(() => new TokenBucket(5, TimeSpan.Zero, time));
            Assert.Throws<ArgumentNullException>(() => new TokenBucket(5, TenSeconds, null));
        }
    }
}
