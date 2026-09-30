using System.Collections.Concurrent;
using WildCat.Interfaces;

namespace WildCat.Tests
{
    /// <summary>
    /// An <see cref="IEmailSender"/> for test clusters that keeps every email instead of sending it.
    /// Tests share one instance per cluster, so look emails up by a recipient unique to the test.
    /// </summary>
    public class CapturingEmailSender : IEmailSender
    {
        public record SentEmail(string To, string Subject, string Body);

        private readonly ConcurrentQueue<SentEmail> sent = new ConcurrentQueue<SentEmail>();

        public Task SendAsync(string to, string subject, string body)
        {
            sent.Enqueue(new SentEmail(to, subject, body));
            return Task.CompletedTask;
        }

        /// <summary>The emails sent to <paramref name="to"/>, oldest first.</summary>
        public IReadOnlyList<SentEmail> SentTo(string to)
        {
            return sent.Where(e => string.Equals(e.To, to, StringComparison.OrdinalIgnoreCase)).ToList();
        }
    }
}
