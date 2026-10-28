using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Cors.Infrastructure;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Routing;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Options;
using Orleans;
using WildCat.Admin.Endpoints;
using WildCat.Interfaces;

namespace WildCat.Admin;

/// <summary>
/// Hosting entry points for the admin REST API that the developer panel uses. It runs inside the
/// silo process next to the game gateway and calls grains through the silo's own IGrainFactory.
/// </summary>
/// <remarks>
/// The host calls, in this order:
/// <code>
/// builder.Services.AddWildCatAdmin(builder.Configuration);
/// ...
/// app.UseAuthentication();
/// app.UseAuthorization();
/// app.UseRateLimiter();
/// app.MapWildCatAdmin();
/// </code>
/// The admin API adds its exception, status code and CORS handling for /api/v1 itself through a
/// startup filter (see <see cref="AdminStartupFilter"/>). It sends account emails through the
/// <c>WildCat.Interfaces.IEmailSender</c> the host registers.
/// </remarks>
public static class WildCatAdminExtensions
{
    public const string ApiPrefix = "/api/v1";
    public const string DeveloperPolicy = "WildCat.Developer";
    public const string AdminPolicy = "WildCat.Admin";
    public const string CorsPolicy = "WildCat.Admin";
    public const string AuthRateLimitPolicy = "WildCat.Auth";

    /// <summary>
    /// The IDeveloperManager singleton grain key used across the backend.
    /// </summary>
    public const string DeveloperManagerKey = "main";

    /// <summary>
    /// Registers the admin API: options from the <c>WildCat:Admin</c> section of
    /// <paramref name="configuration"/> (validated at startup), JWT bearer authentication, the
    /// developer and admin authorization policies, CORS, the /auth rate limiter, ProblemDetails and
    /// the OpenAPI document.
    /// </summary>
    public static IServiceCollection AddWildCatAdmin(this IServiceCollection services, IConfiguration configuration)
    {
        services.AddOptions<WildCatAdminOptions>()
            .Bind(configuration.GetSection(WildCatAdminOptions.SectionName))
            .ValidateOnStart();
        services.TryAddEnumerable(ServiceDescriptor.Singleton<IValidateOptions<WildCatAdminOptions>, WildCatAdminOptionsValidator>());

        services.TryAddSingleton(TimeProvider.System);
        services.TryAddSingleton<AdminTokenService>();
        services.TryAddSingleton<AdminAccess>();
        services.TryAddSingleton<AccountEmails>();
        services.TryAddEnumerable(ServiceDescriptor.Transient<IStartupFilter, AdminStartupFilter>());
        services.AddProblemDetails(options =>
        {
            var previous = options.CustomizeProblemDetails;
            options.CustomizeProblemDetails = context =>
            {
                previous?.Invoke(context);
                if (context.ProblemDetails.Detail == null && context.HttpContext.Request.Path.StartsWithSegments(ApiPrefix))
                    context.ProblemDetails.Detail = DefaultDetail(context.ProblemDetails.Status);
            };
        });

        services.AddAuthentication(JwtBearerDefaults.AuthenticationScheme).AddJwtBearer();
        services.AddOptions<JwtBearerOptions>(JwtBearerDefaults.AuthenticationScheme)
            .Configure<AdminTokenService>((options, tokens) =>
            {
                options.MapInboundClaims = false;
                options.TokenValidationParameters = tokens.CreateValidationParameters();
                options.Events = new JwtBearerEvents
                {
                    OnTokenValidated = async context =>
                    {
                        if (!Guid.TryParse(context.Principal?.FindFirst(AdminClaims.Subject)?.Value, out var developerKey) ||
                            !Guid.TryParse(context.Principal?.FindFirst(AdminClaims.SecurityStamp)?.Value, out var securityStamp))
                        {
                            context.Fail("The token has no valid account credentials.");
                            return;
                        }
                        var grains = context.HttpContext.RequestServices.GetRequiredService<IGrainFactory>();
                        if (!await grains.GetGrain<IDeveloper>(developerKey).IsAdminSessionValid(securityStamp))
                            context.Fail("The account credentials have changed. Log in again.");
                    }
                };
            });

        services.TryAddEnumerable(ServiceDescriptor.Singleton<IAuthorizationHandler, CurrentAdminHandler>());
        services.AddAuthorizationBuilder()
            .AddPolicy(DeveloperPolicy, policy => policy
                .AddAuthenticationSchemes(JwtBearerDefaults.AuthenticationScheme)
                .RequireAuthenticatedUser()
                .RequireClaim(AdminClaims.Subject)
                .RequireRole(AdminClaims.DeveloperRole, AdminClaims.AdminRole))
            .AddPolicy(AdminPolicy, policy => policy
                .AddAuthenticationSchemes(JwtBearerDefaults.AuthenticationScheme)
                .RequireAuthenticatedUser()
                .RequireClaim(AdminClaims.Subject)
                .RequireRole(AdminClaims.AdminRole)
                .AddRequirements(new CurrentAdminRequirement()));

        services.AddCors();
        services.AddOptions<CorsOptions>().Configure<IOptions<WildCatAdminOptions>>((cors, admin) =>
            cors.AddPolicy(CorsPolicy, policy => policy
                .WithOrigins((admin.Value.CorsOrigins ?? []).Select(x => x.TrimEnd('/')).ToArray())
                .AllowAnyHeader()
                .AllowAnyMethod()
                .WithExposedHeaders("Location", "Retry-After")));

        services.AddRateLimiter(options => options.AddPolicy<string, AuthRateLimiterPolicy>(AuthRateLimitPolicy));

        services.AddOpenApi(options =>
        {
            options.AddDocumentTransformer<AdminOpenApiTransformer>();
            options.AddOperationTransformer<AdminOpenApiTransformer>();
        });
        return services;
    }

    /// <summary>
    /// Maps the endpoints under /api/v1, the OpenAPI document at /openapi/v1.json and, when the
    /// content root has wwwroot/panel/index.html, the panel at /panel. Fails here, at startup, when
    /// the options are invalid (for example no signing key outside Development).
    /// </summary>
    public static IEndpointRouteBuilder MapWildCatAdmin(this IEndpointRouteBuilder endpoints)
    {
        endpoints.ServiceProvider.GetRequiredService<AdminTokenService>();

        endpoints.MapOpenApi().AllowAnonymous();

        var api = endpoints.MapGroup(ApiPrefix);
        AuthEndpoints.Map(api);
        AccountEndpoints.Map(api);
        GameEndpoints.Map(api);

        PanelHosting.Map(endpoints);
        return endpoints;
    }

    /// <summary>
    /// A readable detail for the error responses the framework makes without one (authentication,
    /// authorization, routing, body binding and unhandled exceptions), so the panel always has
    /// something to show.
    /// </summary>
    private static string? DefaultDetail(int? status) => status switch
    {
        400 => "The request could not be read. Send a JSON body that matches the API description.",
        401 => "The request needs a valid bearer token. Log in again.",
        403 => "You do not have permission to do this.",
        404 => "There is nothing at this address.",
        405 => "This address does not accept this HTTP method.",
        415 => "Send the request body as application/json.",
        500 => "Something went wrong on the server. The error has been logged.",
        _ => null,
    };
}
