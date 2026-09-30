using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using Orleans;
using WildCat.Interfaces;

namespace WildCat.Admin.Endpoints;

/// <summary>
/// A game's virtual goods. The game's item resource manager lists them; each one is an Item grain
/// keyed "{gameKey}_I_{name}". Prices are paid in other items of the same game.
/// </summary>
internal static class ItemEndpoints
{
    public const int MaxDescriptionLength = 2000;
    public const int MaxMetaDataEntries = 50;
    public const int MaxMetaDataKeyLength = 64;
    public const int MaxMetaDataValueLength = 1000;
    public const int MaxPriceEntries = 20;
    public static readonly TimeSpan MaxDuration = TimeSpan.FromDays(3650);

    public static void Map(RouteGroupBuilder game)
    {
        var items = game.MapGroup("/items").WithTags("Items");
        items.MapGet("", List)
            .WithSummary("The game's items ordered by name.")
            .Produces<List<ItemResponse>>();
        items.MapPut("/{name}", Put)
            .WithSummary("Creates or replaces an item. Durations are TimeSpan strings such as \"1.02:03:04\"; price items must already exist.")
            .Produces<ItemResponse>()
            .ProducesProblem(StatusCodes.Status400BadRequest);
        items.MapDelete("/{name}", Delete)
            .WithSummary("Removes an item from the game's catalog. Inventories that hold it keep their stacks.")
            .Produces(StatusCodes.Status204NoContent);
    }

    private static IResourceManager Manager(IGrainFactory grains, GameScope scope) =>
        grains.GetGrain<IResourceManager>(ResourcesUtility.GetItemManagerName(scope.GameKeyText));

    private static async Task<IResult> List(HttpContext context, IGrainFactory grains)
    {
        var scope = GameScope.From(context);
        var resources = await Manager(grains, scope).GetResources(ResourcesUtility.GetGameItemsClassName());
        var items = await Task.WhenAll(resources.Select(async resource =>
            ToResponse(scope, await grains.GetGrain<IItem>(resource.GetPrimaryKeyString()).GetItemInfo())));
        return TypedResults.Ok(items.OrderBy(x => x.Name, StringComparer.Ordinal).ToList());
    }

