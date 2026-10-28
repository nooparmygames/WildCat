using System.Collections.Concurrent;
using Orleans;
using Orleans.TestingHost;
using WildCat.Interfaces;
using WildCat.Protocol;
using Xunit;

namespace WildCat.Tests;

/// <summary>Pauses a selected player's GameStarted call while the matchmaker is creating its match.</summary>
public sealed class MatchmakingCallGate : IIncomingGrainCallFilter
{
    private static readonly ConcurrentDictionary<string, Gate> gates = new();
    private static readonly ConcurrentDictionary<Guid, Gate> modes = new();

    internal static Gate Pause(IPlayer player)
    {
        string key = player.GetPrimaryKeyString();
        var gate = new Gate(() => gates.TryRemove(key, out _));
        if (!gates.TryAdd(key, gate))
            throw new InvalidOperationException("This player's match notification is already paused.");
        return gate;
    }

    internal static Gate PauseMode(Guid game)
    {
        var gate = new Gate(() => modes.TryRemove(game, out _));
        if (!modes.TryAdd(game, gate))
            throw new InvalidOperationException("This game's mode lookup is already paused.");
        return gate;
    }

    public async Task Invoke(IIncomingGrainCallContext context)
    {
        Gate gate = null;
        if (context.Grain is IPlayer player && context.InterfaceMethod?.Name == nameof(IPlayer.GameStarted)
            && gates.TryGetValue(player.GetPrimaryKeyString(), out var playerGate))
            gate = playerGate;
        else if (context.Grain is IGameModeManager manager && context.InterfaceMethod?.Name == nameof(IGameModeManager.GetGameModeData)
            && modes.TryGetValue(manager.GetPrimaryKey(), out var modeGate))
            gate = modeGate;
        if (gate != null)
        {
            gate.MarkStarted();
            await gate.Released.WaitAsync(TestSetup.Timeout);
        }
        await context.Invoke();
    }

    internal sealed class Gate(Action remove) : IDisposable
    {
        private readonly TaskCompletionSource started = new(TaskCreationOptions.RunContinuationsAsynchronously);
        private readonly TaskCompletionSource released = new(TaskCreationOptions.RunContinuationsAsynchronously);
        public Task Started => started.Task;
        public Task Released => released.Task;
        public void MarkStarted() => started.TrySetResult();
        public void Dispose()
        {
            remove();
            released.TrySetResult();
        }
    }
}

/// <summary>Delays a completed join's reply without holding the player or match grain's turn.</summary>
public sealed class MatchmakingReplyGate : IOutgoingGrainCallFilter
{
    private static readonly ConcurrentDictionary<string, MatchmakingCallGate.Gate> gates = new();

    internal static MatchmakingCallGate.Gate Pause(IPlayer player)
    {
        string key = player.GetPrimaryKeyString();
        var gate = new MatchmakingCallGate.Gate(() => gates.TryRemove(key, out _));
        if (!gates.TryAdd(key, gate))
            throw new InvalidOperationException("This player's join reply is already paused.");
        return gate;
    }

    public async Task Invoke(IOutgoingGrainCallContext context)
    {
        MatchmakingCallGate.Gate gate = null;
        if (context.Grain is IPlayer player && context.InterfaceMethod?.Name == nameof(IPlayer.TryJoinMatch))
            gates.TryGetValue(player.GetPrimaryKeyString(), out gate);
        await context.Invoke();
        if (gate != null)
        {
            gate.MarkStarted();
            await gate.Released.WaitAsync(TestSetup.Timeout);
        }
    }
}

[Collection(ClusterCollection.Name)]
public sealed class MatchmakingRegressionTests(ClusterFixture fixture)
{
    private InProcessTestCluster Cluster => fixture.Cluster;
    private IGrainFactory Grains => Cluster.Client;

    private async Task<IPlayer> Player()
    {
        string id = TestSetup.UniqueId("match-regression");
        var player = Grains.GetGrain<IPlayer>(id);
        await player.CreateAccountWithDevice(id);
        return player;
    }

    private ICustomMatchMaker Matchmaker(Guid? game = null) =>
        Grains.GetGrain<ICustomMatchMaker>(game ?? Guid.NewGuid(), "regression", null);

