using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;
using WildCat.Gateway;
using Xunit;

namespace WildCat.Tests
{
    /// <summary>
    /// GameGatewayOptions: the origin check and how the options bind and validate.
    /// </summary>
    public class GatewayOptionsTests
    {
        private static readonly string[] Allowed = { "https://game.example.com", "http://localhost:8080/" };

        [Theory]
        [InlineData("https://game.example.com")]
        [InlineData("https://GAME.Example.COM")]
        [InlineData("https://game.example.com/")]
        [InlineData("http://localhost:8080")]
        [InlineData("HTTP://LOCALHOST:8080/")]
        public void ListedOriginIsAllowedIgnoringCaseAndATrailingSlash(string origin)
        {
            Assert.True(GatewayExtensions.IsOriginAllowed(origin, Allowed));
        }

        [Theory]
        [InlineData("https://evil.example.com")]
        [InlineData("http://game.example.com")]
        [InlineData("https://game.example.com:8443")]
        [InlineData("https://sub.game.example.com")]
        [InlineData("https://game.example.com.evil.example")]
        [InlineData("http://localhost:8081")]
        [InlineData("null")]
        public void UnlistedOriginIsRefused(string origin)
        {
            Assert.False(GatewayExtensions.IsOriginAllowed(origin, Allowed));
        }

        [Theory]
        [InlineData(null)]
        [InlineData("")]
        public void RequestWithoutAnOriginIsAllowed(string origin)
        {
            // Native clients send no Origin header; the check only concerns browsers.
            Assert.True(GatewayExtensions.IsOriginAllowed(origin, Allowed));
        }

        [Theory]
        [InlineData("https://anything.example.com")]
        [InlineData("null")]
        [InlineData("")]
        public void EmptyOrMissingListAllowsEveryOrigin(string origin)
        {
            Assert.True(GatewayExtensions.IsOriginAllowed(origin, Array.Empty<string>()));
            Assert.True(GatewayExtensions.IsOriginAllowed(origin, null));
        }

        [Fact]
        public void NullEntryInTheListMatchesNothing()
        {
            var allowed = new string[] { null, "https://game.example.com" };
            Assert.False(GatewayExtensions.IsOriginAllowed("https://evil.example.com", allowed));
            Assert.True(GatewayExtensions.IsOriginAllowed("https://game.example.com", allowed));
        }

        [Fact]
        public void DefaultsMatchTheDocumentedValues()
        {
            var options = new GameGatewayOptions();
            Assert.Equal(1024 * 1024, options.MaxMessageBytes);
            Assert.Empty(options.AllowedOrigins);
            Assert.Equal(5, options.AuthAttemptBurst);
            Assert.Equal(6, options.AuthAttemptsPerMinute);
            Assert.Equal(1024, options.OutboundQueueCapacity);
            Assert.Equal(8, options.MaxConcurrentRequests);
        }

        [Fact]
        public void OptionsBindFromTheGatewaySection()
        {
            var configuration = new ConfigurationBuilder().AddInMemoryCollection(new Dictionary<string, string>
            {
                ["Gateway:MaxMessageBytes"] = "4096",
                ["Gateway:AllowedOrigins:0"] = "https://game.example.com",
                ["Gateway:AuthAttemptBurst"] = "3",
                ["Gateway:AuthAttemptsPerMinute"] = "2",
                ["Gateway:OutboundQueueCapacity"] = "16",
                ["Gateway:MaxConcurrentRequests"] = "3",
            }).Build();
            var services = new ServiceCollection();
            services.AddGameGateway(configuration.GetSection(GameGatewayOptions.SectionName));
            using var provider = services.BuildServiceProvider();

            var options = provider.GetRequiredService<IOptions<GameGatewayOptions>>().Value;
            Assert.Equal(4096, options.MaxMessageBytes);
            Assert.Equal(new[] { "https://game.example.com" }, options.AllowedOrigins);
            Assert.Equal(3, options.AuthAttemptBurst);
            Assert.Equal(2, options.AuthAttemptsPerMinute);
            Assert.Equal(16, options.OutboundQueueCapacity);
            Assert.Equal(3, options.MaxConcurrentRequests);
            Assert.Same(TimeProvider.System, provider.GetRequiredService<TimeProvider>());
        }

        [Theory]
        [InlineData("Gateway:AuthAttemptBurst", "0")]
        [InlineData("Gateway:AuthAttemptsPerMinute", "0")]
        [InlineData("Gateway:MaxMessageBytes", "0")]
        [InlineData("Gateway:OutboundQueueCapacity", "0")]
        [InlineData("Gateway:MaxConcurrentRequests", "-1")]
        public void NonPositiveLimitsFailValidation(string key, string value)
        {
            var configuration = new ConfigurationBuilder().AddInMemoryCollection(new Dictionary<string, string>
            {
                [key] = value,
            }).Build();
            var services = new ServiceCollection();
            services.AddGameGateway(configuration.GetSection(GameGatewayOptions.SectionName));
            using var provider = services.BuildServiceProvider();

            Assert.Throws<OptionsValidationException>(() => provider.GetRequiredService<IOptions<GameGatewayOptions>>().Value);
        }
    }
}
