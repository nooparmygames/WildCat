using System.Globalization;
using System.Resources;

namespace WildCat.Admin.Resources;

/// <summary>
/// The account email texts in EmailTexts.resx. English is the neutral language; a translation is an
/// EmailTexts.{culture}.resx next to it, picked by the current UI culture.
/// </summary>
internal static class EmailTexts
{
    private static readonly ResourceManager ResourceManager =
        new("WildCat.Admin.Resources.EmailTexts", typeof(EmailTexts).Assembly);

    public static string GreetingWithName => Get(nameof(GreetingWithName));
    public static string GreetingWithoutName => Get(nameof(GreetingWithoutName));
    public static string ActivationSubject => Get(nameof(ActivationSubject));
    public static string ActivationBody => Get(nameof(ActivationBody));
    public static string PasswordResetSubject => Get(nameof(PasswordResetSubject));
    public static string PasswordResetBody => Get(nameof(PasswordResetBody));
    public static string LinkLine => Get(nameof(LinkLine));

    private static string Get(string name)
    {
        return ResourceManager.GetString(name, CultureInfo.CurrentUICulture)
            ?? throw new InvalidOperationException($"The email text {name} is missing from EmailTexts.resx.");
    }
}
