using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Orleans;
using WildCat.Interfaces;

namespace WildCat.Admin;

/// <summary>
/// The game a game-scoped request works on and the developer who owns it (the caller, or another
/// developer when an admin opens someone else's game).
/// </summary>
internal sealed record GameScope(Guid GameKey, Guid OwnerKey)
{
    public string GameKeyText => GameKey.ToString();

    public static GameScope From(HttpContext context)
    {
        return context.Items[typeof(GameScope)] as GameScope
            ?? throw new InvalidOperationException("The game access filter did not run for this endpoint.");
    }
}

/// <summary>
/// Runs before every /games/{gameKey} endpoint. The caller's own games pass. Another developer's
/// game gets 403, except for admins, who may open any game. A game nobody owns gets 404.
/// </summary>
internal sealed class GameAccessFilter : IEndpointFilter
{
    public async ValueTask<object?> InvokeAsync(EndpointFilterInvocationContext context, EndpointFilterDelegate next)
    {
        var http = context.HttpContext;
        if (!Guid.TryParse(http.GetRouteValue("gameKey") as string, out var gameKey))
            return Problems.NotFound("The game was not found.");

        var grains = http.RequestServices.GetRequiredService<IGrainFactory>();
        var access = http.RequestServices.GetRequiredService<AdminAccess>();
        Guid caller = http.User.GetDeveloperKey();

        Guid owner = Guid.Empty;
        if (await grains.GetGrain<IDeveloper>(caller).IsGameValid(gameKey))
        {
            owner = caller;
        }
        else
        {
            bool isAdmin = access.IsAdmin(http.User);
            owner = await FindOwner(grains, gameKey, isAdmin, http.RequestServices.GetRequiredService<ILogger<GameAccessFilter>>());
            if (owner == Guid.Empty)
                return Problems.NotFound("The game was not found.");
            if (!isAdmin)
                return Problems.Forbidden("This game belongs to another developer.");
        }

        http.Items[typeof(GameScope)] = new GameScope(gameKey, owner);
        return await next(context);
    }

    /// <summary>
    /// Looks the owner up in the manager's index. Admins also get a scan over every developer for
    /// games the index does not know (games created before the admin API, such as the seeded
    /// development game), and the result is recorded for next time.
    /// </summary>
    private static async Task<Guid> FindOwner(IGrainFactory grains, Guid gameKey, bool isAdmin, ILogger logger)
    {
        var manager = grains.GetGrain<IDeveloperManager>(WildCatAdminExtensions.DeveloperManagerKey);
        Guid owner = await manager.GetGameOwner(gameKey);
        if (owner != Guid.Empty)
        {
            if (await OwnsGame(grains, owner, gameKey, logger))
                return owner;
            await manager.RemoveGameOwner(gameKey);
        }
        if (!isAdmin)
            return Guid.Empty;

        foreach (var chunk in (await manager.GetDevelopers()).Chunk(32))
        {
            var checks = chunk.Select(async developer =>
                (Key: developer.GetPrimaryKey(), Owns: await OwnsGame(grains, developer.GetPrimaryKey(), gameKey, logger)));
            var found = (await Task.WhenAll(checks)).FirstOrDefault(x => x.Owns);
            if (found.Owns)
            {
                await manager.SetGameOwner(gameKey, found.Key);
                return found.Key;
            }
        }
        return Guid.Empty;
    }

    private static async Task<bool> OwnsGame(IGrainFactory grains, Guid developerKey, Guid gameKey, ILogger logger)
    {
        try
        {
            return await grains.GetGrain<IDeveloper>(developerKey).IsGameValid(gameKey);
        }
        catch (InvalidOperationException ex)
        {
            // A key in the manager's index whose developer grain was never created.
            logger.LogWarning(ex, "Developer {DeveloperKey} could not be checked for game {GameKey}", developerKey, gameKey);
            return false;
        }
    }
}
