using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using Orleans;
using WildCat.Interfaces;

namespace WildCat.Admin.Endpoints;

/// <summary>
/// A game's leaderboards. The game's leaderboard resource manager lists them; each one is a
/// LeaderBoard grain keyed "{gameKey}_L_{name}".
/// </summary>
internal static class LeaderboardEndpoints
{
    public static void Map(RouteGroupBuilder game)
    {
        var boards = game.MapGroup("/leaderboards").WithTags("Leaderboards");
        boards.MapGet("", List)
            .WithSummary("The game's leaderboards ordered by name.")
            .Produces<List<LeaderboardResponse>>();
        boards.MapPost("", Create)
            .WithSummary("Creates a leaderboard. bestScore true keeps each player's best score, false the last one.")
            .Produces<LeaderboardResponse>(StatusCodes.Status201Created)
            .ProducesProblem(StatusCodes.Status400BadRequest)
            .ProducesProblem(StatusCodes.Status409Conflict);
        boards.MapDelete("/{name}", Delete)
            .WithSummary("Removes a leaderboard from the game and clears its stored scores.")
            .Produces(StatusCodes.Status204NoContent);
    }

    private static IResourceManager Manager(IGrainFactory grains, GameScope scope) =>
        grains.GetGrain<IResourceManager>(ResourcesUtility.GetLeaderboardResourceManagerName(scope.GameKeyText));

    private static async Task<IResult> List(HttpContext context, IGrainFactory grains)
    {
        var scope = GameScope.From(context);
        var resources = await Manager(grains, scope).GetResources(ResourcesUtility.GetLeaderBoardClassName());
        var boards = await Task.WhenAll(resources.Select(async resource =>
        {
            string key = resource.GetPrimaryKeyString();
            var info = await grains.GetGrain<ILeaderBoard>(key).GetInfo();
            return new LeaderboardResponse(info.Name, key, info.AcceptBetterScoresOnly);
        }));
        return TypedResults.Ok(boards.OrderBy(x => x.Name, StringComparer.Ordinal).ToList());
    }

    private static async Task<IResult> Create(CreateLeaderboardRequest request, HttpContext context, IGrainFactory grains)
    {
        var errors = new List<string>();
        Validation.ResourceName(request.Name, "name", errors);
        if (errors.Count > 0)
            return Problems.Validation(errors);

        var scope = GameScope.From(context);
        var manager = Manager(grains, scope);
        string key = ResourcesUtility.GetLeaderBoardKey(scope.GameKeyText, request.Name!);
        if (await manager.HasResource(key))
            return Problems.Conflict("The game already has a leaderboard with this name.");

        var board = grains.GetGrain<ILeaderBoard>(key);
        await board.Initialize(request.Name!, DateTime.UtcNow, DateTime.MaxValue, request.BestScore);
        await manager.AddResource(board);
        var response = new LeaderboardResponse(request.Name!, key, request.BestScore);
        return TypedResults.Created($"{WildCatAdminExtensions.ApiPrefix}/games/{scope.GameKey}/leaderboards/{Uri.EscapeDataString(request.Name!)}", response);
    }

    private static async Task<IResult> Delete(string name, HttpContext context, IGrainFactory grains)
    {
        var scope = GameScope.From(context);
        string key = ResourcesUtility.GetLeaderBoardKey(scope.GameKeyText, name);
        if (!await Manager(grains, scope).RemoveResourceByKey(key))
            return Problems.NotFound("The game has no leaderboard with this name.");
        await grains.GetGrain<ILeaderBoard>(key).FinishLeaderBoard();
        return TypedResults.NoContent();
    }
}
