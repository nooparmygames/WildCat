namespace WildCat.Grains
{
    /// <summary>
    /// SMTP settings for account emails, bound from the "WildCat:Email" configuration section.
    /// Checked-in files leave the strings empty; real values come from user secrets or environment
    /// variables (for example WildCat__Email__Password). When <see cref="SmtpHost"/> or
    /// <see cref="FromAddress"/> is empty, emails are written to the log instead of being sent
    /// (see <see cref="EmailServiceCollectionExtensions.AddWildCatEmail"/>).
    /// </summary>
    public class EmailOptions
    {
        /// <summary>The configuration section these options are read from.</summary>
        public const string SectionName = "WildCat:Email";

        /// <summary>SMTP server host name.</summary>
        public string SmtpHost { get; set; } = "";

        /// <summary>SMTP server port. 587 (submission with STARTTLS) by default.</summary>
        public int SmtpPort { get; set; } = 587;

        /// <summary>
        /// Whether to upgrade the SMTP connection with STARTTLS. System.Net.Mail has no implicit
        /// TLS, so port 465 does not work; use the submission port 587.
        /// </summary>
        public bool UseSsl { get; set; } = true;

        /// <summary>SMTP user name. Leave empty for a server that does not need authentication.</summary>
        public string UserName { get; set; } = "";

        /// <summary>SMTP password.</summary>
        public string Password { get; set; } = "";

        /// <summary>Sender address of every email.</summary>
        public string FromAddress { get; set; } = "";

        /// <summary>Sender display name.</summary>
        public string FromName { get; set; } = "WildCat";

        /// <summary>True when there is enough configuration to send through SMTP.</summary>
        public bool IsSmtpConfigured => !string.IsNullOrWhiteSpace(SmtpHost) && !string.IsNullOrWhiteSpace(FromAddress);
    }
}
