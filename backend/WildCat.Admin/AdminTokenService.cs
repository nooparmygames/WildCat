using System.Security.Cryptography;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Microsoft.IdentityModel.JsonWebTokens;
using Microsoft.IdentityModel.Tokens;

namespace WildCat.Admin;

/// <summary>
/// Issues and describes the panel's bearer tokens: HMAC-SHA256 JWTs with issuer and audience
/// "wildcat", valid for up to eight hours, carrying the developer key (sub), email, role and credential stamp.
/// </summary>
public sealed class AdminTokenService
{
    public const string Issuer = "wildcat";
    public const string Audience = "wildcat";
    public const int MinKeyBytes = 32;
    public static readonly TimeSpan TokenLifetime = TimeSpan.FromHours(8);

    private readonly SymmetricSecurityKey signingKey;
    private readonly TimeProvider timeProvider;
    private readonly JsonWebTokenHandler handler = new();

    public AdminTokenService(IOptions<WildCatAdminOptions> options, IHostEnvironment environment,
        TimeProvider timeProvider, ILogger<AdminTokenService> logger)
    {
        this.timeProvider = timeProvider;
        string? configured = options.Value.JwtSigningKey;
        byte[] keyBytes;
        if (!string.IsNullOrWhiteSpace(configured))
        {
            if (!TryDecodeKey(configured, out keyBytes))
                throw new InvalidOperationException($"WildCat:Admin:JwtSigningKey must be base64 and decode to at least {MinKeyBytes} bytes.");
        }
        else if (environment.IsDevelopment())
        {
            keyBytes = RandomNumberGenerator.GetBytes(MinKeyBytes);
            logger.LogWarning(
                "WildCat:Admin:JwtSigningKey is not set, so the admin API signs tokens with a random key made for this run. " +
                "Panel logins will not survive a restart. Set the key in user secrets to keep them.");
        }
        else
        {
            throw new InvalidOperationException("WildCat:Admin:JwtSigningKey is required outside the Development environment.");
        }
        signingKey = new SymmetricSecurityKey(keyBytes);
    }

    /// <summary>
    /// True when <paramref name="value"/> is base64 for at least <see cref="MinKeyBytes"/> bytes.
    /// </summary>
    public static bool TryDecodeKey(string value, out byte[] key)
    {
        key = [];
        try
        {
            key = Convert.FromBase64String(value.Trim());
        }
        catch (FormatException)
        {
            return false;
        }
        return key.Length >= MinKeyBytes;
    }

    public (string Token, DateTimeOffset ExpiresAt) CreateToken(Guid developerKey, string email, bool isAdmin, Guid securityStamp)
    {
        DateTimeOffset now = timeProvider.GetUtcNow();
        // JWT times have whole-second precision; report the same expiry the token carries.
        DateTimeOffset expiresAt = DateTimeOffset.FromUnixTimeSeconds((now + TokenLifetime).ToUnixTimeSeconds());
        var descriptor = new SecurityTokenDescriptor
        {
            Issuer = Issuer,
            Audience = Audience,
            IssuedAt = now.UtcDateTime,
            NotBefore = now.UtcDateTime,
            Expires = expiresAt.UtcDateTime,
            Claims = new Dictionary<string, object>
            {
                [AdminClaims.Subject] = developerKey.ToString(),
                [AdminClaims.Email] = email,
                [AdminClaims.Role] = isAdmin ? AdminClaims.AdminRole : AdminClaims.DeveloperRole,
                [AdminClaims.SecurityStamp] = securityStamp.ToString("N"),
                [JwtRegisteredClaimNames.Jti] = Guid.NewGuid().ToString("N"),
            },
            SigningCredentials = new SigningCredentials(signingKey, SecurityAlgorithms.HmacSha256),
        };
        return (handler.CreateToken(descriptor), expiresAt);
    }

    public TokenValidationParameters CreateValidationParameters()
    {
        return new TokenValidationParameters
        {
            ValidIssuer = Issuer,
            ValidAudience = Audience,
            IssuerSigningKey = signingKey,
            ValidAlgorithms = [SecurityAlgorithms.HmacSha256],
            ValidateIssuer = true,
            ValidateAudience = true,
            ValidateLifetime = true,
            ValidateIssuerSigningKey = true,
            RequireExpirationTime = true,
            RequireSignedTokens = true,
            ClockSkew = TimeSpan.FromMinutes(1),
            NameClaimType = AdminClaims.Subject,
            RoleClaimType = AdminClaims.Role,
        };
    }
}
