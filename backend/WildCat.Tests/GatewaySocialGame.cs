using Microsoft.Extensions.Logging;
using Orleans;
using Orleans.Streams;
using WildCat.Interfaces;
using WildCat.Protocol;
using Xunit;

namespace WildCat.Tests
{
    /// <summary>
    /// A new developer and game with a gateway test host, for the friend and challenge tests:
    /// creates named players and connects each on a socket of its own. Disposing closes every
    /// socket and then the host.
    /// </summary>
    internal sealed class GatewaySocialGame : IAsyncDisposable
    {
        private readonly List<GatewayTestClient> clients = new List<GatewayTestClient>();

        private GatewaySocialGame(IClusterClient grains, GatewayTestHost host, Guid developerKey, Guid gameKey)
        {
            Grains = grains;
            Host = host;
            DeveloperKey = developerKey;
            GameKey = gameKey;
        }

        public IClusterClient Grains { get; }

        public GatewayTestHost Host { get; }

        public Guid DeveloperKey { get; }

        public Guid GameKey { get; }

        public static async Task<GatewaySocialGame> StartAsync(IClusterClient grains)
        {
            var (developerKey, gameKey) = await TestSetup.CreateDeveloperAndGame(grains);
            var host = await GatewayTestHost.StartAsync(grains);
            return new GatewaySocialGame(grains, host, developerKey, gameKey);
        }

        /// <summary>
        /// Creates a device account called <paramref name="name"/> without logging it in. The name
        /// is set before the first login because the friend list takes the player's name from it.
        /// </summary>
        public async Task<SocialPlayer> CreateAsync(string name)
        {
            string device = TestSetup.UniqueId("device");
            var created = await Grains.GetGrain<IPlayerCreator>(0).CreateAccountWithDevice(device);
            Assert.Equal(AdditionResult.Success, created.result);
            await Grains.GetGrain<IPlayer>(created.Account).SetPlayerName(name);
            return new SocialPlayer(created.Account, device, name);
        }

        /// <summary>Creates a player and logs it in on a new socket.</summary>
        public async Task<SocialPlayer> JoinAsync(string name)
        {
            return await ConnectAsync(await CreateAsync(name));
        }

        /// <summary>Logs <paramref name="player"/> in on a new socket, which replaces the one it had.</summary>
        public async Task<SocialPlayer> ConnectAsync(SocialPlayer player)
        {
            var client = await Host.ConnectAsync();
            clients.Add(client);
            var session = await client.InitializeSessionAsync(DeveloperKey, GameKey);
            var login = await client.LoginWithDeviceAsync(session, player.Device);
            Assert.Equal(player.Id, login.userId);
            player.Client = client;
            player.Ticket = session.ToString();
            return player;
        }

        /// <summary>Closes the player's socket and waits until the gateway and the grain have let the login go.</summary>
        public async Task DisconnectAsync(SocialPlayer player)
        {
            await player.Client.DisposeAsync();
            await TestSetup.WaitUntilAsync(() => Host.Connections.GetByPlayer(player.Id) == null,
                $"the gateway to release {player.Name}");
            await TestSetup.WaitUntilAsync(async () => !await Grains.GetGrain<IPlayer>(player.Id).IsLoggedIn(),
                $"{player.Name} to be logged out");
        }

        /// <summary>How many subscriptions this node's cluster client holds on the player's friend events.</summary>
        public async Task<int> FriendSubscriptionsAsync(string playerId)
        {
            var stream = Grains.GetStreamProvider(StreamNames.PROVIDER_NAME)
                .GetStream<FriendListEventData>(StreamNames.FriendEvents(GameKey, playerId));
            return (await stream.GetAllSubscriptionHandles()).Count;
        }

        /// <summary>How many subscriptions this node's cluster client holds on the player's challenge events.</summary>
        public async Task<int> ChallengeSubscriptionsAsync(string playerId)
        {
            var stream = Grains.GetStreamProvider(StreamNames.PROVIDER_NAME)
                .GetStream<ChallengeEventData>(StreamNames.ChallengeEvents(GameKey, playerId));
            return (await stream.GetAllSubscriptionHandles()).Count;
        }

        /// <summary>Fails when the gateway logged a warning or an error.</summary>
        public void AssertNothingWentWrong()
        {
            Assert.DoesNotContain(Host.Logs.Entries, e => e.Level >= LogLevel.Warning);
        }

        public async ValueTask DisposeAsync()
        {
            foreach (var client in clients)
                await client.DisposeAsync();
            await Host.DisposeAsync();
        }
    }

    /// <summary>A player of a <see cref="GatewaySocialGame"/> and the socket it is logged in on.</summary>
    internal sealed class SocialPlayer
    {
        /// <summary>How long a test watches for a frame that must not arrive.</summary>
        public static readonly TimeSpan QuietPeriod = TimeSpan.FromMilliseconds(500);

        public SocialPlayer(string id, string device, string name)
        {
            Id = id;
            Device = device;
            Name = name;
        }

        public string Id { get; }

        public string Device { get; }

        public string Name { get; }

        public GatewayTestClient Client { get; set; }

        public string Ticket { get; set; }

        public IPlayer Grain(IGrainFactory grains) => grains.GetGrain<IPlayer>(Id);

        /// <summary>Sends a request with this player's ticket and returns the answer.</summary>
        public Task<OptionalResponse<T>> CallAsync<T>(WildCatMsgType type, MessageBase payload, WildCatMsgType responseType)
            where T : MessageBase, new()
        {
            return Client.CallAsync<T>(type, payload, responseType, Ticket);
        }

        /// <summary>Waits for a pushed event of <paramref name="type"/> and returns its successful payload.</summary>
        public async Task<T> WaitForEventAsync<T>(WildCatMsgType type) where T : MessageBase, new()
        {
            var frame = await Client.WaitForTypeAsync(type);
            Assert.Equal(WildCatMsgHeader.normal, frame.header);
            var payload = frame.GetPayload<OptionalResponse<T>>();
            Assert.True(payload.isSuccesful, payload.errorMsg);
            return payload.msg;
        }

        /// <summary>How many frames of <paramref name="type"/> arrived, after a short quiet period for stragglers.</summary>
        public async Task<int> CountAfterQuietPeriodAsync(WildCatMsgType type)
        {
            await Task.Delay(QuietPeriod);
            return Client.Received.Count(m => m.msgType == type);
        }
    }
}