    private static async Task<IResult> Put(string name, ItemRequest request, HttpContext context, IGrainFactory grains)
    {
        var scope = GameScope.From(context);
        var manager = Manager(grains, scope);
        var errors = new List<string>();
        Validation.ResourceName(name, "item name", errors);
        Validation.MaxLength(request.Description, "description", MaxDescriptionLength, errors);
        var effectTime = request.EffectTime ?? TimeSpan.Zero;
        var regenerationPeriod = request.RegenerationPeriod ?? TimeSpan.Zero;
        Duration(effectTime, "effect time", errors);
        Duration(regenerationPeriod, "regeneration period", errors);
        Validation.Range(request.MaxAllowedToHave, "maximum allowed to have", 0, int.MaxValue, errors);
        Validation.Range(request.RegenerationAmount, "regeneration amount", 0, int.MaxValue, errors);

        var metaData = request.MetaData ?? new Dictionary<string, string>();
        if (metaData.Count > MaxMetaDataEntries)
            errors.Add($"An item can have at most {MaxMetaDataEntries} metadata entries.");
        foreach (var entry in metaData)
        {
            if (string.IsNullOrWhiteSpace(entry.Key) || entry.Key.Length > MaxMetaDataKeyLength)
                errors.Add($"Metadata names must be 1 to {MaxMetaDataKeyLength} characters.");
            if (entry.Value == null || entry.Value.Length > MaxMetaDataValueLength)
                errors.Add($"The metadata value of '{entry.Key}' must be a string of at most {MaxMetaDataValueLength} characters.");
        }

        var priceRows = request.Price ?? new List<ItemPrice>();
        if (priceRows.Count > MaxPriceEntries)
            errors.Add($"An item can have at most {MaxPriceEntries} price entries.");
        var price = new List<PlayerInventoryState.InInventoryItemState>();
        foreach (var row in priceRows)
        {
            if (!Validation.IsResourceName(row.Item))
            {
                errors.Add("Every price entry needs the name of an item.");
                continue;
            }
            if (row.Item == name)
                errors.Add("An item cannot be priced in itself.");
            else if (!await manager.HasResource(ResourcesUtility.GetItemKey(scope.GameKeyText, row.Item!)))
                errors.Add($"The price item '{row.Item}' does not exist in this game.");
            if (row.Count < 1)
                errors.Add($"The price count of '{row.Item}' must be at least 1.");
            price.Add(new PlayerInventoryState.InInventoryItemState
            {
                Item = grains.GetGrain<IInventoryItem>(ResourcesUtility.GetItemKey(scope.GameKeyText, row.Item!), ResourcesUtility.GetGameItemsClassName()),
                Count = row.Count
            });
        }
        if (priceRows.Select(x => x.Item).Distinct(StringComparer.Ordinal).Count() != priceRows.Count)
            errors.Add("Each item can appear only once in the price.");
        if (errors.Count > 0)
            return Problems.Validation(errors.Distinct().ToList());

        string key = ResourcesUtility.GetItemKey(scope.GameKeyText, name);
        var item = grains.GetGrain<IItem>(key);
        string? description = Validation.Clean(request.Description);
        var metaCopy = new Dictionary<string, string>(metaData);
        if (await manager.HasResource(key))
        {
            await item.SetItemProperties(name, description, metaCopy, request.MaxAllowedToHave, request.Stackable,
                request.AutomaticallyConsumed, effectTime, request.IsConsumable, request.IsRegenerating,
                request.RegenerationAmount, regenerationPeriod, price);
        }
        else
        {
            await item.Initialize(name, description, metaCopy, request.MaxAllowedToHave, request.Stackable,
                request.AutomaticallyConsumed, effectTime, request.IsConsumable, request.IsRegenerating,
                request.RegenerationAmount, regenerationPeriod, price);
            await manager.AddResource(item);
        }
        return TypedResults.Ok(ToResponse(scope, await item.GetItemInfo()));
    }

    private static async Task<IResult> Delete(string name, HttpContext context, IGrainFactory grains)
    {
        var scope = GameScope.From(context);
        string key = ResourcesUtility.GetItemKey(scope.GameKeyText, name);
        if (!await Manager(grains, scope).RemoveResourceByKey(key))
            return Problems.NotFound("The game has no item with this name.");
        return TypedResults.NoContent();
    }

    private static void Duration(TimeSpan value, string field, List<string> errors)
    {
        if (value < TimeSpan.Zero || value > MaxDuration)
            errors.Add($"The {field} must be between 00:00:00 and {MaxDuration.Days}.00:00:00.");
    }

    private static ItemResponse ToResponse(GameScope scope, ItemState state)
    {
        var price = (state.Price ?? new List<PlayerInventoryState.InInventoryItemState>())
            .Select(x => new ItemPrice(PriceItemName(scope, x.Item), x.Count))
            .ToList();
        return new ItemResponse(state.Name, state.Description, state.EffectTime, state.Stackable,
            state.MaxAllowedToHave, state.IsConsumable, state.AutomaticallyConsumed, state.IsRegenerating,
            state.RegenerationAmount, state.RegenerationPeriod,
            state.MetaData ?? new Dictionary<string, string>(), price);
    }

    /// <summary>
    /// The item name inside a price reference, taken from its grain key ("{gameKey}_I_{name}", or
    /// "_P_" for a package) so no grain call is needed.
    /// </summary>
    private static string? PriceItemName(GameScope scope, IInventoryItem? item)
    {
        if (item == null)
            return null;
        string key = item.GetPrimaryKeyString();
        foreach (var prefix in new[] { ResourcesUtility.GetItemKey(scope.GameKeyText, ""), ResourcesUtility.GetPackageKey(scope.GameKeyText, "") })
        {
            if (key.StartsWith(prefix, StringComparison.Ordinal))
                return key.Substring(prefix.Length);
        }
        return key;
    }
}
