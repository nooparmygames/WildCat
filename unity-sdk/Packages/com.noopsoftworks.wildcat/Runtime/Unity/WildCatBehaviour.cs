using System;
using System.Threading;
using System.Threading.Tasks;
using UnityEngine;

namespace WildCat.Unity
{
    /// <summary>
    /// Owns a <see cref="WildCatClient"/> for a scene: creates it from a
    /// <see cref="WildCatSettings"/> asset, runs its callbacks on the main thread by pumping a
    /// <see cref="QueuedDispatcher"/> in Update, and drops the connection when destroyed. It is not
    /// a singleton: reference the component where you need it (or keep one on a DontDestroyOnLoad
    /// object and find it once). Picks the browser WebSocket transport in WebGL players.
    /// </summary>
    /// <example>
    /// <code>
    /// [SerializeField] WildCatBehaviour wildCat;
    /// async void Start()
    /// {
    ///     await wildCat.ConnectAndInitializeAsync();
    ///     LoginResult login = await wildCat.Client.Auth.LoginWithThisDeviceAsync();
    /// }
    /// </code>
    /// </example>
    [DisallowMultipleComponent]
    [AddComponentMenu("WildCat/WildCat Client")]
    public sealed class WildCatBehaviour : MonoBehaviour
    {
        [Tooltip("Server URL, keys, timeouts and reconnection settings.")]
        [SerializeField]
        private WildCatSettings settings;

        [Tooltip("Keep this object (and the connection) when another scene loads.")]
        [SerializeField]
        private bool dontDestroyOnLoad = true;

        [Tooltip("Set Application.runInBackground to true in Awake, so the game, and with it the SDK's events and Task completions, keeps running while the window is not focused. Off by default because it changes the whole application; outside WebGL the heartbeat runs on a background thread either way.")]
        [SerializeField]
        private bool runInBackground = false;

        [Tooltip("Connect and initialize the session in Start. Errors go to the console; call ConnectAndInitializeAsync yourself to handle them.")]
        [SerializeField]
        private bool connectOnStart = false;

        [Tooltip("The most SDK callbacks run per frame; 0 runs all that are waiting.")]
        [Min(0)]
        [SerializeField]
        private int maxCallbacksPerFrame = 0;

        private readonly QueuedDispatcher dispatcher = new QueuedDispatcher();
        private WildCatClient client;
        private PumpedConnectionScheduler webScheduler;

        /// <summary>The settings the client is created from. Set it before the first use of <see cref="Client"/>.</summary>
        public WildCatSettings Settings
        {
            get { return settings; }
            set
            {
                if (client != null)
                    throw new InvalidOperationException("The WildCat client exists already; set the settings before using it.");
                settings = value;
            }
        }

        /// <summary>The client, created from <see cref="Settings"/> on first use.</summary>
        public WildCatClient Client
        {
            get
            {
                EnsureClient();
                return client;
            }
        }

        /// <summary>The dispatcher this component pumps; SDK callbacks wait in it until the next Update.</summary>
        public QueuedDispatcher Dispatcher
        {
            get { return dispatcher; }
        }

        /// <summary>
        /// Connects (unless connected) and opens a session with the keys from the settings. Throws
        /// InvalidOperationException when the keys are missing, WildCatDisconnectedException when the
        /// server cannot be reached and WildCatException when it refuses the keys.
        /// </summary>
        public async Task ConnectAndInitializeAsync(CancellationToken cancellationToken = default(CancellationToken))
        {
            EnsureClient();
            Guid developerKey;
            Guid gameKey;
            if (!settings.TryGetKeys(out developerKey, out gameKey))
                throw new InvalidOperationException("Set the developer key and the game key (GUIDs) in " + settings.name + ".");
            if (client.Connection.State == ConnectionState.Disconnected)
                await client.ConnectAsync(cancellationToken);
            await client.Session.InitializeAsync(developerKey, gameKey, cancellationToken);
        }

        private void Awake()
        {
            if (dontDestroyOnLoad)
                DontDestroyOnLoad(gameObject);
            if (runInBackground)
                Application.runInBackground = true;
            if (settings != null)
                EnsureClient();
        }

        private async void Start()
        {
            if (!connectOnStart)
                return;
            try
            {
                await ConnectAndInitializeAsync();
            }
            catch (Exception ex)
            {
                Debug.LogException(ex, this);
            }
        }

        private void Update()
        {
            webScheduler?.Pump();
            dispatcher.Pump(maxCallbacksPerFrame > 0 ? maxCallbacksPerFrame : int.MaxValue, OnCallbackError);
        }

        private void OnDestroy()
        {
            if (client == null)
                return;
            client.Dispose();
            client = null;
            // Dispose fails the waiting requests through the dispatcher; nobody pumps it after
            // this, so run those completions now and code awaiting them sees the failure.
            dispatcher.Pump(int.MaxValue, OnCallbackError);
        }

        private void OnApplicationQuit()
        {
            OnDestroy();
        }

        private void OnCallbackError(Exception exception)
        {
            Debug.LogException(exception, this);
        }

        private void EnsureClient()
        {
            if (client != null)
                return;
            if (settings == null)
                throw new InvalidOperationException("Assign a WildCatSettings asset to the WildCat Client component on " + name + ".");
#if UNITY_WEBGL && !UNITY_EDITOR
            webScheduler = new PumpedConnectionScheduler();
#endif
            client = new WildCatClient(settings.CreateOptions(), dispatcher, CreateTransportFactory(),
                new UnityWildCatLogger(settings.LogLevel), webScheduler);
        }

        private static ITransportFactory CreateTransportFactory()
        {
#if UNITY_WEBGL && !UNITY_EDITOR
            return new WebGLWebSocketTransportFactory();
#else
            return new ClientWebSocketTransportFactory();
#endif
        }
    }
}
