using System.Net.Mail;
using System.Text.RegularExpressions;
using WildCat.Interfaces;

namespace WildCat.Admin;

/// <summary>
/// Input rules shared by the endpoints. Each check appends a readable message to a list so one
/// response can report every problem at once.
/// </summary>
internal static partial class Validation
{
    public const int MinPasswordLength = 8;
    public const int MaxEmailLength = 254;
    public const int MaxNameLength = 100;
    public const int MaxResourceNameLength = 64;

    /// <summary>
    /// Leaderboard, item and game mode names: letters, digits, '_', '-' and '.', up to 64
    /// characters. They become part of grain keys and URLs.
    /// </summary>
    [GeneratedRegex("^[A-Za-z0-9_.-]{1,64}$")]
    private static partial Regex ResourceNameRegex();

    /// <summary>
    /// Script event and parameter names are C# identifiers: the script must declare a method with
    /// the event's name.
    /// </summary>
    [GeneratedRegex("^[A-Za-z_][A-Za-z0-9_]{0,63}$")]
    private static partial Regex IdentifierRegex();

    /// <summary>
    /// Trims and lower-cases an email and checks its form. Returns null and adds an error when it
    /// is missing or malformed.
    /// </summary>
    public static string? Email(string? value, List<string> errors)
    {
        string trimmed = value?.Trim() ?? "";
        if (trimmed.Length == 0)
        {
            errors.Add("The email is required.");
            return null;
        }
        if (trimmed.Length > MaxEmailLength
            || !MailAddress.TryCreate(trimmed, out var address)
            || !string.Equals(address.Address, trimmed, StringComparison.Ordinal))
        {
            errors.Add("The email is not a valid address.");
            return null;
        }
        return trimmed.ToLowerInvariant();
    }

    /// <summary>
    /// A new password: at least <see cref="MinPasswordLength"/> characters and at most
    /// <see cref="PasswordHasher.MaxPasswordLength"/>.
    /// </summary>
    public static void NewPassword(string? value, string field, List<string> errors)
    {
        if (string.IsNullOrEmpty(value))
            errors.Add($"The {field} is required.");
        else if (value.Length < MinPasswordLength)
            errors.Add($"The {field} must be at least {MinPasswordLength} characters long.");
        else if (value.Length > PasswordHasher.MaxPasswordLength)
            errors.Add($"The {field} cannot be longer than {PasswordHasher.MaxPasswordLength} characters.");
    }

    public static void Required(string? value, string field, List<string> errors)
    {
        if (string.IsNullOrWhiteSpace(value))
            errors.Add($"The {field} is required.");
    }

    public static void MaxLength(string? value, string field, int max, List<string> errors)
    {
        if (value != null && value.Length > max)
            errors.Add($"The {field} cannot be longer than {max} characters.");
    }

    public static void Range(long value, string field, long min, long max, List<string> errors)
    {
        if (value < min || value > max)
            errors.Add($"The {field} must be between {min} and {max}.");
    }

    public static bool IsResourceName(string? value) => value != null && ResourceNameRegex().IsMatch(value);

    public static bool IsIdentifier(string? value) => value != null && IdentifierRegex().IsMatch(value);

    public static void ResourceName(string? value, string field, List<string> errors)
    {
        if (!IsResourceName(value))
            errors.Add($"The {field} must be 1 to {MaxResourceNameLength} letters, digits, '_', '-' or '.'.");
    }

    /// <summary>
    /// Trims a value and turns an empty one into null.
    /// </summary>
    public static string? Clean(string? value)
    {
        string? trimmed = value?.Trim();
        return string.IsNullOrEmpty(trimmed) ? null : trimmed;
    }
}
