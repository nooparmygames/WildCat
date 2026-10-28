using System.Net;
using Microsoft.Extensions.DependencyInjection;
using Orleans;
using Orleans.Runtime;
using Orleans.Storage;
using Orleans.TestingHost;
using WildCat.Admin;
using WildCat.Grains;
using WildCat.Interfaces;
using Xunit;

namespace WildCat.Tests;

[Collection(ClusterCollection.Name)]
public class LeaderBoardPersistenceTests(ClusterFixture fixture)
{
    private InProcessTestCluster Cluster => fixture.Cluster;
    private IGrainFactory Grains => Cluster.Client;

    private ILeaderBoardStorage Chunk(ILeaderBoard board, int index) => Grains.GetGrain<ILeaderBoardStorage>(
        ResourcesUtility.GetLeaderBoardChunkKey(board.GetPrimaryKeyString(), index));

    private async Task<ILeaderBoard> CreateBoard(int chunkLength = 500000, bool bestScores = true)
    {
        var board = Grains.GetGrain<ILeaderBoard>(TestSetup.UniqueId("board"));
        await board.Initialize("weekly", DateTime.UtcNow, DateTime.MaxValue, bestScores).WaitAsync(TestSetup.Timeout);
        if (chunkLength != 500000)
        {
            // Small stored chunks exercise the real boundary without creating half a million players.
            await Cluster.Deactivate(board);
            var storage = Cluster.GetSiloServiceProvider(Cluster.Silos[0].SiloAddress)
                .GetRequiredKeyedService<IGrainStorage>(GrainStorageNames.Blob);
            var state = new GrainState<LeaderBoardState>();
            await storage.ReadStateAsync(GrainStateNames.LeaderBoard, board.GetGrainId(), state).WaitAsync(TestSetup.Timeout);
            state.State.MaxListLength = chunkLength;
            await storage.WriteStateAsync(GrainStateNames.LeaderBoard, board.GetGrainId(), state).WaitAsync(TestSetup.Timeout);
        }
        return board;
    }

    private IPlayer Player() => Grains.GetGrain<IPlayer>(TestSetup.UniqueId("score-player"));

    [Fact]
    public async Task AScoreInTheFirstPartialChunkSurvivesDeactivation()
    {
        var board = await CreateBoard();
        var player = Player();
        await board.AddPlayerScore(player, 42).WaitAsync(TestSetup.Timeout);
        await Cluster.Deactivate(board);
        await Cluster.Deactivate(Chunk(board, 0));

        var entry = Assert.Single(await board.GetEntries().WaitAsync(TestSetup.Timeout));
        Assert.Equal(player.GetPrimaryKeyString(), entry.PlayerID);
        Assert.Equal(42, entry.Score);
    }

    [Theory]
    [InlineData(1)]
    [InlineData(2)]
    [InlineData(3)]
    [InlineData(4)]
    [InlineData(5)]
    public async Task CompleteAndPartialChunksReloadInRankOrder(int playerCount)
    {
        var board = await CreateBoard(chunkLength: 2);
        var players = Enumerable.Range(0, playerCount).Select(_ => Player()).ToArray();
        for (int i = 0; i < players.Length; i++)
            await board.AddPlayerScore(players[i], i * 10).WaitAsync(TestSetup.Timeout);

        int chunkCount = (playerCount + 1) / 2;
        for (int i = 0; i < chunkCount; i++)
        {
            var entries = await Chunk(board, i).GetEntries().WaitAsync(TestSetup.Timeout);
            Assert.Equal(Math.Min(2, playerCount - i * 2), entries.Count);
        }
        Assert.Empty(await Chunk(board, chunkCount).GetEntries().WaitAsync(TestSetup.Timeout));

        await Cluster.Deactivate(board);
        for (int i = 0; i < chunkCount; i++)
            await Cluster.Deactivate(Chunk(board, i));

        var reloaded = await board.GetEntries().WaitAsync(TestSetup.Timeout);
        Assert.Equal(Enumerable.Range(0, playerCount).Reverse().Select(i => i * 10), reloaded.Select(x => x.Score));
        await board.AddPlayerScore(players[0], 100).WaitAsync(TestSetup.Timeout);
        await board.AddPlayerScore(players[0], 99).WaitAsync(TestSetup.Timeout);
        await Cluster.Deactivate(board);
        var updated = await board.GetEntries().WaitAsync(TestSetup.Timeout);
        Assert.Equal(playerCount, updated.Count);
        Assert.Equal(100, updated[0].Score);
        Assert.Equal(players[0].GetPrimaryKeyString(), updated[0].PlayerID);
    }

