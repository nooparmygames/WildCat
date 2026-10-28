using System;
using System.Threading;
using System.Threading.Tasks;

namespace WildCat
{
    /// <summary>
    /// The entry point of the SDK: one connection to a WildCat gateway and the feature clients
    /// that use it. Engine free; in Unity, WildCatBehaviour creates one and pumps its dispatcher.
    /// </summary>
    /// <example>
    /// <code>
    /// var client = new WildCatClient(new WildCatClientOptions { ServerUri = new Uri("wss://game.example.com/ws") });
    /// await client.ConnectAsync();
    /// await client.Session.InitializeAsync(developerKey, gameKey);
    /// LoginResult login = await client.Auth.LoginWithDeviceAsync(deviceId);
    /// MatchInfo match = await client.Matchmaking.FindMatchAsync("duel");
    /// </code>
    /// </example>
    public sealed class WildCatClient : IDisposable
    {
        /// <param name="options">Server URL, timeouts and reconnection settings; copied.</param>
        /// <param name="dispatcher">
        /// Where events and Task completions run. Null runs them inline on the network thread (fine
        /// for tools and tests); Unity passes a <see cref="QueuedDispatcher"/> it pumps on the main thread.
        /// </param>
        /// <param name="transportFactory">The socket implementation; null uses System.Net.WebSockets.ClientWebSocket.</param>
        /// <param name="logger">Receives the SDK's log messages; null drops them.</param>
        /// <param name="scheduler">Deadlines and delays; null uses managed timers. WildCatBehaviour supplies a frame-driven scheduler on Web.</param>
        public WildCatClient(WildCatClientOptions options, IMainThreadDispatcher dispatcher = null,
            ITransportFactory transportFactory = null, IWildCatLogger logger = null, IConnectionScheduler scheduler = null)
        {
            Connection = new WildCatConnection(options, dispatcher, transportFactory, logger, scheduler);
            Session = new SessionClient(Connection);
            Auth = new AuthClient(Connection, Session);
            Profile = new ProfileClient(Connection);
            CloudData = new CloudDataClient(Connection);
            Friends = new FriendsClient(Connection);
            Challenges = new ChallengesClient(Connection);
            Matchmaking = new MatchmakingClient(Connection);
            Matches = new MatchesClient(Connection);
            Relay = new RelayClient(Connection);
            Leaderboards = new LeaderboardsClient(Connection);
            Inventory = new InventoryClient(Connection);
            Scripts = new ScriptsClient(Connection);
            ServerTime = new ServerTimeClient(Connection);

            Session.NewSession += Auth.Clear;
            Connection.ResumeHandler = ResumeAsync;
            Connection.ClosedHandler = () =>
            {
                Session.Clear();
                Auth.Clear();
            };
        }

        /// <summary>The connection: state, events, round trip time and the low-level request API.</summary>
        public WildCatConnection Connection { get; }

        /// <summary>Opening the session with the developer and game keys.</summary>
        public SessionClient Session { get; }

        /// <summary>Accounts and logins.</summary>
        public AuthClient Auth { get; }

        /// <summary>Display names.</summary>
        public ProfileClient Profile { get; }

        /// <summary>Private, public and publisher key/value data.</summary>
        public CloudDataClient CloudData { get; }

        /// <summary>Friend list, invitations and friend events.</summary>
        public FriendsClient Friends { get; }

        /// <summary>Direct challenges between players.</summary>
        public ChallengesClient Challenges { get; }

        /// <summary>Finding a match per game mode.</summary>
        public MatchmakingClient Matchmaking { get; }

        /// <summary>Match properties, leaving, rejoining and match events.</summary>
        public MatchesClient Matches { get; }

        /// <summary>Game messages between the players of a match.</summary>
        public RelayClient Relay { get; }

        /// <summary>Reading leaderboards.</summary>
        public LeaderboardsClient Leaderboards { get; }

        /// <summary>Reading, spending and unlocking inventory.</summary>
        public InventoryClient Inventory { get; }

        /// <summary>Running server script events.</summary>
        public ScriptsClient Scripts { get; }

        /// <summary>The server's clock.</summary>
        public ServerTimeClient ServerTime { get; }

        /// <summary>True while the connection is open and not reconnecting.</summary>
        public bool IsConnected
        {
            get { return Connection.IsConnected; }
        }

        /// <summary>True while a player is logged in.</summary>
        public bool IsLoggedIn
        {
            get { return Auth.IsLoggedIn; }
        }

        /// <summary>See <see cref="WildCatConnection.Connected"/>.</summary>
        public event Action Connected
        {
            add { Connection.Connected += value; }
            remove { Connection.Connected -= value; }
        }

        /// <summary>See <see cref="WildCatConnection.Disconnected"/>.</summary>
        public event Action<DisconnectedEventArgs> Disconnected
        {
            add { Connection.Disconnected += value; }
            remove { Connection.Disconnected -= value; }
        }

        /// <summary>See <see cref="WildCatConnection.Reconnecting"/>.</summary>
        public event Action<ReconnectingEventArgs> Reconnecting
        {
            add { Connection.Reconnecting += value; }
            remove { Connection.Reconnecting -= value; }
        }

        /// <summary>See <see cref="WildCatConnection.Reconnected"/>.</summary>
        public event Action Reconnected
        {
            add { Connection.Reconnected += value; }
            remove { Connection.Reconnected -= value; }
        }

        /// <summary>Opens the connection. Then initialize the session and log in.</summary>
        public Task ConnectAsync(CancellationToken cancellationToken = default(CancellationToken))
        {
            return Connection.ConnectAsync(cancellationToken);
        }

        /// <summary>Closes the connection; the session and login end with it.</summary>
        public Task DisconnectAsync()
        {
            return Connection.DisconnectAsync();
        }

        /// <summary>
        /// Drops the connection without raising events and forgets the session: the keys, the
        /// ticket, the player and the stored credentials (the password of a user name or email
        /// login included). Waiting requests fail with <see cref="WildCatDisconnectedException"/>
        /// through the dispatcher.
        /// </summary>
        public void Dispose()
        {
            Connection.Dispose();
            // After the connection is disposed no answer can store anything again (see
            // WildCatConnection.SendRequestAsync), so this clears for good.
            Session.Clear();
            Auth.Clear();
        }

        /// <summary>
        /// Runs on a network thread after a reconnection: opens a new session with the same keys
        /// and logs in again with the last credentials, when <see cref="WildCatClientOptions.ResumeSession"/> is on.
        /// </summary>
        private async Task ResumeAsync(CancellationToken cancellationToken)
        {
            Session.ForgetTicket();
            Auth.ForgetPlayer();
            if (!Connection.Options.ResumeSession)
            {
                Session.Clear();
                Auth.Clear();
                return;
            }

            Guid developerKey;
            Guid gameKey;
            if (!Session.TryGetKeys(out developerKey, out gameKey))
                return;
            await Session.InitializeCoreAsync(developerKey, gameKey, true, cancellationToken).ConfigureAwait(Connection.CaptureContext);

            StoredLogin login = Auth.LastLogin;
            if (login != null)
                await Auth.LoginAsync(login, true, cancellationToken).ConfigureAwait(Connection.CaptureContext);
        }
    }
}
