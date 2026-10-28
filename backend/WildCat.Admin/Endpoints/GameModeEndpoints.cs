using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using Orleans;
using WildCat.Interfaces;

namespace WildCat.Admin.Endpoints;

/// <summary>
/// A game's matchmaking modes, stored in the GameModeManager grain keyed by the game key. The
/// criteria rows are a relaxation schedule: from startTimeInSeconds after a player joins the queue,
/// a match within acceptableThreshold skill points is accepted, and isMinPlayersEnough lets it
/// start with only minPlayers.
/// </summary>
internal static class GameModeEndpoints
{
    public const int MinPlayersLimit = 2;
    public const int MaxPlayersLimit = 1000;
    public const int MaxCriteria = 20;
    public const int MaxStartTimeSeconds = 24 * 3600;

    public static void Map(RouteGroupBuilder game)
    {
        var modes = game.MapGroup("/game-modes").WithTags("Game modes");
        modes.MapGet("", List)
            .WithSummary("The game's matchmaking modes ordered by name.")
            .Produces<List<GameModeResponse>>();
        modes.MapPut("/{name}", Put)
            .WithSummary("Creates or replaces a game mode. The first criteria row must start at 0 seconds and later rows later.")
            .Produces<GameModeResponse>()
            .ProducesProblem(StatusCodes.Status400BadRequest);
        modes.MapDelete("/{name}", Delete)
            .WithSummary("Deletes a game mode. Its queue uses the default rules on the next matching pass.")
            .Produces(StatusCodes.Status204NoContent);
    }

    private static IGameModeManager Manager(IGrainFactory grains, GameScope scope) =>
        grains.GetGrain<IGameModeManager>(scope.GameKey);

    private static async Task<IResult> List(HttpContext context, IGrainFactory grains)
    {
        var state = await Manager(grains, GameScope.From(context)).GetAllGameModes();
        return TypedResults.Ok((state.GameModes ?? new List<GameMode>())
            .OrderBy(x => x.Name, StringComparer.Ordinal)
            .Select(ToResponse)
            .ToList());
    }

    private static async Task<IResult> Put(string name, GameModeRequest request, HttpContext context, IGrainFactory grains)
    {
        var errors = new List<string>();
        Validation.ResourceName(name, "game mode name", errors);
        Validation.Range(request.MinPlayers, "minimum player count", MinPlayersLimit, MaxPlayersLimit, errors);
        Validation.Range(request.MaxPlayers, "maximum player count", MinPlayersLimit, MaxPlayersLimit, errors);
        if (request.MaxPlayers < request.MinPlayers)
            errors.Add("The maximum player count cannot be less than the minimum.");

        var criteria = request.Criteria ?? new List<MatchmakingCriterionDto>();
        if (criteria.Count == 0)
            errors.Add("A game mode needs at least one criteria row.");
        else if (criteria[0].StartTimeInSeconds != 0)
            errors.Add("The first criteria row must start at 0 seconds.");
        if (criteria.Count > MaxCriteria)
            errors.Add($"A game mode can have at most {MaxCriteria} criteria rows.");
        for (int i = 0; i < criteria.Count; i++)
        {
            if (criteria[i].AcceptableThreshold < 0)
                errors.Add($"Row {i + 1}: the acceptable threshold cannot be negative.");
            if (criteria[i].StartTimeInSeconds < 0 || criteria[i].StartTimeInSeconds > MaxStartTimeSeconds)
                errors.Add($"Row {i + 1}: the start time must be between 0 and {MaxStartTimeSeconds} seconds.");
            if (i > 0 && criteria[i].StartTimeInSeconds <= criteria[i - 1].StartTimeInSeconds)
                errors.Add($"Row {i + 1}: each row must start later than the row before it.");
        }
        if (errors.Count > 0)
            return Problems.Validation(errors);

        var manager = Manager(grains, GameScope.From(context));
        var existing = await manager.GetGameModeData(name);
        var mode = new GameMode
        {
            Name = name,
            MinPlayers = request.MinPlayers,
            MaxPlayers = request.MaxPlayers,
            FinishGameWithOneDisconnect = request.FinishGameWithOneDisconnect ?? existing?.FinishGameWithOneDisconnect ?? true,
            Script = existing?.Script,
            Criterias = criteria.Select(x => new GameMode.MatchMakingCriteria
            {
                IsMinPlayersEnough = x.IsMinPlayersEnough,
                AcceptableThreshold = x.AcceptableThreshold,
                StartTimeInSeconds = x.StartTimeInSeconds,
            }).ToList(),
        };
        await manager.AddOrUpdateGameMode(mode);
        return TypedResults.Ok(ToResponse(mode));
    }

    private static async Task<IResult> Delete(string name, HttpContext context, IGrainFactory grains)
    {
        var manager = Manager(grains, GameScope.From(context));
        if (await manager.GetGameModeData(name) == null)
            return Problems.NotFound("The game has no game mode with this name.");
        await manager.Remove(name);
        return TypedResults.NoContent();
    }

    private static GameModeResponse ToResponse(GameMode mode)
    {
        return new GameModeResponse(mode.Name, mode.MinPlayers, mode.MaxPlayers, mode.FinishGameWithOneDisconnect,
            (mode.Criterias ?? new List<GameMode.MatchMakingCriteria>())
                .Select(x => new MatchmakingCriterionDto(x.IsMinPlayersEnough, x.AcceptableThreshold, x.StartTimeInSeconds))
                .ToList());
    }
}
