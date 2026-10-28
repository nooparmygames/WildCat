using System.Security.Claims;
using Microsoft.AspNetCore.Authorization;
using Microsoft.Extensions.Options;

namespace WildCat.Admin;

/// <summary>
/// Claim names and role values in the panel's tokens. Inbound claim mapping is off, so the
/// principal carries these short names as they are in the JWT.
/// </summary>
public static class AdminClaims
{
    public const string Subject = "sub";
    public const string Email = "email";
    public const string Role = "role";
    public const string SecurityStamp = "security_stamp";
    public const string DeveloperRole = "developer";
    public const string AdminRole = "admin";

    /// <summary>
    /// The developer key in the <c>sub</c> claim. Only called behind the developer policy, which
    /// requires the claim.
    /// </summary>
    public static Guid GetDeveloperKey(this ClaimsPrincipal user)
    {
        return Guid.Parse(user.FindFirstValue(Subject) ?? throw new InvalidOperationException("The token has no sub claim."));
    }
}

/// <summary>
/// Decides who is an admin. The role in the token is decided at login, and every admin check also
/// looks at the current <see cref="WildCatAdminOptions.AdminEmails"/>, so removing an email from
/// the list takes effect without waiting for its tokens to expire.
/// </summary>
public sealed class AdminAccess(IOptionsMonitor<WildCatAdminOptions> options)
{
    public bool IsAdminEmail(string? email)
    {
        if (string.IsNullOrWhiteSpace(email))
            return false;
        return (options.CurrentValue.AdminEmails ?? []).Any(x =>
            string.Equals(x?.Trim(), email.Trim(), StringComparison.OrdinalIgnoreCase));
    }

    public bool IsAdmin(ClaimsPrincipal user)
    {
        return user.IsInRole(AdminClaims.AdminRole) && IsAdminEmail(user.FindFirstValue(AdminClaims.Email));
    }
}

internal sealed class CurrentAdminRequirement : IAuthorizationRequirement
{
}

internal sealed class CurrentAdminHandler(AdminAccess access) : AuthorizationHandler<CurrentAdminRequirement>
{
    protected override Task HandleRequirementAsync(AuthorizationHandlerContext context, CurrentAdminRequirement requirement)
    {
        if (access.IsAdmin(context.User))
            context.Succeed(requirement);
        return Task.CompletedTask;
    }
}
