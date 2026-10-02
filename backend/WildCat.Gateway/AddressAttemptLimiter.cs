using System;
using System.Collections.Concurrent;
using System.Net;
using System.Net.Sockets;
using System.Threading;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace WildCat.Gateway
{
    /// <summary>
    /// Sign-in attempts per client address, shared by every connection of this gateway node: a
    /// token bucket per address of <see cref="GameGatewayOptions.AuthAttemptsPerAddressBurst"/>
    /// attempts that refills at <see cref="GameGatewayOptions.AuthAttemptsPerAddressPerMinute"/>. It closes
    /// the gap the per-connection allowance leaves open, where every new socket starts with a full
    /// allowance (and every attempt costs a password hash).
    /// </summary>
    /// <remarks>
    /// The address is the one the host sees after the forwarded headers middleware, so behind a
    /// reverse proxy the proxy has to be listed in ForwardedHeaders:KnownProxies or
    /// ForwardedHeaders:KnownNetworks; otherwise every client shares the proxy's allowance.
    /// IPv6 addresses count per /64 network, since one client usually holds a whole /64. Loopback
    /// addresses are not limited: they are local tools, tests, or a proxy on the same machine that
    /// does not forward the client address, where one shared allowance would lock everyone out.
    /// Buckets that have refilled are dropped once a minute; past
    /// <see cref="MaxTrackedAddresses"/> addresses, new ones are let through (with a warning)
    /// rather than tracked.
    /// </remarks>
    public sealed class AddressAttemptLimiter
    {
        /// <summary>The most addresses tracked at once.</summary>
        internal const int MaxTrackedAddresses = 100_000;

        private static readonly TimeSpan SweepInterval = TimeSpan.FromMinutes(1);

        private readonly ConcurrentDictionary<string, TokenBucket> buckets = new ConcurrentDictionary<string, TokenBucket>(StringComparer.Ordinal);
        private readonly int burst;
        private readonly TimeSpan refillInterval;
        private readonly TimeProvider timeProvider;
        private readonly ILogger<AddressAttemptLimiter> logger;
        private readonly object sweepGate = new object();
        private long lastSweep;
        private int warnedFull;

        public AddressAttemptLimiter(IOptions<GameGatewayOptions> options, TimeProvider timeProvider, ILogger<AddressAttemptLimiter> logger)
        {
            var value = options.Value;
            burst = value.AuthAttemptsPerAddressBurst;
            refillInterval = TimeSpan.FromMinutes(1) / value.AuthAttemptsPerAddressPerMinute;
            this.timeProvider = timeProvider;
            this.logger = logger;
            lastSweep = timeProvider.GetTimestamp();
        }

        /// <summary>Addresses with a bucket right now.</summary>
        internal int TrackedAddresses
        {
            get { return buckets.Count; }
        }

        /// <summary>
        /// Takes an attempt from the allowance of <paramref name="address"/> and returns true, or
        /// returns false when it is used up. Always true when the limit is off (a burst of 0), and
        /// for a missing or loopback address.
        /// </summary>
        public bool TryTake(IPAddress address)
        {
            if (burst <= 0 || address == null || IPAddress.IsLoopback(address))
                return true;
            string key = KeyOf(address);
            if (!buckets.TryGetValue(key, out TokenBucket bucket))
            {
                SweepIfDue();
                if (buckets.Count >= MaxTrackedAddresses)
                {
                    if (Interlocked.Exchange(ref warnedFull, 1) == 0)
                    {
                        logger.LogWarning("The sign-in limit tracks {Count} client addresses, its maximum; attempts from new addresses are not limited per address until old ones expire",
                            MaxTrackedAddresses);
                    }
                    return true;
                }
                bucket = buckets.GetOrAdd(key, _ => new TokenBucket(burst, refillInterval, timeProvider));
            }
            return bucket.TryTake();
        }

        /// <summary>
        /// The key an address counts under: IPv4 addresses (also when mapped into IPv6) as they
        /// are, IPv6 addresses by their /64 network.
        /// </summary>
        internal static string KeyOf(IPAddress address)
        {
            if (address.IsIPv4MappedToIPv6)
                address = address.MapToIPv4();
            if (address.AddressFamily != AddressFamily.InterNetworkV6)
                return address.ToString();
            byte[] bytes = address.GetAddressBytes();
            Array.Clear(bytes, 8, 8);
            return new IPAddress(bytes) + "/64";
        }

        /// <summary>Drops the buckets that have refilled, at most once a <see cref="SweepInterval"/>.</summary>
        private void SweepIfDue()
        {
            long now = timeProvider.GetTimestamp();
            lock (sweepGate)
            {
                if (timeProvider.GetElapsedTime(lastSweep, now) < SweepInterval)
                    return;
                lastSweep = now;
            }
            foreach (var entry in buckets)
            {
                if (entry.Value.IsFull())
                    buckets.TryRemove(entry);
            }
            if (buckets.Count < MaxTrackedAddresses)
                Interlocked.Exchange(ref warnedFull, 0);
        }
    }
}
