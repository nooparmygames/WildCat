using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Xunit;

namespace WildCat.Sdk.Tests
{
    /// <summary>
    /// Base of the tests that talk to the <see cref="ServerFixture"/>. Clients made with
    /// <see cref="NewClient"/> are closed after each test.
    /// </summary>
    public abstract class ServerTestBase : IAsyncLifetime
    {
        private readonly List<WildCatClient> clients = new List<WildCatClient>();

        protected ServerTestBase(ServerFixture server)
        {
            Server = server;
        }

        protected ServerFixture Server { get; }

        /// <summary>
        /// A client for the test server with quick reconnection (100 ms to 500 ms) so reconnection
        /// tests do not wait for the production delays.
        /// </summary>
        protected WildCatClient NewClient(Action<WildCatClientOptions> configure = null, IMainThreadDispatcher dispatcher = null,
            ListLogger logger = null)
        {
            var options = new WildCatClientOptions
            {
                ServerUri = Server.ServerUri,
                ReconnectMinDelay = TimeSpan.FromMilliseconds(100),
                ReconnectMaxDelay = TimeSpan.FromMilliseconds(500),
            };
            configure?.Invoke(options);
            var client = new WildCatClient(options, dispatcher, null, logger ?? new ListLogger());
            lock (clients)
                clients.Add(client);
            return client;
        }

        /// <summary>A connected client with a session for the game and a device login.</summary>
        protected async Task<(WildCatClient Client, LoginResult Login)> LoggedInClientAsync(Guid developerKey, Guid gameKey,
            string deviceId = null, Action<WildCatClientOptions> configure = null, ListLogger logger = null)
        {
            WildCatClient client = NewClient(configure, null, logger);
            await Wait.For(client.ConnectAsync());
            await Wait.For(client.Session.InitializeAsync(developerKey, gameKey));
            LoginResult login = await Wait.For(client.Auth.LoginWithDeviceAsync(deviceId ?? Unique.Id("device")));
            return (client, login);
        }

        public Task InitializeAsync()
        {
            return Task.CompletedTask;
        }

        public async Task DisposeAsync()
        {
            List<WildCatClient> all;
            lock (clients)
                all = clients.ToList();
            foreach (WildCatClient client in all)
            {
                try
                {
                    await client.DisconnectAsync().WaitAsync(TimeSpan.FromSeconds(10));
                }
                catch (Exception)
                {
                }
                client.Dispose();
            }
        }
    }

    /// <summary>Keeps the SDK's log messages for assertions.</summary>
    public sealed class ListLogger : IWildCatLogger
    {
        private readonly ConcurrentQueue<(WildCatLogLevel Level, string Message)> entries = new ConcurrentQueue<(WildCatLogLevel, string)>();

        public IReadOnlyList<(WildCatLogLevel Level, string Message)> Entries
        {
            get { return entries.ToList(); }
        }

        public void Log(WildCatLogLevel level, string message, Exception exception)
        {
            entries.Enqueue((level, exception == null ? message : message + " " + exception.Message));
        }

        public bool Contains(string text)
        {
            return entries.Any(e => e.Message.Contains(text, StringComparison.Ordinal));
        }
    }

    /// <summary>Records the events of one kind in arrival order, thread safe.</summary>
    public sealed class Recorder<T>
    {
        private readonly List<T> items = new List<T>();

        public void Add(T item)
        {
            lock (items)
                items.Add(item);
        }

        public IReadOnlyList<T> Items
        {
            get
            {
                lock (items)
                    return items.ToList();
            }
        }

        public int Count
        {
            get
            {
                lock (items)
                    return items.Count;
            }
        }

        public async Task<T> WaitForAsync(Func<T, bool> match, string description)
        {
            T found = default;
            await Wait.UntilAsync(() =>
            {
                lock (items)
                {
                    int index = items.FindIndex(i => match(i));
                    if (index < 0)
                        return false;
                    found = items[index];
                    return true;
                }
            }, description);
            return found;
        }

        public Task<T> WaitForAnyAsync(string description)
        {
            return WaitForAsync(_ => true, description);
        }
    }
}