    private static async Task ExpectCanceledSearch(Task<FoundRelayMatchResult> search)
    {
        try
        {
            Assert.Null(await search.WaitAsync(TestSetup.Timeout));
        }
        catch (OperationCanceledException)
        {
        }
    }

    [Fact]
    public async Task RemovingDisconnectedPlayerCompletesItsSearchAndAllowsAnotherSearch()
    {
        var player = await Player();
        var other = await Player();
        var maker = Matchmaker();
        var abandoned = maker.FindMatch(player, 100, 20);
        await TestSetup.WaitUntilAsync(async () => await maker.GetOnlinePlayersCount() == 1, "the first search");

        Assert.True(await maker.RemoveDisconnectedPlayer(player));
        Assert.Null(await abandoned.WaitAsync(TestSetup.Timeout));
        Assert.Equal(0, await maker.GetOnlinePlayersCount());

        var found = await Task.WhenAll(maker.FindMatch(player, 100, 10), maker.FindMatch(other, 100, 10))
            .WaitAsync(TestSetup.Timeout);
        Assert.All(found, Assert.NotNull);
        Assert.Equal(found[0].game, found[1].game);
    }

    [Fact]
    public async Task CancelingASearchRemovesItBeforeAnotherPlayerCanMatch()
    {
        var maker = Matchmaker();
        using var canceled = new CancellationTokenSource();
        var abandoned = maker.FindMatch(await Player(), 100, 20, cancellationToken: canceled.Token);
        await TestSetup.WaitUntilAsync(async () => await maker.GetOnlinePlayersCount() == 1, "the cancellable search");
        canceled.Cancel();
        await ExpectCanceledSearch(abandoned);
        await TestSetup.WaitUntilAsync(async () => await maker.GetOnlinePlayersCount() == 0, "search cancellation");

        Assert.Null(await maker.FindMatch(await Player(), 100, 1).WaitAsync(TestSetup.Timeout));
    }

    [Fact]
    public async Task AnOldCallsCancellationCannotRemoveItsReplacementSearch()
    {
        var player = await Player();
        var maker = Matchmaker();
        using var oldCancellation = new CancellationTokenSource();
        var original = maker.FindMatch(player, 100, 20, cancellationToken: oldCancellation.Token);
        await TestSetup.WaitUntilAsync(async () => await maker.GetOnlinePlayersCount() == 1, "the original search");
        var replacement = maker.FindMatch(player, 100, 10);
        Assert.Null(await original.WaitAsync(TestSetup.Timeout));
        oldCancellation.Cancel();
        Assert.Equal(1, await maker.GetOnlinePlayersCount());

        var found = await Task.WhenAll(replacement, maker.FindMatch(await Player(), 100, 10)).WaitAsync(TestSetup.Timeout);
        Assert.All(found, Assert.NotNull);
        Assert.Equal(found[0].game, found[1].game);
    }

    [Fact]
    public async Task CancellationDuringStartGameRetiresThePartialMatchAndPreservesOtherSearches()
    {
        var disconnected = await Player();
        var survivor = await Player();
        var replacement = await Player();
        var maker = Matchmaker();
        using var gate = MatchmakingCallGate.Pause(disconnected);
        using var canceled = new CancellationTokenSource();
        var abandoned = maker.FindMatch(disconnected, 100, 20, cancellationToken: canceled.Token);
        var waiting = maker.FindMatch(survivor, 100, 20);
        await gate.Started.WaitAsync(TestSetup.Timeout);

        canceled.Cancel();
        await ExpectCanceledSearch(abandoned);
        await TestSetup.WaitUntilAsync(async () => await maker.GetOnlinePlayersCount() == 1, "cancellation during match creation");
        var newSearch = maker.FindMatch(replacement, 100, 20);
        gate.Dispose();

        var found = await Task.WhenAll(waiting, newSearch).WaitAsync(TestSetup.Timeout);
        Assert.All(found, Assert.NotNull);
        Assert.Equal(found[0].game, found[1].game);
        var players = await found[0].game.GetPlayers();
        Assert.DoesNotContain(disconnected, players);
        Assert.Contains(survivor, players);
        Assert.Contains(replacement, players);
        await TestSetup.WaitUntilAsync(async () => (await MatchTestSetup.RunningMatches(disconnected)).Count == 0,
            "the abandoned match to leave the canceled player's state");
    }

