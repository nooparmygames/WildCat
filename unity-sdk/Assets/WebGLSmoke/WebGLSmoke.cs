using System;
using System.Threading;
using System.Threading.Tasks;
using UnityEngine;
using WildCat;
using WildCat.Protocol;
using WildCat.Unity;

/// <summary>
/// Browser integration check against the Development host's seeded game. Build a scene containing
/// this component, serve it over HTTP, and look for WILDCAT_WEBGL_SMOKE_PASS in the browser console.
/// Uses both the direct client and WildCatBehaviour, including real JavaScript WebSocket callbacks.
/// </summary>
public sealed class WebGLSmoke : MonoBehaviour
{
    private static readonly Guid Developer = new Guid("11111111-1111-1111-1111-111111111111");
    private static readonly Guid Game = new Guid("22222222-2222-2222-2222-222222222222");
    private PumpedConnectionScheduler scheduler;
    private readonly QueuedDispatcher dispatcher = new QueuedDispatcher();
    private WildCatClient first;
    private WildCatBehaviour second;
    private double started;
    private bool finished;

    private async void Start()
    {
        started = Time.realtimeSinceStartupAsDouble;
        Application.runInBackground = true;
        try
        {
            scheduler = new PumpedConnectionScheduler();
            var factory = new TrackingFactory();
            first = new WildCatClient(new WildCatClientOptions
            {
                ServerUri = new Uri("ws://127.0.0.1:5000/ws"),
                HeartbeatInterval = TimeSpan.FromSeconds(0.25),
                HeartbeatTimeout = TimeSpan.FromSeconds(3),
                RequestTimeout = TimeSpan.FromSeconds(5),
                ReconnectMinDelay = TimeSpan.FromSeconds(0.25),
                ReconnectMaxDelay = TimeSpan.FromSeconds(1),
            }, dispatcher, factory, new UnityWildCatLogger(WildCatLogLevel.Info), scheduler);
            await first.ConnectAsync();
            await first.Session.InitializeAsync(Developer, Game);
            var login = await first.Auth.LoginWithDeviceAsync("web-smoke-" + Guid.NewGuid().ToString("N"));
            Debug.Log("WILDCAT_WEBGL_CONNECTED");

            var settings = ScriptableObject.CreateInstance<WildCatSettings>();
            settings.ServerUrl = "ws://127.0.0.1:5000/ws";
            settings.DeveloperKey = Developer.ToString();
            settings.GameKey = Game.ToString();
            settings.HeartbeatIntervalSeconds = 1;
            second = new GameObject("Second browser client").AddComponent<WildCatBehaviour>();
            second.Settings = settings;
            await second.ConnectAndInitializeAsync();
            await second.Client.Auth.LoginWithDeviceAsync("web-smoke-" + Guid.NewGuid().ToString("N"));
            await first.Profile.SetNameAsync("Browser player");
            Require(await first.Profile.GetNameAsync() == "Browser player", "Profile round trip failed.");
            await Until(() => first.Connection.RoundTripTime > TimeSpan.Zero, "heartbeat");
            Debug.Log("WILDCAT_WEBGL_HEARTBEAT");

            bool timedOut = false;
            try
            {
                await first.Connection.RequestAsync<FindMatchResponse>(WildCatMsgType.FindMatchRequest,
                    new FindMatchRequest("timeout-" + Guid.NewGuid().ToString("N"), 0, 1), WildCatMsgType.FindMatchResponse,
                    new RequestOptions { Timeout = TimeSpan.FromMilliseconds(100) });
            }
            catch (TimeoutException) { timedOut = true; }
            Require(timedOut, "The request timeout did not fire.");
            Debug.Log("WILDCAT_WEBGL_TIMEOUT");

            string mode = "web-smoke-" + Guid.NewGuid().ToString("N");
            var left = first.Matchmaking.FindMatchAsync(mode);
            var right = second.Client.Matchmaking.FindMatchAsync(mode);
            var matches = await Task.WhenAll(left, right);
            Require(matches[0] != null && matches[1] != null && matches[0].Id == matches[1].Id, "Matchmaking failed.");
            bool relayed = false;
            var messageType = RelayClient.MessageType(42);
            using (second.Client.Relay.On<IntegerMessage>(messageType, message => relayed = message.value == 42))
            {
                Require(first.Relay.Send(matches[0].Id, messageType, new IntegerMessage { value = 42 }), "Relay was not queued.");
                await Until(() => relayed, "relay");
            }
            Debug.Log("WILDCAT_WEBGL_RELAY");

            bool reconnected = false;
            first.Reconnected += () => reconnected = true;
            Guid oldTicket = first.Session.Ticket;
            factory.Current.Abort();
            await Until(() => reconnected, "reconnection");
            Require(first.IsLoggedIn && first.Auth.PlayerId == login.PlayerId && first.Session.Ticket != oldTicket,
                "Reconnect did not resume the account.");
            Require(await first.Profile.GetNameAsync() == "Browser player", "Resumed requests failed.");
            Debug.Log("WILDCAT_WEBGL_RECONNECTED");
            await first.DisconnectAsync();
            await second.Client.DisconnectAsync();
            finished = true;
            Debug.Log("WILDCAT_WEBGL_SMOKE_PASS");
        }
        catch (Exception ex)
        {
            finished = true;
            Debug.LogError("WILDCAT_WEBGL_SMOKE_FAIL: " + ex);
        }
    }

    private void Update()
    {
        scheduler?.Pump();
        dispatcher.Pump();
        if (!finished && started > 0 && Time.realtimeSinceStartupAsDouble - started > 90)
        {
            finished = true;
            Debug.LogError("WILDCAT_WEBGL_SMOKE_FAIL: overall deadline exceeded");
        }
    }

    private static async Task Until(Func<bool> condition, string step)
    {
        double deadline = Time.realtimeSinceStartupAsDouble + 20;
        while (!condition())
        {
            Require(Time.realtimeSinceStartupAsDouble < deadline, step + " timed out.");
            await Awaitable.NextFrameAsync();
        }
    }

    private static void Require(bool condition, string message)
    {
        if (!condition) throw new InvalidOperationException(message);
    }

    private void OnDestroy() { first?.Dispose(); }

    private sealed class TrackingFactory : ITransportFactory
    {
        internal ITransport Current;
        public ITransport Create(WildCatClientOptions options)
        {
#if UNITY_WEBGL && !UNITY_EDITOR
            Current = new WebGLWebSocketTransport(options.MaxMessageBytes);
#else
            Current = new ClientWebSocketTransportFactory().Create(options);
#endif
            return Current;
        }
    }
}
