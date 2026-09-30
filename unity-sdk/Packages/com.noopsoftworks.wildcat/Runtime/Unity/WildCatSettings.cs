using System;
using UnityEngine;

namespace WildCat.Unity
{
    /// <summary>
    /// Where the game's WildCat server is and how the client behaves: server URL, developer and
    /// game keys, timeouts and reconnection. Create one with Assets > Create > WildCat > Settings
    /// (or the WildCat menu) and assign it to a <see cref="WildCatBehaviour"/>.
    /// </summary>
    /// <remarks>
    /// The developer and game keys identify the game; they are not secrets (every build ships
    /// them). The developer secret must never go into a game build or this asset.
    /// </remarks>
    [CreateAssetMenu(fileName = "WildCatSettings", menuName = "WildCat/Settings", order = 0)]
    public sealed class WildCatSettings : ScriptableObject
    {
        [Tooltip("The gateway's WebSocket URL. Use wss:// for anything but a server on this machine; passwords are sent as plain text.")]
        [SerializeField]
        private string serverUrl = "ws://localhost:5000/ws";

        [Tooltip("The developer key (a GUID) from the admin panel.")]
        [SerializeField]
        private string developerKey = "";

        [Tooltip("The game key (a GUID) from the admin panel.")]
        [SerializeField]
        private string gameKey = "";

        [Tooltip("How long a request waits for its answer, in seconds.")]
        [Min(1)]
        [SerializeField]
        private float requestTimeoutSeconds = 15;

        [Tooltip("How often a heartbeat goes to the server, in seconds. 0 turns heartbeats off (the server then marks the player offline after 20 seconds).")]
        [Min(0)]
        [SerializeField]
        private float heartbeatIntervalSeconds = 10;

        [Tooltip("Reconnect on its own when the connection drops, and resume the session and login.")]
        [SerializeField]
        private bool autoReconnect = true;

        [Tooltip("The first reconnection delay, in seconds; it doubles per attempt.")]
        [Min(0)]
        [SerializeField]
        private float reconnectMinDelaySeconds = 1;

        [Tooltip("The longest reconnection delay, in seconds.")]
        [Min(0)]
        [SerializeField]
        private float reconnectMaxDelaySeconds = 30;

        [Tooltip("Reconnection attempts before giving up; 0 never gives up.")]
        [Min(0)]
        [SerializeField]
        private int maxReconnectAttempts = 0;

        [Tooltip("Send passwords over ws:// to other machines. Only for a test server on a network you trust.")]
        [SerializeField]
        private bool allowPlainTextPasswordsWithoutTls = false;

        [Tooltip("The lowest level of SDK messages written to the Unity console.")]
        [SerializeField]
        private WildCatLogLevel logLevel = WildCatLogLevel.Warning;

        public string ServerUrl
        {
            get { return serverUrl; }
            set { serverUrl = value; }
        }

        public string DeveloperKey
        {
            get { return developerKey; }
            set { developerKey = value; }
        }

        public string GameKey
        {
            get { return gameKey; }
            set { gameKey = value; }
        }

        public float RequestTimeoutSeconds
        {
            get { return requestTimeoutSeconds; }
            set { requestTimeoutSeconds = value; }
        }

        public float HeartbeatIntervalSeconds
        {
            get { return heartbeatIntervalSeconds; }
            set { heartbeatIntervalSeconds = value; }
        }

        public bool AutoReconnect
        {
            get { return autoReconnect; }
            set { autoReconnect = value; }
        }

        public WildCatLogLevel LogLevel
        {
            get { return logLevel; }
            set { logLevel = value; }
        }

        /// <summary>Parses the two keys; false when either is missing or not a GUID.</summary>
        public bool TryGetKeys(out Guid developer, out Guid game)
        {
            game = Guid.Empty;
            return Guid.TryParse(developerKey, out developer) && Guid.TryParse(gameKey, out game)
                   && developer != Guid.Empty && game != Guid.Empty;
        }

        /// <summary>Client options from these settings. Throws ArgumentException for a URL that is not ws:// or wss://.</summary>
        public WildCatClientOptions CreateOptions()
        {
            Uri uri;
            if (!Uri.TryCreate(serverUrl, UriKind.Absolute, out uri))
                throw new ArgumentException("The WildCat server URL \"" + serverUrl + "\" is not a valid URL.");
            return new WildCatClientOptions
            {
                ServerUri = uri,
                RequestTimeout = TimeSpan.FromSeconds(Math.Max(1f, requestTimeoutSeconds)),
                HeartbeatInterval = TimeSpan.FromSeconds(Math.Max(0f, heartbeatIntervalSeconds)),
                AutoReconnect = autoReconnect,
                ReconnectMinDelay = TimeSpan.FromSeconds(Math.Max(0f, reconnectMinDelaySeconds)),
                ReconnectMaxDelay = TimeSpan.FromSeconds(Math.Max(reconnectMinDelaySeconds, reconnectMaxDelaySeconds)),
                MaxReconnectAttempts = Math.Max(0, maxReconnectAttempts),
                AllowPlainTextPasswordsWithoutTls = allowPlainTextPasswordsWithoutTls,
            };
        }

        private void OnValidate()
        {
            if (reconnectMaxDelaySeconds < reconnectMinDelaySeconds)
                reconnectMaxDelaySeconds = reconnectMinDelaySeconds;
        }
    }
}