    [Fact]
    public async Task ASlowMatchCreationDoesNotLetAnotherPairMatchAfterItsDeadline()
    {
        var first = await Player();
        var partner = await Player();
        var waiting = await Player();
        var waitingPartner = await Player();
        var game = Guid.NewGuid();
        var maker = Matchmaker(game);
        using var modeGate = MatchmakingCallGate.PauseMode(game);
        using var creationGate = MatchmakingCallGate.Pause(first);
        var firstSearch = maker.FindMatch(first, 0, 20);
        var partnerSearch = maker.FindMatch(partner, 0, 20);
        await modeGate.Started.WaitAsync(TestSetup.Timeout);
        await TestSetup.WaitUntilAsync(async () => await maker.GetOnlinePlayersCount() == 2, "the first pair queued before the second");
        var expired = maker.FindMatch(waiting, 100000, 1);
        var expiredPartner = maker.FindMatch(waitingPartner, 100000, 1);
        await TestSetup.WaitUntilAsync(async () => await maker.GetOnlinePlayersCount() == 4, "all four searches in one matching pass");
        modeGate.Dispose();
        await creationGate.Started.WaitAsync(TestSetup.Timeout);
        await Task.Delay(TimeSpan.FromSeconds(1.2));
        creationGate.Dispose();

        Assert.All(await Task.WhenAll(firstSearch, partnerSearch).WaitAsync(TestSetup.Timeout), Assert.NotNull);
        Assert.All(await Task.WhenAll(expired, expiredPartner).WaitAsync(TestSetup.Timeout), Assert.Null);
        Assert.Empty(await MatchTestSetup.RunningMatches(waiting));
        Assert.Empty(await MatchTestSetup.RunningMatches(waitingPartner));
    }

    [Fact]
    public async Task AnUpdatedModeAppliesWithoutReactivatingTheMatchmaker()
    {
        var game = Guid.NewGuid();
        var manager = Grains.GetGrain<IGameModeManager>(game);
        var mode = GameMode.CreateDefault();
        mode.Name = "regression";
        await manager.AddOrUpdateGameMode(mode);
        var maker = Matchmaker(game);
        Assert.Equal(0, await maker.GetOnlinePlayersCount());

        mode.MinPlayers = mode.MaxPlayers = 3;
        mode.FinishGameWithOneDisconnect = false;
        await manager.AddOrUpdateGameMode(mode);
        var tooFew = await Task.WhenAll(maker.FindMatch(await Player(), 100, 2), maker.FindMatch(await Player(), 100, 2))
            .WaitAsync(TestSetup.Timeout);
        Assert.All(tooFew, Assert.Null);

        var found = await Task.WhenAll(Enumerable.Range(0, 3).Select(async _ => await maker.FindMatch(await Player(), 100, 10)))
            .WaitAsync(TestSetup.Timeout);
        Assert.All(found, Assert.NotNull);
        Assert.Single(found.Select(result => result.game).Distinct());
        var state = await found[0].game.GetState();
        Assert.Equal(3, state.Mode.MaxPlayers);
        Assert.False(state.Mode.FinishGameWithOneDisconnect);
    }

    [Fact]
    public async Task DeletingAModeMakesTheExistingQueueUseDefaultRules()
    {
        var game = Guid.NewGuid();
        var manager = Grains.GetGrain<IGameModeManager>(game);
        var mode = GameMode.CreateDefault();
        mode.Name = "regression";
        mode.MinPlayers = mode.MaxPlayers = 3;
        await manager.AddOrUpdateGameMode(mode);
        var maker = Matchmaker(game);
        Assert.Equal(0, await maker.GetOnlinePlayersCount());
        await manager.Remove("regression");

        var found = await Task.WhenAll(maker.FindMatch(await Player(), 100, 10), maker.FindMatch(await Player(), 100, 10))
            .WaitAsync(TestSetup.Timeout);
        Assert.All(found, Assert.NotNull);
        Assert.Equal(found[0].game, found[1].game);
        Assert.Equal(2, (await found[0].game.GetPlayers()).Count);
    }

