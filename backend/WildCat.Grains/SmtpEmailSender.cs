using System.Net;
using System.Net.Mail;
using System.Text;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using WildCat.Interfaces;

namespace WildCat.Grains
{
    /// <summary>
    /// Sends plain text emails through the SMTP server in <see cref="EmailOptions"/>, using
    /// System.Net.Mail. <see cref="EmailOptions.UseSsl"/> means STARTTLS on the given port (587 by
    /// default); System.Net.Mail does not support implicit TLS on port 465.
    /// </summary>
    public class SmtpEmailSender : IEmailSender
    {
        /// <summary>
        /// Upper bound for one send, well below the 30 second grain call timeout, since the Player
        /// grain waits for the send.
        /// </summary>
        private static readonly TimeSpan SendTimeout = TimeSpan.FromSeconds(20);

        private readonly EmailOptions options;
        private readonly ILogger<SmtpEmailSender> logger;

        public SmtpEmailSender(IOptions<EmailOptions> options, ILogger<SmtpEmailSender> logger)
        {
            this.options = options.Value;
            this.logger = logger;
        }

        public async Task SendAsync(string to, string subject, string body)
        {
            using var message = new MailMessage
            {
                From = new MailAddress(options.FromAddress, options.FromName),
                Subject = subject,
                SubjectEncoding = Encoding.UTF8,
                Body = body,
                BodyEncoding = Encoding.UTF8,
                IsBodyHtml = false,
            };
            message.To.Add(new MailAddress(to));

            // SmtpClient is not safe for concurrent sends, so every send gets its own.
            using var client = new SmtpClient(options.SmtpHost, options.SmtpPort)
            {
                DeliveryMethod = SmtpDeliveryMethod.Network,
                EnableSsl = options.UseSsl,
                UseDefaultCredentials = false,
            };
            if (!string.IsNullOrEmpty(options.UserName))
                client.Credentials = new NetworkCredential(options.UserName, options.Password);

            // SendMailAsync ignores SmtpClient.Timeout, so the limit comes from the token.
            using var timeout = new CancellationTokenSource(SendTimeout);
            await client.SendMailAsync(message, timeout.Token);
            logger.LogDebug("Sent the email \"{Subject}\" through {SmtpHost}", subject, options.SmtpHost);
        }
    }
}
