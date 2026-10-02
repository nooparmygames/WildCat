using System.Security.Claims;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using Orleans;
using WildCat.Interfaces;

namespace WildCat.Admin.Endpoints;

/// <summary>
/// The caller's games, and the /games/{gameKey} group that every per-game endpoint hangs off.
/// </summary>
internal static class GameEndpoints
{
    public const int MaxDescriptionLength = 2000;
    public const int MaxShortTextLength = 100;
    public const int MaxWebsiteLength = 200;

    public static void Map(RouteGroupBuilder api)
    {
        var games = api.MapGroup("/games").WithTags("Games").RequireAuthorization(WildCatAdminExtensions.DeveloperPolicy);
        games.MapGet("", ListGames)
            .WithSummary("The caller's games ordered by name.")
            .Produces<List<GameResponse>>();
        games.MapPost("", CreateGame)
            .WithSummary("Creates a game and returns its key.")
            .Produces<CreatedKeyResponse>(StatusCodes.Status201Created)
            .ProducesProblem(StatusCodes.Status400BadRequest)
            .ProducesProblem(StatusCodes.Status409Conflict);

        var game = games.MapGroup("/{gameKey:guid}")
            .AddEndpointFilter<GameAccessFilter>()
            .ProducesProblem(StatusCodes.Status403Forbidden)
            .ProducesProblem(StatusCodes.Status404NotFound);
        game.MapGet("", GetGame)
            .WithSummary("One game.")
            .Produces<GameResponse>();
        game.MapPut("", UpdateGame)
            .WithSummary("Replaces a game's details.")
            .Produces<GameResponse>()
            .ProducesProblem(StatusCodes.Status400BadRequest)
            .ProducesProblem(StatusCodes.Status409Conflict);
        game.MapDelete("", DeleteGame)
            .WithSummary("Deletes a game. Its leaderboards, items, events and game modes are left in storage.")
            .Produces(StatusCodes.Status204NoContent);

        LeaderboardEndpoints.Map(game);
        ItemEndpoints.Map(game);
        ScriptEventEndpoints.Map(game);
        GameModeEndpoints.Map(game);
    }

    private static async Task<IResult> ListGames(ClaimsPrincipal user, IGrainFactory grains)
    {
        var games = await grains.GetGrain<IDeveloper>(user.GetDeveloperKey()).GetGames();
        return TypedResults.Ok(games
            .OrderBy(x => x.Name, StringComparer.OrdinalIgnoreCase)
            .Select(ToResponse)
            .ToList());
    }

    private static async Task<IResult> CreateGame(GameRequest request, ClaimsPrincipal user, IGrainFactory grains)
    {
        var errors = Validate(request);
        if (errors.Count > 0)
            return Problems.Validation(errors);

        Guid developerKey = user.GetDeveloperKey();
        var developer = grains.GetGrain<IDeveloper>(developerKey);
        Guid key;
        try
        {
            key = await developer.AddGame(ToGameInfo(request, image: null));
        }
        catch (InvalidOperationException)
        {
            return Problems.Conflict("You already have a game with this name.");
        }
        await grains.GetGrain<IDeveloperManager>(WildCatAdminExtensions.DeveloperManagerKey).SetGameOwner(key, developerKey);
        return TypedResults.Created($"{WildCatAdminExtensions.ApiPrefix}/games/{key}", new CreatedKeyResponse(key));
    }

    private static async Task<IResult> GetGame(HttpContext context, IGrainFactory grains)
    {
        var scope = GameScope.From(context);
        var game = await FindGame(grains, scope);
        return game == null ? Problems.NotFound("The game was not found.") : TypedResults.Ok(ToResponse(game));
    }

    private static async Task<IResult> UpdateGame(GameRequest request, HttpContext context, IGrainFactory grains)
    {
        var errors = Validate(request);
        if (errors.Count > 0)
            return Problems.Validation(errors);

        var scope = GameScope.From(context);
        var owner = grains.GetGrain<IDeveloper>(scope.OwnerKey);
        var games = (await owner.GetGames()).ToList();
        var existing = games.FirstOrDefault(x => x.Key == scope.GameKey);
        if (existing == null)
            return Problems.NotFound("The game was not found.");
        string name = request.Name!.Trim();
        if (games.Any(x => x.Key != scope.GameKey && x.Name == name))
            return Problems.Conflict("You already have a game with this name.");

        var updated = ToGameInfo(request, existing.Image);
        await owner.EditGame(scope.GameKey, updated);
        updated.Key = scope.GameKey;
        return TypedResults.Ok(ToResponse(updated));
    }

    private static async Task<IResult> DeleteGame(HttpContext context, IGrainFactory grains)
    {
        var scope = GameScope.From(context);
        try
        {
            await grains.GetGrain<IDeveloper>(scope.OwnerKey).RemoveGame(scope.GameKey);
        }
        catch (InvalidOperationException)
        {
            return Problems.NotFound("The game was not found.");
        }
        await grains.GetGrain<IDeveloperManager>(WildCatAdminExtensions.DeveloperManagerKey).RemoveGameOwner(scope.GameKey);
        return TypedResults.NoContent();
    }

    private static async Task<GameInfo?> FindGame(IGrainFactory grains, GameScope scope)
    {
        var games = await grains.GetGrain<IDeveloper>(scope.OwnerKey).GetGames();
        return games.FirstOrDefault(x => x.Key == scope.GameKey);
    }

    private static List<string> Validate(GameRequest request)
    {
        var errors = new List<string>();
        Validation.Required(request.Name, "name", errors);
        Validation.MaxLength(request.Name?.Trim(), "name", MaxShortTextLength, errors);
        Validation.MaxLength(request.Description, "description", MaxDescriptionLength, errors);
        Validation.MaxLength(request.StudioName, "studio name", MaxShortTextLength, errors);
        Validation.MaxLength(request.Website, "website", MaxWebsiteLength, errors);
        Validation.MaxLength(request.Platform, "platform", MaxShortTextLength, errors);
        if (!string.IsNullOrWhiteSpace(request.Website)
            && !(Uri.TryCreate(request.Website.Trim(), UriKind.Absolute, out var site) && (site.Scheme == Uri.UriSchemeHttp || site.Scheme == Uri.UriSchemeHttps)))
            errors.Add("The website must be an absolute http or https address.");
        Validation.Range(request.Genre, "genre", 0, Enum.GetValues<GameInfo.GameGenre>().Length - 1, errors);
        Validation.Range(request.Region, "region", 0, Enum.GetValues<GameInfo.GameRegion>().Length - 1, errors);
        Validation.Range(request.AgeRating, "age rating", 0, Enum.GetValues<GameInfo.GameAgeRating>().Length - 1, errors);
        return errors;
    }

    private static GameInfo ToGameInfo(GameRequest request, byte[]? image)
    {
        return new GameInfo
        {
            Name = request.Name!.Trim(),
            Description = Validation.Clean(request.Description),
            StudioName = Validation.Clean(request.StudioName),
            Website = Validation.Clean(request.Website),
            Platform = Validation.Clean(request.Platform),
            Genre = (GameInfo.GameGenre)request.Genre,
            Region = (GameInfo.GameRegion)request.Region,
            AgeRating = (GameInfo.GameAgeRating)request.AgeRating,
            Image = image,
        };
    }

    private static GameResponse ToResponse(GameInfo game)
    {
        return new GameResponse(game.Key, game.Name, game.Description, game.StudioName, game.Website,
            (int)game.Genre, (int)game.Region, game.Platform, (int)game.AgeRating);
    }
}
