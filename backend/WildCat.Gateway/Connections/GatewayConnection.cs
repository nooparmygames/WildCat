using System;
using System.Threading;
using System.Threading.Channels;
using Orleans.Streams;
using WildCat.Interfaces;
using WildCat.Protocol;

namespace WildCat.Gateway
{
    /// <summary>
    /// What the gateway keeps about one open socket: its transport, the login it holds (if any),
    /// its sign-in allowance and the queues the dispatcher uses. Handlers get it from
    /// <see cref="MessageContext.Connection"/> and use it to send frames; the login fields change
    /// only through <see cref="LoginManager"/>.
    /// </summary>
    public sealed class GatewayConnection
    {
        /// <summary>Relayed game messages waiting for the relay worker, in arrival order.</summary>
        internal const int RelayQueueCapacity = 256;

        internal GatewayConnection(WebSocketConnection transport, TokenBucket authAttempts, int maxConcurrentRequests)
        {
            Transport = transport ?? throw new ArgumentNullException(nameof(transport));
            AuthAttempts = authAttempts ?? throw new ArgumentNullException(nameof(authAttempts));
            RequestSlots = new SemaphoreSlim(maxConcurrentRequests, maxConcurrentRequests);
            RelayQueue = Channel.CreateBounded<WildCatMessageBase>(new BoundedChannelOptions(RelayQueueCapacity)
            {
                SingleReader = true,
                SingleWriter = true,
                FullMode = BoundedChannelFullMode.Wait,
            });
        }

        public int Id
        {
            get { return Transport.Id; }
        }

        /// <summary>Cancelled as soon as the socket starts closing.</summary>
        public CancellationToken Closing
        {
            get { return Transport.Closing; }
        }

        /// <summary>The logged in player, or null. Set by <see cref="LoginManager"/>.</summary>
        public IPlayer Player { get; internal set; }

        /// <summary>The logged in player's id, or null.</summary>
        public string PlayerId
        {
            get { return Player?.GetPrimaryKeyString(); }
        }

        public bool IsLoggedIn { get; internal set; }

        /// <summary>The game the login is for; Guid.Empty when nobody is logged in.</summary>
        public Guid GameKey { get; internal set; }

        /// <summary>
        /// Identifies the login this connection holds to the player grain; passed to
        /// IPlayer.Disconnected so a stale disconnect cannot log out a newer login.
        /// </summary>
        public Guid LoginTicket { get; internal set; }

        /// <summary>Queues an encoded frame for the socket; never waits (see <see cref="WebSocketConnection.TrySend"/>).</summary>
        public bool TrySend(byte[] frame)
        {
            return Transport.TrySend(frame);
        }

        /// <summary>Encodes a frame and queues it for the socket; never waits.</summary>
        public bool Send(WildCatMessageBase message)
        {
            return Transport.TrySend(WireCodec.Encode(message));
        }

        internal WebSocketConnection Transport { get; }

        /// <summary>Sign-in attempts left (see <see cref="GameGatewayOptions.AuthAttemptBurst"/>).</summary>
        internal TokenBucket AuthAttempts { get; }

        /// <summary>Caps the requests in flight (see <see cref="GameGatewayOptions.MaxConcurrentRequests"/>).</summary>
        internal SemaphoreSlim RequestSlots { get; }

        internal Channel<WildCatMessageBase> RelayQueue { get; }

        /// <summary>Serializes binding a login to this connection with the disconnect cleanup.</summary>
        internal SemaphoreSlim LoginLock { get; } = new SemaphoreSlim(1, 1);

        /// <summary>When the dispatcher last pinged the player grain's heartbeat, as a TimeProvider timestamp.</summary>
        internal long LastHeartBeatPing;

        internal StreamSubscriptionHandle<PlayerConnectionMessage> ConnectionHandle;
        internal StreamSubscriptionHandle<ChallengeEventData> ChallengesHandle;
        internal StreamSubscriptionHandle<FriendListEventData> FriendsHandle;
    }
}
