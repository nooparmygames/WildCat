using System;
using System.Collections.Concurrent;
using System.Linq;
using System.Text.RegularExpressions;
using System.Threading.Tasks;
using WildCat.Grains.Resources;
using WildCat.Interfaces;

namespace WildCat.Sdk.Tests
{
    /// <summary>Keeps the emails the grains send, so tests can read activation and reset codes.</summary>
    public sealed class CapturingEmailSender : IEmailSender
    {
        private readonly ConcurrentQueue<(string To, string Subject, string Body)> sent = new ConcurrentQueue<(string, string, string)>();

        public Task SendAsync(string to, string subject, string body)
        {
            sent.Enqueue((to, subject, body));
            return Task.CompletedTask;
        }

        /// <summary>Waits for the activation email to <paramref name="to"/> and returns its code.</summary>
        public async Task<string> WaitForActivationCodeAsync(string to)
        {
            string pattern = Regex.Escape(EmailTexts.ActivationBody("CODEPLACEHOLDER")).Replace("CODEPLACEHOLDER", "([A-Z0-9]{6})");
            DateTime deadline = DateTime.UtcNow + Wait.Long;
            while (true)
            {
                var email = sent.LastOrDefault(e => string.Equals(e.To, to, StringComparison.OrdinalIgnoreCase));
                if (email.Body != null)
                {
                    Match match = Regex.Match(email.Body, pattern);
                    if (match.Success)
                        return match.Groups[1].Value;
                }
                if (DateTime.UtcNow > deadline)
                    throw new TimeoutException($"No activation email reached {to}.");
                await Task.Delay(20);
            }
        }
    }
}