    [Fact]
    public async Task SkillDistanceDoesNotWrapAtUnsignedIntegerBoundaries()
    {
        var game = Guid.NewGuid();
        var mode = GameMode.CreateDefault();
        mode.Name = "regression";
        mode.Criterias[0].AcceptableThreshold = int.MaxValue;
        await Grains.GetGrain<IGameModeManager>(game).AddOrUpdateGameMode(mode);
        var maker = Matchmaker(game);
        var found = await Task.WhenAll(maker.FindMatch(await Player(), 0, 2), maker.FindMatch(await Player(), uint.MaxValue, 2))
            .WaitAsync(TestSetup.Timeout);
        Assert.All(found, Assert.Null);
    }

    [Fact]
    public async Task SocketDisconnectCancelsItsQueuedSearchAndTheAccountCanSearchAgain()
    {
        var (developer, game) = await TestSetup.CreateDeveloperAndGame(Grains);
        await using var host = await GatewayTestHost.StartAsync(Cluster.Client);
        var maker = Matchmaker(game);
        string device = TestSetup.UniqueId("disconnecting-device");
        string playerId;
        await using (var disconnected = await GatewayPlayer.ConnectAsync(host, developer, game, device))
        {
            playerId = disconnected.Id;
            await disconnected.Client.SendAsync(disconnected.Request(WildCatMsgType.FindMatchRequest,
                new FindMatchRequest("regression", 100, 20)));
            await TestSetup.WaitUntilAsync(async () => await maker.GetOnlinePlayersCount() == 1, "the socket's search");
        }
        await TestSetup.WaitUntilAsync(async () => !await Grains.GetGrain<IPlayer>(playerId).IsLoggedIn(), "logout");
        await TestSetup.WaitUntilAsync(async () => await maker.GetOnlinePlayersCount() == 0, "the closed socket's search removal");

        await using var other = await GatewayPlayer.ConnectAsync(host, developer, game);
        var alone = await other.CallAsync<FindMatchResponse>(WildCatMsgType.FindMatchRequest,
            new FindMatchRequest("regression", 100, 1), WildCatMsgType.FindMatchResponse);
        Assert.False(alone.isSuccesful);
        Assert.Empty(await MatchTestSetup.RunningMatches(Grains.GetGrain<IPlayer>(playerId)));

        await using var reconnected = await GatewayPlayer.ConnectAsync(host, developer, game, device);
        var together = await Task.WhenAll(new[] { reconnected, other }.Select(player =>
            player.CallAsync<FindMatchResponse>(WildCatMsgType.FindMatchRequest,
                new FindMatchRequest("regression", 100, 10), WildCatMsgType.FindMatchResponse)));
        Assert.All(together, answer => Assert.True(answer.isSuccesful, answer.errorMsg));
        Assert.Equal(together[0].msg.gameId, together[1].msg.gameId);
    }

    [Fact]
    public async Task MovingTheAccountToAnotherGatewayAndGameInvalidatesItsOldSearch()
    {
        var (developer1, game1) = await TestSetup.CreateDeveloperAndGame(Grains);
        var (developer2, game2) = await TestSetup.CreateDeveloperAndGame(Grains);
        await using var host1 = await GatewayTestHost.StartAsync(Cluster.Client);
        await using var host2 = await GatewayTestHost.StartAsync(Cluster.Client);
        await using var original = await GatewayPlayer.ConnectAsync(host1, developer1, game1);
        var maker = Matchmaker(game1);
        var request = original.Request(WildCatMsgType.FindMatchRequest, new FindMatchRequest("regression", 100, 20));
        await original.Client.SendAsync(request);
        await TestSetup.WaitUntilAsync(async () => await maker.GetOnlinePlayersCount() == 1, "the old gateway's search");

        await using var replacement = await GatewayPlayer.ConnectAsync(host2, developer2, game2, original.Device);
        Assert.Equal(original.Id, replacement.Id);
        await TestSetup.WaitUntilAsync(async () => await maker.GetOnlinePlayersCount() == 0, "the invalidated login's search removal");
        var reply = await original.Client.WaitForAsync(frame => frame.hasRequestID && frame.requestID == request.requestID,
            "the old gateway's failed search response");
        Assert.False(reply.GetPayload<OptionalResponse<FindMatchResponse>>().isSuccesful);
        Assert.True(await Grains.GetGrain<IPlayer>(replacement.Id).IsLoggedIn());

        await using var other = await GatewayPlayer.ConnectAsync(host1, developer1, game1);
        var alone = await other.CallAsync<FindMatchResponse>(WildCatMsgType.FindMatchRequest,
            new FindMatchRequest("regression", 100, 1), WildCatMsgType.FindMatchResponse);
        Assert.False(alone.isSuccesful);
        Assert.Empty(await MatchTestSetup.RunningMatches(Grains.GetGrain<IPlayer>(original.Id)));
    }

