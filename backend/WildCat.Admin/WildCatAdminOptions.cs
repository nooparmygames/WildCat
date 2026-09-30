using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Options;

namespace WildCat.Admin;

/// <summary>
/// Settings for the admin API, bound from the <c>WildCat:Admin</c> configuration section. None of
/// these belong in a checked-in file with real values: set the signing key and admin emails through
/// user secrets or environment variables (for example <c>WildCat__Admin__JwtSigningKey</c> and
/// <c>WildCat__Admin__AdminEmails__0</c>).
/// </summary>
public sealed class WildCatAdminOptions
{
    public const string SectionName = "WildCat:Admin";

    /// <summary>
    /// Base64 HMAC-SHA256 key for the panel's bearer tokens, at least 32 bytes once decoded
    /// (<c>openssl rand -base64 32</c> makes one). Required outside the Development environment; in
    /// Development an empty value means a random key per run, so tokens do not survive a restart.
    /// </summary>
    public string? JwtSigningKey { get; set; }

    /// <summary>
    /// Developers who log in with one of these emails get the admin role. Compared without case.
    /// </summary>
    public string[] AdminEmails { get; set; } = [];

    /// <summary>
    /// Browser origins allowed to call the API from another origin, such as the Vite dev server at
    /// http://localhost:5173. Empty allows none; the panel served from /panel needs no entry.
    /// </summary>
    public string[] CorsOrigins { get; set; } = [];

    /// <summary>
    /// Optional public address of the site that serves the panel under /panel, such as
    /// https://games.example.com. When set, activation and password reset emails also link to
    /// {PanelBaseUrl}/panel/activate and {PanelBaseUrl}/panel/reset-password with the email and
    /// code filled in. Empty means the emails carry only the code.
    /// </summary>
    public string? PanelBaseUrl { get; set; }

    /// <summary>
    /// How many requests one client IP address may send to the unauthenticated /auth endpoints in
    /// a one minute window before getting 429.
    /// </summary>
    public int AuthRequestsPerMinute { get; set; } = 10;
}

internal sealed class WildCatAdminOptionsValidator(IHostEnvironment environment) : IValidateOptions<WildCatAdminOptions>
{
    public ValidateOptionsResult Validate(string? name, WildCatAdminOptions options)
    {
        var failures = new List<string>();

        if (string.IsNullOrWhiteSpace(options.JwtSigningKey))
        {
            if (!environment.IsDevelopment())
            {
                failures.Add("WildCat:Admin:JwtSigningKey is required outside the Development environment. " +
                    "Set it to a base64 string of at least 32 random bytes through user secrets or the " +
                    "WildCat__Admin__JwtSigningKey environment variable.");
            }
        }
        else if (!AdminTokenService.TryDecodeKey(options.JwtSigningKey, out _))
        {
            failures.Add($"WildCat:Admin:JwtSigningKey must be base64 and decode to at least {AdminTokenService.MinKeyBytes} bytes.");
        }

        foreach (var origin in options.CorsOrigins ?? [])
        {
            if (!IsHttpUrl(origin))
                failures.Add($"WildCat:Admin:CorsOrigins contains '{origin}', which is not an absolute http or https origin.");
        }

        if (!string.IsNullOrWhiteSpace(options.PanelBaseUrl) && !IsHttpUrl(options.PanelBaseUrl))
            failures.Add("WildCat:Admin:PanelBaseUrl must be an absolute http or https address.");

        if (options.AuthRequestsPerMinute < 1)
            failures.Add("WildCat:Admin:AuthRequestsPerMinute must be at least 1.");

        return failures.Count == 0 ? ValidateOptionsResult.Success : ValidateOptionsResult.Fail(failures);
    }

    private static bool IsHttpUrl(string? value)
    {
        return Uri.TryCreate(value, UriKind.Absolute, out var uri)
            && (uri.Scheme == Uri.UriSchemeHttp || uri.Scheme == Uri.UriSchemeHttps);
    }
}
