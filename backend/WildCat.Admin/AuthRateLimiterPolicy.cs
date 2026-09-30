using System.Globalization;
using System.Net;
using System.Net.Sockets;
using System.Threading.RateLimiting;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.RateLimiting;
using Microsoft.Extensions.Options;

namespace WildCat.Admin;

/// <summary>
/// Limits the anonymous /auth endpoints (login, register, activation and password reset) to
/// <see cref="WildCatAdminOptions.AuthRequestsPerMinute"/> requests per client address in a fixed
/// one minute window, and answers the rest with 429, a Retry-After header and a ProblemDetails
/// body. IPv6 clients are grouped by their /64 network, since one host usually has a whole /64.
/// The client address is the one the forwarded headers middleware reports behind a proxy.
/// </summary>
internal sealed class AuthRateLimiterPolicy(IOptions<WildCatAdminOptions> options) : IRateLimiterPolicy<string>
{
    public Func<OnRejectedContext, CancellationToken, ValueTask>? OnRejected { get; } = async (context, cancellationToken) =>
    {
        var response = context.HttpContext.Response;
        response.StatusCode = StatusCodes.Status429TooManyRequests;
        if (context.Lease.TryGetMetadata(MetadataName.RetryAfter, out TimeSpan retryAfter))
            response.Headers.RetryAfter = Math.Max(1, (int)Math.Ceiling(retryAfter.TotalSeconds)).ToString(CultureInfo.InvariantCulture);
        await Problems.Problem(StatusCodes.Status429TooManyRequests,
                "Too many requests from this address. Wait a minute and try again.")
            .ExecuteAsync(context.HttpContext);
    };

    public RateLimitPartition<string> GetPartition(HttpContext httpContext)
    {
        int limit = options.Value.AuthRequestsPerMinute;
        return RateLimitPartition.GetFixedWindowLimiter(PartitionKey(httpContext.Connection.RemoteIpAddress), _ =>
            new FixedWindowRateLimiterOptions
            {
                PermitLimit = limit,
                Window = TimeSpan.FromMinutes(1),
                QueueLimit = 0,
                AutoReplenishment = true,
            });
    }

    internal static string PartitionKey(IPAddress? address)
    {
        if (address == null)
            return "unknown";
        if (address.IsIPv4MappedToIPv6)
            address = address.MapToIPv4();
        if (address.AddressFamily == AddressFamily.InterNetworkV6)
        {
            byte[] bytes = address.GetAddressBytes();
            Array.Clear(bytes, 8, 8);
            return new IPAddress(bytes) + "/64";
        }
        return address.ToString();
    }
}
