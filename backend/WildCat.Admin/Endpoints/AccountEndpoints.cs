using System.Security.Claims;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Routing;
using Microsoft.Extensions.Logging;
using Orleans;
using WildCat.Interfaces;

namespace WildCat.Admin.Endpoints;

/// <summary>
/// The caller's own profile and secret (/me), the developer-wide publisher data, and the admin
/// developer list.
/// </summary>
internal static class AccountEndpoints
{
    public const int MaxPublisherKeyLength = 128;
    public const int MaxPublisherValueLength = 65536;
    public const int MaxPageSize = 100;

    public static void Map(RouteGroupBuilder api)
    {
        var me = api.MapGroup("/me").WithTags("Me").RequireAuthorization(WildCatAdminExtensions.DeveloperPolicy);
        me.MapGet("", GetMe)
            .WithSummary("The caller's profile, including the developer secret the game server tools need.")
            .Produces<MeResponse>();
        me.MapPut("", UpdateMe)
            .WithSummary("Changes the caller's first and last name.")
            .Produces<MeResponse>()
            .ProducesProblem(StatusCodes.Status400BadRequest);
        me.MapPost("/secret", RegenerateSecret)
            .WithSummary("Replaces the developer secret; the old one stops working at once.")
            .Produces<SecretResponse>();

        var data = api.MapGroup("/publisher-data").WithTags("Publisher data").RequireAuthorization(WildCatAdminExtensions.DeveloperPolicy);
        data.MapGet("", ListPublisherData)
            .WithSummary("The caller's publisher key/value strings.")
            .Produces<List<PublisherDataEntry>>();
        data.MapPut("/{key}", PutPublisherData)
            .WithSummary("Creates or replaces one publisher value.")
            .Produces<PublisherDataEntry>()
            .ProducesProblem(StatusCodes.Status400BadRequest);
        data.MapDelete("/{key}", DeletePublisherData)
            .WithSummary("Deletes one publisher value.")
            .Produces(StatusCodes.Status204NoContent)
            .ProducesProblem(StatusCodes.Status404NotFound);

        var admin = api.MapGroup("/admin").WithTags("Admin").RequireAuthorization(WildCatAdminExtensions.AdminPolicy);
        admin.MapGet("/developers", ListDevelopers)
            .WithSummary("Registered developers ordered by email; search matches part of the email.")
            .Produces<DeveloperPage>()
            .ProducesProblem(StatusCodes.Status400BadRequest);
    }

    private static IDeveloper Caller(IGrainFactory grains, ClaimsPrincipal user) =>
        grains.GetGrain<IDeveloper>(user.GetDeveloperKey());

    private static async Task<IResult> GetMe(ClaimsPrincipal user, IGrainFactory grains, AdminAccess access)
    {
        var data = await Caller(grains, user).GetDeveloperData();
        return TypedResults.Ok(ToMe(user, data, access));
    }

    private static async Task<IResult> UpdateMe(UpdateMeRequest request, ClaimsPrincipal user, IGrainFactory grains, AdminAccess access)
    {
        var errors = new List<string>();
        Validation.MaxLength(request.FirstName, "first name", Validation.MaxNameLength, errors);
        Validation.MaxLength(request.LastName, "last name", Validation.MaxNameLength, errors);
        if (errors.Count > 0)
            return Problems.Validation(errors);

        var developer = Caller(grains, user);
        await developer.ChangeName(Validation.Clean(request.FirstName), Validation.Clean(request.LastName));
        return TypedResults.Ok(ToMe(user, await developer.GetDeveloperData(), access));
    }

    private static async Task<IResult> RegenerateSecret(ClaimsPrincipal user, IGrainFactory grains)
    {
        Guid secret = await Caller(grains, user).GenerateSecret();
        return TypedResults.Ok(new SecretResponse(secret));
    }

    private static MeResponse ToMe(ClaimsPrincipal user, DeveloperState data, AdminAccess access)
    {
        return new MeResponse(user.GetDeveloperKey(), data.Email, data.FirstName, data.LastName, access.IsAdmin(user), data.DeveloperSecret);
    }

