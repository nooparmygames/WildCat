using System;
using System.Diagnostics;
using System.Threading;
using System.Threading.Tasks;
using WildCat.Protocol;

namespace WildCat
{
    /// <summary>
    /// The server's clock. Needs a connection but no session. <see cref="GetAsync"/> also
    /// estimates how far the local clock is off, so <see cref="Now"/> can give the server time
    /// without asking again.
    /// </summary>
    public sealed class ServerTimeClient : FeatureClient
    {
        private readonly object sync = new object();
        private TimeSpan offset;
        private bool synchronized;

        internal ServerTimeClient(WildCatConnection connection)
            : base(connection)
        {
        }

        /// <summary>The server time estimated from the last <see cref="GetAsync"/>, in UTC; the local UTC time before that.</summary>
        public DateTime Now
        {
            get { return DateTime.UtcNow + Offset; }
        }

        /// <summary>Server time minus local time, measured by the last <see cref="GetAsync"/> (half the round trip is taken off).</summary>
        public TimeSpan Offset
        {
            get
            {
                lock (sync)
                    return offset;
            }
        }

        /// <summary>True once <see cref="GetAsync"/> has succeeded.</summary>
        public bool IsSynchronized
        {
            get
            {
                lock (sync)
                    return synchronized;
            }
        }

        /// <summary>Asks the server for its current time (UTC).</summary>
        public async Task<DateTime> GetAsync(CancellationToken cancellationToken = default(CancellationToken))
        {
            Stopwatch watch = Stopwatch.StartNew();
            DateTimeMessage answer = await Connection.SendRequestAsync<DateTimeMessage>(WildCatMsgType.GetServerTimeRequest,
                new EmptyMessage(), WildCatMsgType.GetServerTimeResponse, false, null, null, false, cancellationToken).ConfigureAwait(false);
            TimeSpan roundTrip = watch.Elapsed;
            DateTime serverNow = answer.value;
            lock (sync)
            {
                offset = serverNow + TimeSpan.FromTicks(roundTrip.Ticks / 2) - DateTime.UtcNow;
                synchronized = true;
            }
            return serverNow;
        }
    }
}
