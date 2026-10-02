using System;

namespace WildCat.Gateway
{
    /// <summary>
    /// A thread-safe token bucket: it starts full with <c>capacity</c> tokens, each allowed
    /// attempt takes one, and one token comes back every <c>refillInterval</c> up to the capacity.
    /// The gateway keeps one per connection and one per client address (<see cref="AddressAttemptLimiter"/>)
    /// to limit sign-in attempts.
    /// </summary>
    internal sealed class TokenBucket
    {
        private readonly object gate = new object();
        private readonly int capacity;
        private readonly TimeSpan refillInterval;
        private readonly TimeProvider timeProvider;
        private double tokens;
        private long lastRefill;

        public TokenBucket(int capacity, TimeSpan refillInterval, TimeProvider timeProvider)
        {
            if (capacity < 1)
                throw new ArgumentOutOfRangeException(nameof(capacity), "The bucket must hold at least one token.");
            if (refillInterval <= TimeSpan.Zero)
                throw new ArgumentOutOfRangeException(nameof(refillInterval), "The refill interval must be positive.");
            this.capacity = capacity;
            this.refillInterval = refillInterval;
            this.timeProvider = timeProvider ?? throw new ArgumentNullException(nameof(timeProvider));
            tokens = capacity;
            lastRefill = timeProvider.GetTimestamp();
        }

        /// <summary>
        /// Takes a token and returns true, or returns false when the bucket is empty.
        /// </summary>
        public bool TryTake()
        {
            lock (gate)
            {
                Refill();
                if (tokens < 1)
                    return false;
                tokens -= 1;
                return true;
            }
        }

        /// <summary>True when the bucket has refilled to its capacity, as if nothing had been taken.</summary>
        public bool IsFull()
        {
            lock (gate)
            {
                Refill();
                return tokens >= capacity;
            }
        }

        private void Refill()
        {
            long now = timeProvider.GetTimestamp();
            TimeSpan elapsed = timeProvider.GetElapsedTime(lastRefill, now);
            lastRefill = now;
            tokens = Math.Min(capacity, tokens + elapsed / refillInterval);
        }
    }
}