    private static async Task<(IDeveloperStorage Storage, Guid Secret)> PublisherStorage(ClaimsPrincipal user, IGrainFactory grains)
    {
        Guid key = user.GetDeveloperKey();
        var data = await grains.GetGrain<IDeveloper>(key).GetDeveloperData();
        return (grains.GetGrain<IDeveloperStorage>(key), data.DeveloperSecret);
    }

    private static async Task<IResult> ListPublisherData(ClaimsPrincipal user, IGrainFactory grains)
    {
        var (storage, secret) = await PublisherStorage(user, grains);
        var values = await storage.GetAllStringData(secret);
        return TypedResults.Ok(values
            .OrderBy(x => x.Key, StringComparer.Ordinal)
            .Select(x => new PublisherDataEntry(x.Key, x.Value))
            .ToList());
    }

    private static async Task<IResult> PutPublisherData(string key, PublisherDataRequest request, ClaimsPrincipal user, IGrainFactory grains)
    {
        var errors = new List<string>();
        ValidatePublisherKey(key, errors);
        if (request.Value == null)
            errors.Add("The value is required.");
        Validation.MaxLength(request.Value, "value", MaxPublisherValueLength, errors);
        if (errors.Count > 0)
            return Problems.Validation(errors);

        var (storage, secret) = await PublisherStorage(user, grains);
        await storage.SetString(key, request.Value!, secret);
        return TypedResults.Ok(new PublisherDataEntry(key, request.Value!));
    }

    private static async Task<IResult> DeletePublisherData(string key, ClaimsPrincipal user, IGrainFactory grains)
    {
        var (storage, secret) = await PublisherStorage(user, grains);
        if (await storage.GetStringForDeveloper(key, secret) == null)
            return Problems.NotFound("There is no publisher value with this key.");
        await storage.RemoveString(key, secret);
        return TypedResults.NoContent();
    }

    private static void ValidatePublisherKey(string key, List<string> errors)
    {
        if (string.IsNullOrWhiteSpace(key))
            errors.Add("The key is required.");
        else if (key.Length > MaxPublisherKeyLength)
            errors.Add($"The key cannot be longer than {MaxPublisherKeyLength} characters.");
        else if (key.Any(char.IsControl))
            errors.Add("The key cannot contain control characters.");
    }

    private static async Task<IResult> ListDevelopers(IGrainFactory grains, ILoggerFactory loggers,
        [FromQuery] string? search, [FromQuery] int? page, [FromQuery] int? pageSize)
    {
        int pageNumber = page ?? 1;
        int size = pageSize ?? 25;
        var errors = new List<string>();
        if (pageNumber < 1)
            errors.Add("The page must be 1 or more.");
        Validation.Range(size, "page size", 1, MaxPageSize, errors);
        Validation.MaxLength(search, "search", Validation.MaxEmailLength, errors);
        if (errors.Count > 0)
            return Problems.Validation(errors);

        var manager = grains.GetGrain<IDeveloperManager>(WildCatAdminExtensions.DeveloperManagerKey);
        var result = await manager.SearchDevelopers(search, (int)Math.Min(int.MaxValue, (long)(pageNumber - 1) * size), size);
        var logger = loggers.CreateLogger(typeof(AccountEndpoints));
        var items = await Task.WhenAll(result.Items.Select(async entry =>
        {
            try
            {
                var data = await grains.GetGrain<IDeveloper>(entry.Key).GetDeveloperData();
                return new AdminDeveloperResponse(entry.Key, entry.Email, data.FirstName, data.LastName,
                    data.Status != DeveloperState.DeveloperStatus.NotActivated);
            }
            catch (InvalidOperationException ex)
            {
                logger.LogWarning(ex, "Developer {DeveloperKey} is in the manager's index but could not be read", entry.Key);
                return new AdminDeveloperResponse(entry.Key, entry.Email, null, null, false);
            }
        }));
        return TypedResults.Ok(new DeveloperPage(items.ToList(), result.Total));
    }
}
