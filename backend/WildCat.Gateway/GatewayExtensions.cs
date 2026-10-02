using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using System;
using System.Collections.Generic;
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
        /// Registers the gateway services (dispatcher, connection registry, logins, push
        /// subscriptions and every <see cref="IMessageHandler"/> in this assembly) and binds
        /// <see cref="GameGatewayOptions"/> from <paramref name="configuration"/> when it is given.
        /// Call it on a host that also calls UseOrleans.
        /// </summary>
        public static IServiceCollection AddGameGateway(this IServiceCollection services, IConfiguration configuration = null)
        {
            var options = services.AddOptions<GameGatewayOptions>();
            if (configuration != null)
                options.Bind(configuration);
            options
                .Validate(o => o.MaxMessageBytes > 0, "Gateway:MaxMessageBytes must be greater than zero.")
                .Validate(o => o.AuthAttemptBurst > 0, "Gateway:AuthAttemptBurst must be greater than zero.")
                .Validate(o => o.AuthAttemptsPerMinute > 0, "Gateway:AuthAttemptsPerMinute must be greater than zero.")
                .Validate(o => o.AuthAttemptsPerAddressBurst >= 0, "Gateway:AuthAttemptsPerAddressBurst must not be negative (0 turns the limit off).")
                .Validate(o => o.AuthAttemptsPerAddressPerMinute > 0, "Gateway:AuthAttemptsPerAddressPerMinute must be greater than zero.")
                .Validate(o => o.OutboundQueueCapacity > 0, "Gateway:OutboundQueueCapacity must be greater than zero.")
                .Validate(o => o.OutboundQueueBytes > 0, "Gateway:OutboundQueueBytes must be greater than zero.")
                .Validate(o => o.RelayQueueBytes > 0, "Gateway:RelayQueueBytes must be greater than zero.")
                .Validate(o => o.MaxConcurrentRequests > 0, "Gateway:MaxConcurrentRequests must be greater than zero.")
                .ValidateOnStart();
            services.TryAddSingleton(TimeProvider.System);
            services.TryAddSingleton<ConnectionRegistry>();
            services.TryAddSingleton<AddressAttemptLimiter>();
            services.TryAddSingleton<PushSubscriptions>();
            services.TryAddSingleton<LoginManager>();
            foreach (Type handler in HandlerTypes())
                services.TryAddEnumerable(ServiceDescriptor.Singleton(typeof(IMessageHandler), handler));
            services.TryAddSingleton<HandlerRegistry>();
            services.TryAddSingleton<MessageDispatcher>();
            return services;
        }

        /// <summary>
        /// Every concrete <see cref="IMessageHandler"/> in the gateway assembly.
        /// </summary>
        internal static IEnumerable<Type> HandlerTypes()
        {
            return typeof(GatewayExtensions).Assembly.GetTypes()
                .Where(t => t.IsClass && !t.IsAbstract && !t.IsGenericTypeDefinition
                            && typeof(IMessageHandler).IsAssignableFrom(t))
                .OrderBy(t => t.FullName, StringComparer.Ordinal);
        }

        /// <summary>
        /// Accepts WebSocket connections at <paramref name="pattern"/> and hands each one to the
        /// <see cref="MessageDispatcher"/>. A request to the same path that is not a WebSocket
        /// upgrade gets 400, and a WebSocket request from an origin missing from
        /// <see cref="GameGatewayOptions.AllowedOrigins"/> gets 403. Call app.UseWebSockets() before this.
        /// </summary>
        public static IEndpointConventionBuilder MapGameGateway(this IEndpointRouteBuilder endpoints, string pattern = "/ws")
        {
            return endpoints.Map(pattern, async context =>
            {
                if (!context.WebSockets.IsWebSocketRequest)
                {
                    context.Response.StatusCode = StatusCodes.Status400BadRequest;
                    return;
                }

                var options = context.RequestServices.GetRequiredService<IOptions<GameGatewayOptions>>().Value;
                if (!IsOriginAllowed(context.Request.Headers.Origin.ToString(), options.AllowedOrigins))
                {
                    var logger = context.RequestServices.GetRequiredService<ILogger<MessageDispatcher>>();
                    logger.LogInformation("Refused a WebSocket from origin {Origin}, which is not in Gateway:AllowedOrigins",
                        context.Request.Headers.Origin.ToString());
                    context.Response.StatusCode = StatusCodes.Status403Forbidden;
                    return;
                }

                // Resolved per connection rather than at startup so the silo (and its stream
                // providers) is running before the gateway singletons are created.
                var dispatcher = context.RequestServices.GetRequiredService<MessageDispatcher>();
                WebSocket webSocket = await context.WebSockets.AcceptWebSocketAsync();
                await dispatcher.RunAsync(webSocket, context.Connection.RemoteIpAddress, context.RequestAborted);
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
