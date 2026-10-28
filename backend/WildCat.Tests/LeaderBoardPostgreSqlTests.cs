using Orleans;
using WildCat.Interfaces;
using Xunit;

namespace WildCat.Tests;

public class LeaderBoardPostgreSqlTests(PostgreSqlStorageFixture fixture) : IClassFixture<PostgreSqlStorageFixture>
{
    [PostgreSqlFact]
    public async Task ScoresAndDeletionSurviveReactivationWithPostgreSqlStorage()
    {
        var grains = fixture.Cluster.Client;
        var board = grains.GetGrain<ILeaderBoard>(TestSetup.UniqueId("pg-board"));
        var player = grains.GetGrain<IPlayer>(TestSetup.UniqueId("pg-player"));
        var chunk = grains.GetGrain<ILeaderBoardStorage>(ResourcesUtility.GetLeaderBoardChunkKey(board.GetPrimaryKeyString(), 0));
        await board.Initialize("weekly", DateTime.UtcNow, DateTime.MaxValue, true).WaitAsync(TestSetup.Timeout);
        await board.AddPlayerScore(player, 42).WaitAsync(TestSetup.Timeout);
        await fixture.Cluster.Deactivate(board);
        await fixture.Cluster.Deactivate(chunk);
        Assert.Equal(42, Assert.Single(await board.GetEntries().WaitAsync(TestSetup.Timeout)).Score);

        await board.FinishLeaderBoard().WaitAsync(TestSetup.Timeout);
        await fixture.Cluster.Deactivate(board);
        await fixture.Cluster.Deactivate(chunk);
        Assert.Empty(await chunk.GetEntries().WaitAsync(TestSetup.Timeout));
        await board.Initialize("new-season", DateTime.UtcNow, DateTime.MaxValue, true).WaitAsync(TestSetup.Timeout);
        Assert.Empty(await board.GetEntries().WaitAsync(TestSetup.Timeout));
        await board.AddPlayerScore(player, 1).WaitAsync(TestSetup.Timeout);
        await fixture.Cluster.Deactivate(board);
        Assert.Equal(1, Assert.Single(await board.GetEntries().WaitAsync(TestSetup.Timeout)).Score);
    }
}
