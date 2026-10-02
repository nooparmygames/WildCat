using Microsoft.Extensions.Logging;
using WildCat.Interfaces;

namespace WildCat.Grains
{
    /// <summary>
    /// The email sender used when SMTP is not configured: it writes the recipient and the body,
    /// which holds the activation or reset code, to the log at Warning level instead of sending
    /// anything. Fine for development; in production configure WildCat:Email.
    /// </summary>
    public class LoggingEmailSender : IEmailSender
    {
        private readonly ILogger<LoggingEmailSender> logger;

        public LoggingEmailSender(ILogger<LoggingEmailSender> logger)
        {
            this.logger = logger;
        }

        public Task SendAsync(string to, string subject, string body)
        {
            logger.LogWarning(
                "SMTP is not configured (set {Section}:SmtpHost and FromAddress), so this email was not sent. " +
                "To: {To}. Subject: {Subject}. Body: {Body}",
                EmailOptions.SectionName, to, subject, body);
            return Task.CompletedTask;
        }
    }
}
