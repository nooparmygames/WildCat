using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using Microsoft.Extensions.DependencyInjection;
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
        /// every message). Call it on a host that also calls UseOrleans.
        /// </summary>
        public static IServiceCollection AddGameGateway(this IServiceCollection services)
        {
            services.AddSingleton<IFrontEndServer, FrontEndServer>();
            return services;
        }

        /// <summary>
        /// Accepts WebSocket connections at <paramref name="pattern"/> and hands each one to the
        /// gateway. A request to the same path that is not a WebSocket upgrade gets 400. Call
        /// app.UseWebSockets() before this.
        /// </summary>
        public static IEndpointConventionBuilder MapGameGateway(this IEndpointRouteBuilder endpoints, string pattern = "/ws")
        {
            return endpoints.Map(pattern, async context =>
            {
                if (context.WebSockets.IsWebSocketRequest)
                {
                    // Resolved per connection rather than at startup so the silo (and its stream
                    // providers) is running before the gateway singleton is created.
                    var frontEndServer = context.RequestServices.GetRequiredService<IFrontEndServer>();
                    WebSocket webSocket = await context.WebSockets.AcceptWebSocketAsync();
                    ConnectionProcessor c = new ConnectionProcessor(webSocket, ConnectionProcessor.GetNextId());
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
    }
}