    [Fact]
    public async Task DelayedCleanupFromAnOldSearchDoesNotDisconnectARejoinedLogin()
    {
        var (developer, game) = await TestSetup.CreateDeveloperAndGame(Grains);
        var mode = GameMode.CreateDefault();
        mode.Name = "regression";
        mode.FinishGameWithOneDisconnect = false;
        await Grains.GetGrain<IGameModeManager>(game).AddOrUpdateGameMode(mode);
        await using var host = await GatewayTestHost.StartAsync(Cluster.Client);
        await using var original = await GatewayPlayer.ConnectAsync(host, developer, game);
        await using var other = await GatewayPlayer.ConnectAsync(host, developer, game);
        var player = Grains.GetGrain<IPlayer>(original.Id);
        var oldConnection = host.Connections.GetByPlayer(original.Id);
        using var gate = MatchmakingReplyGate.Pause(player);
        foreach (var connected in new[] { original, other })
            await connected.Client.SendAsync(connected.Request(WildCatMsgType.FindMatchRequest, new FindMatchRequest("regression", 100, 10)));
        await gate.Started.WaitAsync(TestSetup.Timeout);
        var match = Assert.Single((await player.GetState()).RunningGames);
        Guid matchId = match.GetPrimaryKey(out string _);

        original.Client.Abort();
        await original.Client.Closed.WaitAsync(TestSetup.Timeout);
        await TestSetup.WaitUntilAsync(async () => !await player.IsLoggedIn(), "the original login's disconnect");
        Assert.Equal(RelayServerPlayerGameStatus.Disconnected, await match.GetPlayerStatus(player));
        await using var reconnected = await GatewayPlayer.ConnectAsync(host, developer, game, original.Device);
        var rejoin = await reconnected.CallAsync<EmptyMessage>(WildCatMsgType.RejoinMatchRequest,
            new GuidMessage { value = matchId }, WildCatMsgType.RejoinMatchResponse);
        Assert.True(rejoin.isSuccesful, rejoin.errorMsg);
        gate.Dispose();
        await TestSetup.WaitUntilAsync(() => oldConnection.RequestSlots.CurrentCount == 8, "the old search handler to finish cleanup");

        Assert.True(await player.IsLoggedIn());
        Assert.Equal(RelayServerPlayerGameStatus.InGame, await match.GetPlayerStatus(player));
    }

    [Fact]
    public async Task JoiningWithAStaleLoginTicketDoesNotChangeMatchMembership()
    {
        var (developer, game, session) = await TestSetup.CreateDeveloperGameAndSession(Grains);
        var oldTicket = Guid.NewGuid();
        var player = await TestSetup.CreateLoggedInPlayer(Grains, session, oldTicket);
        var match = Grains.GetGrain<IRelayServerGame>(Guid.NewGuid(), game.ToString(), null);
        await match.StartGame(new[] { player }, GameMode.CreateDefault());
        var newSession = await Grains.GetGrain<IDeveloper>(developer).InitializeSession(game);
        var newTicket = Guid.NewGuid();
        Assert.NotNull(await player.LoginWithDeviceID(player.GetPrimaryKeyString(), newSession, newTicket));

        Assert.False(await player.TryJoinMatch(match, oldTicket));
        Assert.Equal(RelayServerPlayerGameStatus.NotStarted, await match.GetPlayerStatus(player));
        Assert.True(await player.TryJoinMatch(match, newTicket));
        Assert.Equal(RelayServerPlayerGameStatus.InGame, await match.GetPlayerStatus(player));
    }

