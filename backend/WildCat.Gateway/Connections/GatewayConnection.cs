using System;
using System.Net;
using System.Threading;
using Orleans.Streams;
using WildCat.Interfaces;
using WildCat.Protocol;

namespace WildCat.Gateway
{
    /// <summary>
    /// What the gateway keeps about one open socket: its transport, the login it holds (if any),
    /// its sign-in allowance and the queues the dispatcher uses. Handlers get it from
    /// <see cref="MessageContext.Connection"/> and use it to send frames; the login changes only
    /// through <see cref="LoginManager"/>, as a whole (see <see cref="Login"/>).
    /// </summary>
    public sealed class GatewayConnection
    {
        /// <summary>Relayed game messages waiting for the relay worker, in arrival order.</summary>
        internal const int RelayQueueCapacity = 256;

        private volatile LoginState login;

        internal GatewayConnection(WebSocketConnection transport, TokenBucket authAttempts, int maxConcurrentRequests,
            long relayQueueBytes, IPAddress remoteAddress = null)
        {
            Transport = transport ?? throw new ArgumentNullException(nameof(transport));
            AuthAttempts = authAttempts ?? throw new ArgumentNullException(nameof(authAttempts));
            RequestSlots = new SemaphoreSlim(maxConcurrentRequests, maxConcurrentRequests);
            RelayQueue = new RelayQueue(RelayQueueCapacity, relayQueueBytes);
            RemoteAddress = remoteAddress;
        }

        /// <summary>
        /// What a login binds to a connection. Replaced as a whole, never changed in place, so code
        /// on other threads (stream callbacks pushing events) reads one consistent login: never the
        /// player of one login with the game of another, nor half of a Guid.
        /// </summary>
        public sealed class LoginState
        {
            internal LoginState(IPlayer player, Guid gameKey, Guid loginTicket)
            {
                Player = player ?? throw new ArgumentNullException(nameof(player));
                GameKey = gameKey;
                LoginTicket = loginTicket;
            }

            public IPlayer Player { get; }

            /// <summary>The game the login is for.</summary>
            public Guid GameKey { get; }

            /// <summary>
            /// Identifies the login to the player grain; passed to IPlayer.Disconnected so a stale
            /// disconnect cannot log out a newer login.
            /// </summary>
            public Guid LoginTicket { get; }
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

        /// <summary>
        /// The client's address as the host sees it (the forwarded address behind a trusted proxy),
        /// or null when it is not known. Sign-in attempts are limited per address too.
        /// </summary>
        public IPAddress RemoteAddress { get; }

        /// <summary>
        /// The login this connection holds, or null. Read it once and use its fields when more than
        /// one of them matters; the properties below each read it again.
        /// </summary>
        public LoginState Login
        {
            get { return login; }
        }

        /// <summary>The logged in player, or null.</summary>
        public IPlayer Player
        {
            get { return login?.Player; }
        }

        /// <summary>The logged in player's id, or null.</summary>
        public string PlayerId
        {
            get { return login?.Player.GetPrimaryKeyString(); }
        }

        public bool IsLoggedIn
        {
            get { return login != null; }
        }

        /// <summary>The game the login is for; Guid.Empty when nobody is logged in.</summary>
        public Guid GameKey
        {
            get { return login?.GameKey ?? Guid.Empty; }
        }

        /// <summary>The ticket of the login (see <see cref="LoginState.LoginTicket"/>); Guid.Empty when nobody is logged in.</summary>
        public Guid LoginTicket
        {
            get { return login?.LoginTicket ?? Guid.Empty; }
        }

        /// <summary>Binds a login; called by <see cref="LoginManager"/> with the login lock held.</summary>
        internal void SetLogin(IPlayer player, Guid gameKey, Guid loginTicket)
        {
            login = new LoginState(player, gameKey, loginTicket);
        }

        /// <summary>Drops the login; called by <see cref="LoginManager"/> with the login lock held.</summary>
        internal void ClearLogin()
        {
            login = null;
        }

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

        internal RelayQueue RelayQueue { get; }

        /// <summary>Serializes binding a login to this connection with the disconnect cleanup.</summary>
        internal SemaphoreSlim LoginLock { get; } = new SemaphoreSlim(1, 1);

        /// <summary>When the dispatcher last pinged the player grain's heartbeat, as a TimeProvider timestamp.</summary>
        internal long LastHeartBeatPing;

        internal StreamSubscriptionHandle<PlayerConnectionMessage> ConnectionHandle;
        internal StreamSubscriptionHandle<ChallengeEventData> ChallengesHandle;
        internal StreamSubscriptionHandle<FriendListEventData> FriendsHandle;
    }
}
