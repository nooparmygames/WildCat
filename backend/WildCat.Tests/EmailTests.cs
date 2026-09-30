using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Orleans;
using WildCat.Grains;
using WildCat.Grains.Resources;
using WildCat.Interfaces;
using Xunit;

namespace WildCat.Tests
{
    /// <summary>
    /// Activation and reset emails sent by the Player grain through <see cref="IEmailSender"/>.
    /// </summary>
    [Collection(ClusterCollection.Name)]
    public class EmailTests
    {
        private const string CodeKey = "code";
        private readonly ClusterFixture fixture;
        private IGrainFactory GrainFactory => fixture.Cluster.Client;

        public EmailTests(ClusterFixture fixture)
        {
            this.fixture = fixture;
        }

        [Fact]
        public async Task NewEmailAccountGetsTheActivationCodeByEmail()
        {
            string email = $"{TestSetup.UniqueId("player")}@example.com";
            var created = await GrainFactory.GetGrain<IPlayerCreator>(0).CreateAccountWithEmail(email, "a-password");
            Assert.Equal(AdditionResult.Success, created.result);
            var player = GrainFactory.GetGrain<IPlayer>(created.Account);
            string code = (await player.GetPlayerEmailAccounts())[email][CodeKey];

            var sent = Assert.Single(fixture.Emails.SentTo(email));
            Assert.Equal(EmailTexts.ActivationSubject, sent.Subject);
            Assert.Contains(code, sent.Body);
            Assert.DoesNotContain("{0}", sent.Body);

            Assert.Equal(email, await player.Activate(code));
        }

        [Fact]
        public async Task PasswordResetSendsTheResetCodeByEmail()
        {
            string email = $"{TestSetup.UniqueId("player")}@example.com";
            var created = await GrainFactory.GetGrain<IPlayerCreator>(0).CreateAccountWithEmail(email, "a-password");
            var player = GrainFactory.GetGrain<IPlayer>(created.Account);

            string resetCode = await player.SendPasswordChangeCodeToEmail(email);

            var emails = fixture.Emails.SentTo(email);
            Assert.Equal(2, emails.Count);
            Assert.Equal(EmailTexts.ResetSubject, emails[1].Subject);
            Assert.Contains(resetCode, emails[1].Body);
            Assert.True(await player.ChangePasswordForEmailByCode(resetCode, "another-password"));
        }

        [Fact]
        public void EmailTextsAreEnglishPlainText()
        {
            foreach (string text in new[] { EmailTexts.ActivationSubject, EmailTexts.ActivationBody("ABC123"), EmailTexts.ResetSubject, EmailTexts.ResetBody("ABC123") })
            {
                Assert.False(string.IsNullOrWhiteSpace(text));
                Assert.All(text, c => Assert.True(c < 128, $"Non-ASCII character in \"{text}\""));
                Assert.DoesNotContain("<", text);
            }
            Assert.Contains("ABC123", EmailTexts.ActivationBody("ABC123"));
            Assert.Contains("ABC123", EmailTexts.ResetBody("ABC123"));
        }
    }

    /// <summary>
    /// How the host picks the email sender from the WildCat:Email section.
    /// </summary>
    public class EmailRegistrationTests
    {
        [Fact]
        public void WithoutSmtpSettingsEmailsGoToTheLog()
        {
            var sender = BuildSender(new Dictionary<string, string>
            {
                ["WildCat:Email:SmtpHost"] = "",
                ["WildCat:Email:FromAddress"] = "",
            });
            Assert.IsType<LoggingEmailSender>(sender);
        }

        [Fact]
        public void HostWithoutSenderAddressStillGoesToTheLog()
        {
            var sender = BuildSender(new Dictionary<string, string>
            {
                ["WildCat:Email:SmtpHost"] = "smtp.example.com",
            });
            Assert.IsType<LoggingEmailSender>(sender);
        }

        [Fact]
        public void HostAndSenderAddressSelectSmtp()
        {
            var sender = BuildSender(new Dictionary<string, string>
            {
                ["WildCat:Email:SmtpHost"] = "smtp.example.com",
                ["WildCat:Email:FromAddress"] = "noreply@example.com",
            });
            Assert.IsType<SmtpEmailSender>(sender);
        }

        private static IEmailSender BuildSender(Dictionary<string, string> settings)
        {
            var configuration = new ConfigurationBuilder().AddInMemoryCollection(settings).Build();
            var services = new ServiceCollection();
            services.AddLogging();
            services.AddWildCatEmail(configuration.GetSection(EmailOptions.SectionName));
            using var provider = services.BuildServiceProvider();
            return provider.GetRequiredService<IEmailSender>();
        }
    }
}