    [Fact]
    public async Task ReplacingTheSocketsLoginCancelsThePreviousAccountsSearch()
    {
        var (developer, game) = await TestSetup.CreateDeveloperAndGame(Grains);
        await using var host = await GatewayTestHost.StartAsync(Cluster.Client);
        await using var connected = await GatewayPlayer.ConnectAsync(host, developer, game);
        var maker = Matchmaker(game);
        var request = connected.Request(WildCatMsgType.FindMatchRequest, new FindMatchRequest("regression", 100, 20));
        await connected.Client.SendAsync(request);
        await TestSetup.WaitUntilAsync(async () => await maker.GetOnlinePlayersCount() == 1, "the previous account's search");

        var newLogin = await connected.Client.LoginWithDeviceAsync(Guid.Parse(connected.Ticket), TestSetup.UniqueId("other-device"));
        Assert.NotEqual(connected.Id, newLogin.userId);
        await TestSetup.WaitUntilAsync(async () => await maker.GetOnlinePlayersCount() == 0, "the replaced login's search removal");
        var reply = await connected.Client.WaitForAsync(frame => frame.hasRequestID && frame.requestID == request.requestID,
            "the canceled search's response");
        Assert.Equal(WildCatMsgType.FindMatchResponse, reply.msgType);
        Assert.False(reply.GetPayload<OptionalResponse<FindMatchResponse>>().isSuccesful);
        Assert.DoesNotContain(host.Logs.Entries, entry => entry.Level >= Microsoft.Extensions.Logging.LogLevel.Error);
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public async Task PendingMatchesContainOnlyTheRequestGame(bool sameDeveloper)
    {
        var (developer1, game1) = await TestSetup.CreateDeveloperAndGame(Grains);
        var (developer2, game2) = sameDeveloper
            ? (developer1, await Grains.GetGrain<IDeveloper>(developer1).AddGame(new GameInfo { Name = "second" }))
            : await TestSetup.CreateDeveloperAndGame(Grains);
        await using var host = await GatewayTestHost.StartAsync(Cluster.Client);
        string device = TestSetup.UniqueId("shared-device");
        Guid match1 = await CreatePendingMatch(host, developer1, game1, device);
        Guid match2 = await CreatePendingMatch(host, developer2, game2, device);

        foreach (var (developer, game, expected, excluded) in new[]
            { (developer1, game1, match1, match2), (developer2, game2, match2, match1) })
        {
            await using var connected = await GatewayPlayer.ConnectAsync(host, developer, game, device);
            var response = await connected.CallAsync<PendingMatchesCollectionData>(WildCatMsgType.GetPlayerPendingMatchesRequest,
                new EmptyMessage(), WildCatMsgType.GetPlayerMatchesResponse);
            Assert.True(response.isSuccesful, response.errorMsg);
            Assert.Equal(expected, Assert.Single(response.msg.matches).matchId);
            Assert.DoesNotContain(response.msg.matches, match => match.matchId == excluded);
        }
    }

    private async Task<Guid> CreatePendingMatch(GatewayTestHost host, Guid developer, Guid game, string device)
    {
        var connected = await GatewayPlayer.ConnectAsync(host, developer, game, device);
        var player = Grains.GetGrain<IPlayer>(connected.Id);
        var match = Grains.GetGrain<IRelayServerGame>(Guid.NewGuid(), game.ToString(), null);
        var mode = GameMode.CreateDefault();
        mode.FinishGameWithOneDisconnect = false;
        try
        {
            await match.StartGame(new[] { player }, mode);
            await match.PlayerStarted(player);
        }
        finally
        {
            await connected.DisposeAsync();
        }
        await TestSetup.WaitUntilAsync(async () => !await player.IsLoggedIn(), "the pending player's logout");
        Assert.Equal(RelayServerPlayerGameStatus.Disconnected, await match.GetPlayerStatus(player));
        return match.GetPrimaryKey(out string _);
    }
}
