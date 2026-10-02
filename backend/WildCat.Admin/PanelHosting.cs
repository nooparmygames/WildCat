using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using Microsoft.AspNetCore.StaticFiles;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.FileProviders;
using Microsoft.Extensions.Hosting;

namespace WildCat.Admin;

/// <summary>
/// Serves a built panel from wwwroot/panel under the host's content root at /panel, when that
/// folder has an index.html (a Docker image copies the panel build there). Paths that are not a
/// file get index.html so the panel's client-side routes work on reload; a missing file with an
/// extension, such as a stale script, gets 404 instead. Vite's hashed files under assets/ are
/// cached for a year, everything else is revalidated.
/// </summary>
internal static class PanelHosting
{
    public const string RequestPath = "/panel";

    public static void Map(IEndpointRouteBuilder endpoints)
    {
        var environment = endpoints.ServiceProvider.GetRequiredService<IHostEnvironment>();
        string root = Path.Combine(environment.ContentRootPath, "wwwroot", "panel");
        string index = Path.Combine(root, "index.html");
        if (!File.Exists(index))
            return;

        var files = new PhysicalFileProvider(root);
        var contentTypes = new FileExtensionContentTypeProvider();

        // The catch-all also matches /panel itself, which is sent to /panel/ so relative URLs in
        // the page resolve inside the panel.
        endpoints.MapGet(RequestPath + "/{**path}", (string? path, HttpContext context) =>
            {
                if (string.IsNullOrEmpty(path) && !context.Request.Path.Value!.EndsWith('/'))
                    return Results.Redirect(context.Request.PathBase + RequestPath + "/" + context.Request.QueryString);
                if (!string.IsNullOrEmpty(path))
                {
                    var file = files.GetFileInfo(path);
                    if (file.Exists && !file.IsDirectory && file.PhysicalPath != null)
                    {
                        if (!contentTypes.TryGetContentType(file.Name, out var contentType))
                            contentType = "application/octet-stream";
                        context.Response.Headers.CacheControl = path.StartsWith("assets/", StringComparison.Ordinal)
                            ? "public, max-age=31536000, immutable"
                            : "no-cache";
                        return Results.File(file.PhysicalPath, contentType, lastModified: file.LastModified);
                    }
                    if (Path.HasExtension(path))
                        return Results.NotFound();
                }
                context.Response.Headers.CacheControl = "no-cache";
                return Results.File(index, "text/html; charset=utf-8", lastModified: File.GetLastWriteTimeUtc(index));
            })
            .AllowAnonymous()
            .ExcludeFromDescription();
    }
}
