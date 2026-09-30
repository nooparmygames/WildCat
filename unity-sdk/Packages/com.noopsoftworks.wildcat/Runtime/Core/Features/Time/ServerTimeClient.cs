using System;
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
            DateTimeMessage answer = await Connection.SendRequestAsync<DateTimeMessage>(WildCatMsgType.GetServerTimeRequest,
                new EmptyMessage(), WildCatMsgType.GetServerTimeResponse, false, null, null, false, cancellationToken,
                (arrived, roundTrip) => Record(arrived.value, roundTrip)).ConfigureAwait(false);
            return answer.value;
        }

        /// <summary>
        /// Runs on the network thread as the answer arrives, like the heartbeat's measurement, so
        /// the wait until the game pumps the dispatcher counts neither as network delay nor as
        /// clock difference. The round trip starts when the request went to the socket, so the
        /// wait behind other queued frames does not count either.
        /// </summary>
        private void Record(DateTime serverNow, TimeSpan roundTrip)
        {
            lock (sync)
            {
                offset = serverNow + TimeSpan.FromTicks(roundTrip.Ticks / 2) - DateTime.UtcNow;
                synchronized = true;
            }
        }
    }
}
