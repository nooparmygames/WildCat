using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using System;
using System.Linq;
using System.Net.WebSockets;

namespace WildCat.Gateway
{
    /// <summary>
    /// Hosting entry points for the WebSocket gateway. The gateway runs inside the silo process and
    /// uses the silo's own IClusterClient, so there is no separate Orleans client to configure.
    /// </summary>
    public static class GatewayExtensions
    {
        /// <summary>
        /// Registers the gateway services (the singleton <see cref="IFrontEndServer"/> that dispatches
        /// every message) and binds <see cref="GameGatewayOptions"/> from <paramref name="configuration"/>
        /// when it is given. Call it on a host that also calls UseOrleans.
        /// </summary>
        public static IServiceCollection AddGameGateway(this IServiceCollection services, IConfiguration configuration = null)
        {
            var options = services.AddOptions<GameGatewayOptions>();
            if (configuration != null)
                options.Bind(configuration);
            options
                .Validate(o => o.MaxMessageBytes > 0, "Gateway:MaxMessageBytes must be greater than zero.")
                .ValidateOnStart();
            services.AddSingleton<IFrontEndServer, FrontEndServer>();
            return services;
        }

        /// <summary>
        /// Accepts WebSocket connections at <paramref name="pattern"/> and hands each one to the
        /// gateway. A request to the same path that is not a WebSocket upgrade gets 400, and a
        /// WebSocket request from an origin missing from <see cref="GameGatewayOptions.AllowedOrigins"/>
        /// gets 403. Call app.UseWebSockets() before this.
        /// </summary>
        public static IEndpointConventionBuilder MapGameGateway(this IEndpointRouteBuilder endpoints, string pattern = "/ws")
        {
            return endpoints.Map(pattern, async context =>
            {
                if (context.WebSockets.IsWebSocketRequest)
                {
                    var options = context.RequestServices.GetRequiredService<IOptions<GameGatewayOptions>>().Value;
                    var logger = context.RequestServices.GetRequiredService<ILogger<ConnectionProcessor>>();
                    if (!IsOriginAllowed(context.Request.Headers.Origin.ToString(), options.AllowedOrigins))
                    {
                        logger.LogInformation("Refused a WebSocket from origin {Origin}, which is not in Gateway:AllowedOrigins",
                            context.Request.Headers.Origin.ToString());
                        context.Response.StatusCode = StatusCodes.Status403Forbidden;
                        return;
                    }

                    // Resolved per connection rather than at startup so the silo (and its stream
                    // providers) is running before the gateway singleton is created.
                    var frontEndServer = context.RequestServices.GetRequiredService<IFrontEndServer>();
                    WebSocket webSocket = await context.WebSockets.AcceptWebSocketAsync();
                    ConnectionProcessor c = new ConnectionProcessor(webSocket, ConnectionProcessor.GetNextId(),
                        options.MaxMessageBytes, logger);
                    c.OnConnected += frontEndServer.PlayerConnected;
                    c.OnDisConnected += frontEndServer.PlayerDisconnected;
                    await c.Process(context, webSocket);
                }
                else
                {
                    context.Response.StatusCode = 400;
                }
            }).WithDisplayName("Game gateway WebSocket");
        }

        /// <summary>
        /// An empty list accepts every origin. Otherwise the origin must be in the list, compared
        /// without case and ignoring a trailing slash. A request without an Origin header (a native
        /// client rather than a browser) is always accepted.
        /// </summary>
        internal static bool IsOriginAllowed(string origin, string[] allowedOrigins)
        {
            if (allowedOrigins == null || allowedOrigins.Length == 0 || string.IsNullOrEmpty(origin))
                return true;
            return allowedOrigins.Any(allowed =>
                string.Equals(allowed?.TrimEnd('/'), origin.TrimEnd('/'), StringComparison.OrdinalIgnoreCase));
        }
    }
}
