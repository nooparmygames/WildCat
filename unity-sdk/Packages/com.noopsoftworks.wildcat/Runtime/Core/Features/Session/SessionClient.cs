using System;
using System.Threading;
using System.Threading.Tasks;
using WildCat.Protocol;

namespace WildCat
{
    /// <summary>
    /// The session: a developer key plus one of that developer's game keys, answered with a
    /// ticket that every later request carries. Initialize it after connecting and before logging in.
    /// </summary>
    public sealed class SessionClient : FeatureClient
    {
        private readonly object sync = new object();
        private Guid developerKey;
        private Guid gameKey;
        private Guid ticket;

        internal SessionClient(WildCatConnection connection)
            : base(connection)
        {
        }

        /// <summary>
        /// Raised on the network thread when the answer to <see cref="InitializeAsync"/> arrives,
        /// under the connection's lock: a new session has no login. Handlers must be quick.
        /// </summary>
        internal event Action NewSession;

        /// <summary>The session ticket, or Guid.Empty before <see cref="InitializeAsync"/> succeeded.</summary>
        public Guid Ticket
        {
            get
            {
                lock (sync)
                    return ticket;
            }
        }

        /// <summary>True once a session is open on the current connection.</summary>
        public bool IsInitialized
        {
            get { return Ticket != Guid.Empty; }
        }

        /// <summary>The developer key of the session, or Guid.Empty.</summary>
        public Guid DeveloperKey
        {
            get
            {
                lock (sync)
                    return developerKey;
            }
        }

        /// <summary>The game key of the session, or Guid.Empty.</summary>
        public Guid GameKey
        {
            get
            {
                lock (sync)
                    return gameKey;
            }
        }

        /// <summary>
        /// Opens a session for the game (InitializeSession, message 6) and returns its ticket.
        /// Fails with error code 1 when the developer does not exist or the game is not theirs.
        /// Calling it again opens a new session, which logs the player out.
        /// </summary>
        public Task<Guid> InitializeAsync(Guid developerKey, Guid gameKey, CancellationToken cancellationToken = default(CancellationToken))
        {
            return InitializeCoreAsync(developerKey, gameKey, false, cancellationToken);
        }

        /// <summary>
        /// Opens a session. <c>isResume</c> is true for the resume after a reconnection, an
        /// internal request that keeps the stored login so it can be repeated.
        /// </summary>
        internal async Task<Guid> InitializeCoreAsync(Guid developer, Guid game, bool isResume, CancellationToken cancellationToken)
        {
            if (developer == Guid.Empty || game == Guid.Empty)
                throw new ArgumentException("The developer key and the game key must both be set.");
            GuidMessage answer = await Connection.SendRequestAsync<GuidMessage>(WildCatMsgType.InitializeSessionRequest,
                new InitializeSessionRequest { DeveloperKey = developer, GameKey = game },
                WildCatMsgType.InitializeSessionResponse, false, null, null, isResume, cancellationToken,
                (opened, roundTrip) => Apply(developer, game, opened.value, isResume)).ConfigureAwait(false);
            return answer.value;
        }

        /// <summary>
        /// Stores the new session. Runs on the network thread as the answer arrives (see
        /// WildCatConnection.SendRequestAsync), not when the game pumps the completion, so a late
        /// pump cannot bring back a session that a reconnection or a disconnect replaced.
        /// </summary>
        private void Apply(Guid developer, Guid game, Guid newTicket, bool isResume)
        {
            lock (sync)
            {
                developerKey = developer;
                gameKey = game;
                ticket = newTicket;
            }
            Connection.SetSession(newTicket.ToString(), false);
            if (!isResume)
            {
                Action handler = NewSession;
                if (handler != null)
                    handler();
            }
        }

        /// <summary>
        /// Takes the ticket a login answered with, if the session is still the one the login was
        /// sent for. False when InitializeAsync opened another session meanwhile.
        /// </summary>
        internal bool TryReplaceTicket(Guid loggedInTicket, Guid newTicket)
        {
            lock (sync)
            {
                if (ticket != loggedInTicket)
                    return false;
                ticket = newTicket;
                return true;
            }
        }

        internal bool TryGetKeys(out Guid developer, out Guid game)
        {
            lock (sync)
            {
                developer = developerKey;
                game = gameKey;
                return developer != Guid.Empty && game != Guid.Empty;
            }
        }

        /// <summary>The socket dropped: the ticket is gone but the keys stay for resuming.</summary>
        internal void ForgetTicket()
        {
            lock (sync)
                ticket = Guid.Empty;
        }

        internal void Clear()
        {
            lock (sync)
            {
                ticket = Guid.Empty;
                developerKey = Guid.Empty;
                gameKey = Guid.Empty;
            }
        }
    }
}
