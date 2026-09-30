using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.Extensions.Options;

namespace WildCat.Admin;

/// <summary>
/// Adds the admin API's own middleware in front of the host's pipeline, for /api/v1 requests only,
/// so the host's Program.cs does not have to: unhandled exceptions and empty error responses (401
/// and 403 from authorization, 404 for unknown routes, 400 from body binding) become ProblemDetails,
/// and the CORS policy from <see cref="WildCatAdminOptions.CorsOrigins"/> applies when it lists any
/// origin. Everything else, such as the game WebSocket at /ws, passes by untouched.
/// </summary>
internal sealed class AdminStartupFilter(IOptions<WildCatAdminOptions> options) : IStartupFilter
{
    public Action<IApplicationBuilder> Configure(Action<IApplicationBuilder> next)
    {
        return app =>
        {
            bool useCors = (options.Value.CorsOrigins ?? []).Length > 0;
            app.UseWhen(context => context.Request.Path.StartsWithSegments(WildCatAdminExtensions.ApiPrefix), api =>
            {
                api.UseExceptionHandler();
                api.UseStatusCodePages();
                if (useCors)
                    api.UseCors(WildCatAdminExtensions.CorsPolicy);
            });
            next(app);
        };
    }
}