    [Fact]
    public async Task FinishingClearsEveryStoredChunkAndTheActiveBoard()
    {
        var board = await CreateBoard(chunkLength: 2);
        for (int i = 0; i < 5; i++)
            await board.AddPlayerScore(Player(), i).WaitAsync(TestSetup.Timeout);

        await board.FinishLeaderBoard().WaitAsync(TestSetup.Timeout);
        Assert.Empty(await board.GetEntries().WaitAsync(TestSetup.Timeout));
        for (int i = 0; i < 3; i++)
        {
            Assert.Empty(await Chunk(board, i).GetEntries().WaitAsync(TestSetup.Timeout));
            await Cluster.Deactivate(Chunk(board, i));
            Assert.Empty(await Chunk(board, i).GetEntries().WaitAsync(TestSetup.Timeout));
        }

        await board.Initialize("new-season", DateTime.UtcNow, DateTime.MaxValue, false).WaitAsync(TestSetup.Timeout);
        Assert.Empty(await board.GetEntries().WaitAsync(TestSetup.Timeout));
        var player = Player();
        await board.AddPlayerScore(player, 10).WaitAsync(TestSetup.Timeout);
        await board.AddPlayerScore(player, 5).WaitAsync(TestSetup.Timeout);
        await Cluster.Deactivate(board);
        Assert.Equal(5, Assert.Single(await board.GetEntries().WaitAsync(TestSetup.Timeout)).Score);
        Assert.Equal("new-season", await board.GetName().WaitAsync(TestSetup.Timeout));
    }

    [Fact]
    public async Task SortingHandlesTheWholeIntegerScoreRange()
    {
        var board = await CreateBoard();
        int[] scores = [-1, int.MaxValue, int.MinValue, 0];
        var players = scores.Select(_ => Player()).ToArray();
        for (int i = 0; i < scores.Length; i++)
            await board.AddPlayerScore(players[i], scores[i]).WaitAsync(TestSetup.Timeout);
        await Cluster.Deactivate(board);

        var top = await board.GetTopEntries(4, 0).WaitAsync(TestSetup.Timeout);
        Assert.Equal(new[] { int.MaxValue, 0, -1, int.MinValue }, top.Item2.Select(x => x.Score));
        Assert.Equal(0, await board.GetPlayerRank(players[1]).WaitAsync(TestSetup.Timeout));
        Assert.Equal(3, await board.GetPlayerRank(players[2]).WaitAsync(TestSetup.Timeout));
    }

    [Fact]
    public async Task AdminDeleteAndRecreateStartsWithNoScores()
    {
        await using var host = await AdminApiHost.StartAsync(Cluster.Client);
        var owner = await host.CreateAccountAsync();
        var game = await AdminApiHost.ReadAsync<CreatedKeyResponse>(
            await host.PostAsync("/api/v1/games", new { name = "Scores" }, owner.Token));
        string url = $"/api/v1/games/{game.Key}/leaderboards";
        Assert.Equal(HttpStatusCode.Created,
            (await host.PostAsync(url, new { name = "weekly", bestScore = true }, owner.Token)).StatusCode);
        var board = Grains.GetGrain<ILeaderBoard>(ResourcesUtility.GetLeaderBoardKey(game.Key.ToString(), "weekly"));
        await board.AddPlayerScore(Player(), 100).WaitAsync(TestSetup.Timeout);

        Assert.Equal(HttpStatusCode.NoContent, (await host.DeleteAsync(url + "/weekly", owner.Token)).StatusCode);
        Assert.Equal(HttpStatusCode.Created,
            (await host.PostAsync(url, new { name = "weekly", bestScore = true }, owner.Token)).StatusCode);
        Assert.Empty(await board.GetEntries().WaitAsync(TestSetup.Timeout));
        await Cluster.Deactivate(board);
        Assert.Empty(await board.GetEntries().WaitAsync(TestSetup.Timeout));
    }
}
