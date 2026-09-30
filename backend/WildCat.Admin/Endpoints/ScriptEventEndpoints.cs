using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using Microsoft.Extensions.Logging;
using Orleans;
using WildCat.Interfaces;

namespace WildCat.Admin.Endpoints;

/// <summary>
/// A game's server-side script events. The game's event resource manager lists them; each one is a
/// ScriptEventCompiler grain keyed "{gameKey}_G_{name}" that validates and compiles the C# method.
/// DELETE takes the event off the list and deletes the grain's state, so game clients that still
/// call it get an error, as for an event that never existed.
/// </summary>
internal static class ScriptEventEndpoints
{
    public const int MaxScriptLength = 100_000;
    public const int MaxParameters = 16;

    public static void Map(RouteGroupBuilder game)
    {
        var events = game.MapGroup("/events").WithTags("Script events");
        events.MapGet("", List)
            .WithSummary("The game's script events ordered by name.")
            .Produces<List<ScriptEventResponse>>();
        events.MapPut("/{name}", Put)
            .WithSummary("Creates or replaces a script event. The script must declare a method named after the event. " +
                "Parameter types are numbers: 0 Bool, 1 Byte, 2 Short, 3 Int, 4 Long, 5 Float, 6 Double, 7 String, 8 ByteArray.")
            .Produces<ScriptEventResponse>()
            .ProducesProblem(StatusCodes.Status400BadRequest);
        events.MapDelete("/{name}", Delete)
            .WithSummary("Deletes a script event. Game clients that still run it get an error.")
            .Produces(StatusCodes.Status204NoContent)
            .ProducesProblem(StatusCodes.Status404NotFound);
    }

    private static IResourceManager Manager(IGrainFactory grains, GameScope scope) =>
        grains.GetGrain<IResourceManager>(ResourcesUtility.GetEventScriptResourceManagerName(scope.GameKeyText));

    private static async Task<IResult> List(HttpContext context, IGrainFactory grains, ILoggerFactory loggers)
    {
        var scope = GameScope.From(context);
        var resources = await Manager(grains, scope).GetResources(ResourcesUtility.GetEventScriptClassName());
        var logger = loggers.CreateLogger(typeof(ScriptEventEndpoints));
        var events = await Task.WhenAll(resources.Select(async resource =>
        {
            string key = resource.GetPrimaryKeyString();
            try
            {
                return ToResponse(await grains.GetGrain<IScriptEventCompiler>(key).GetInfo());
            }
            catch (Exception ex) when (ex is InvalidOperationException or ArgumentException)
            {
                // The compiler grain refuses to activate when its stored script no longer validates.
                // List the event anyway so the developer can see it and save a fixed script.
                logger.LogWarning(ex, "Script event {Key} could not be loaded", key);
                string name = key.Substring(key.IndexOf("_G_", StringComparison.Ordinal) + 3);
                return new ScriptEventResponse(name, new List<EventParameterDto>(), "");
            }
        }));
        return TypedResults.Ok(events.OrderBy(x => x.Name, StringComparer.Ordinal).ToList());
    }

    private static async Task<IResult> Put(string name, ScriptEventRequest request, HttpContext context, IGrainFactory grains)
    {
        var errors = new List<string>();
        if (!Validation.IsIdentifier(name))
            errors.Add("The event name must be a C# identifier of at most 64 characters.");
        if (string.IsNullOrWhiteSpace(request.Script))
            errors.Add("The script is required.");
        Validation.MaxLength(request.Script, "script", MaxScriptLength, errors);
        var parameters = ToParameters(request.Parameters, errors);
        if (errors.Count > 0)
            return Problems.Validation(errors);

        var scope = GameScope.From(context);
        var manager = Manager(grains, scope);
        string key = ResourcesUtility.GetEventScriptKey(scope.GameKeyText, name);
        var compiler = grains.GetGrain<IScriptEventCompiler>(key);
        bool exists = await manager.HasResource(key);
        try
        {
            if (exists)
            {
                // Validate against the new parameter list first, so a rejected script never
                // replaces the stored one. (Validate would check against the stored parameters and
                // reject every change to the list.)
                var validation = await compiler.ValidateWith(parameters, request.Script!);
                if (!validation.IsValid)
                    return ScriptRejected(validation);
                await compiler.Edit(parameters, request.Script!);
            }
            else
            {
                await compiler.Initialize(name, parameters, request.Script!);
                await manager.AddResource(compiler);
            }
        }
        catch (InvalidOperationException)
        {
            // Initialize and Edit only say that the script is not valid, so ask the compiler for
            // the reasons.
            return ScriptRejected(await compiler.ValidateWith(parameters, request.Script!));
        }
        return TypedResults.Ok(ToResponse(await compiler.GetInfo()));
    }

    private static async Task<IResult> Delete(string name, HttpContext context, IGrainFactory grains)
    {
        var scope = GameScope.From(context);
        string key = ResourcesUtility.GetEventScriptKey(scope.GameKeyText, name);
        if (!await Manager(grains, scope).RemoveResourceByKey(key))
            return Problems.NotFound("The game has no script event with this name.");
        await grains.GetGrain<IScriptEventCompiler>(key).Delete();
        return TypedResults.NoContent();
    }

    private static IResult ScriptRejected(ValidationResult validation)
    {
        string message = string.IsNullOrWhiteSpace(validation.ErrorMsg) ? "The script is not valid." : validation.ErrorMsg;
        return Problems.Validation(message);
    }

    private static List<EventParameter> ToParameters(List<EventParameterDto>? rows, List<string> errors)
    {
        var parameters = new List<EventParameter>();
        rows ??= new List<EventParameterDto>();
        if (rows.Count > MaxParameters)
            errors.Add($"An event can have at most {MaxParameters} parameters.");
        foreach (var row in rows)
        {
            if (!Validation.IsIdentifier(row.Name))
                errors.Add($"The parameter name '{row.Name}' must be a C# identifier of at most 64 characters.");
            var type = (EventParameter.ParamType)row.Type;
            if (!Enum.IsDefined(type))
                errors.Add($"The parameter type {row.Type} of '{row.Name}' is not between 0 and {Enum.GetValues<EventParameter.ParamType>().Length - 1}.");
            else
                parameters.Add(new EventParameter { Name = row.Name, Type = type });
        }
        if (rows.Select(x => x.Name).Distinct(StringComparer.Ordinal).Count() != rows.Count)
            errors.Add("Parameter names must be unique.");
        return parameters;
    }

    private static ScriptEventResponse ToResponse(ScriptEventState state)
    {
        var parameters = (state.Parameters ?? new List<EventParameter>())
            .Select(x => new EventParameterDto(x.Name, (int)x.Type))
            .ToList();
        return new ScriptEventResponse(state.Name, parameters, state.Script ?? "");
    }
}
