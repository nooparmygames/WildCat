using System.Globalization;

namespace WildCat.Grains.Resources
{
    /// <summary>
    /// Reads the account email texts from EmailTexts.resx (English) or a satellite translation for
    /// the current UI culture.
    /// </summary>
    public static class EmailTexts
    {
        // Fully qualified because WildCat.Grains has a grain named ResourceManager.
        private static readonly System.Resources.ResourceManager Texts =
            new System.Resources.ResourceManager("WildCat.Grains.Resources.EmailTexts", typeof(EmailTexts).Assembly);

        public static string ActivationSubject => Get(nameof(ActivationSubject));

        /// <summary>The activation email body with <paramref name="code"/> filled in.</summary>
        public static string ActivationBody(string code) => Format(nameof(ActivationBody), code);

        public static string ResetSubject => Get(nameof(ResetSubject));

        /// <summary>The password reset email body with <paramref name="code"/> filled in.</summary>
        public static string ResetBody(string code) => Format(nameof(ResetBody), code);

        private static string Get(string name)
        {
            return Texts.GetString(name, CultureInfo.CurrentUICulture)
                   ?? throw new InvalidOperationException($"The email text {name} is missing from EmailTexts.resx.");
        }

        private static string Format(string name, string code)
        {
            return string.Format(CultureInfo.CurrentUICulture, Get(name), code);
        }
    }
}
