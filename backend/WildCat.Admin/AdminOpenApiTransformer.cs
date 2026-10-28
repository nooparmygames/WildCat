using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.OpenApi;
using Microsoft.OpenApi;

namespace WildCat.Admin;

/// <summary>
/// Names the OpenAPI document, describes the bearer token scheme and marks the operations that need
/// a token, so OpenAPI tools can call the API after a login.
/// </summary>
internal sealed class AdminOpenApiTransformer : IOpenApiDocumentTransformer, IOpenApiOperationTransformer
{
    private const string SchemeName = "Bearer";

    public Task TransformAsync(OpenApiDocument document, OpenApiDocumentTransformerContext context, CancellationToken cancellationToken)
    {
        document.Info ??= new OpenApiInfo();
        document.Info.Title = "WildCat admin API";
        document.Info.Description =
            "The REST API behind the WildCat developer panel. Log in with POST /api/v1/auth/login and send the token " +
            "as 'Authorization: Bearer <token>'. Errors are RFC 7807 problem details with a readable 'detail'.";
        document.Components ??= new OpenApiComponents();
        document.Components.SecuritySchemes ??= new Dictionary<string, IOpenApiSecurityScheme>();
        document.Components.SecuritySchemes[SchemeName] = new OpenApiSecurityScheme
        {
            Type = SecuritySchemeType.Http,
            Scheme = "bearer",
            BearerFormat = "JWT",
            Description = "The token from POST /api/v1/auth/login, valid for up to eight hours. Changing or resetting the password ends all admin sessions.",
        };
        return Task.CompletedTask;
    }

    public Task TransformAsync(OpenApiOperation operation, OpenApiOperationTransformerContext context, CancellationToken cancellationToken)
    {
        // The game endpoints read {gameKey} in a filter rather than binding it, so the generator
        // does not list it; every route parameter must be declared, so add the missing ones.
        foreach (string name in RouteParameters(context.Description.RelativePath))
        {
            operation.Parameters ??= new List<IOpenApiParameter>();
            if (operation.Parameters.Any(x => x.In == ParameterLocation.Path && x.Name == name))
                continue;
            operation.Parameters.Add(new OpenApiParameter
            {
                Name = name,
                In = ParameterLocation.Path,
                Required = true,
                Schema = new OpenApiSchema
                {
                    Type = JsonSchemaType.String,
                    Format = name == "gameKey" ? "uuid" : null,
                },
            });
        }

        var metadata = context.Description.ActionDescriptor.EndpointMetadata;
        if (metadata.OfType<IAuthorizeData>().Any() && !metadata.OfType<IAllowAnonymous>().Any())
        {
            operation.Security ??= new List<OpenApiSecurityRequirement>();
            operation.Security.Add(new OpenApiSecurityRequirement
            {
                [new OpenApiSecuritySchemeReference(SchemeName, context.Document)] = [],
            });
        }
        return Task.CompletedTask;
    }

    private static IEnumerable<string> RouteParameters(string? relativePath)
    {
        foreach (var segment in (relativePath ?? "").Split('/'))
        {
            if (segment.StartsWith('{') && segment.EndsWith('}'))
                yield return segment.Trim('{', '}').TrimStart('*').Split(':', '=', '?')[0];
        }
    }
}
