using System.Net;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Orleans;
using Orleans.TestingHost;
using WildCat.Admin;
using Xunit;

namespace WildCat.Tests
{
    /// <summary>
    /// Rate limiting, options validation, CORS and static panel hosting of the admin API.
    /// </summary>
    [Collection(ClusterCollection.Name)]
    public class AdminApiHostingTests
    {
        private readonly InProcessTestCluster cluster;

        public AdminApiHostingTests(ClusterFixture fixture)
        {
            cluster = fixture.Cluster;
        }

        [Fact]
        public async Task AuthEndpointsAreRateLimitedPerClient()
        {
            await using var host = await AdminApiHost.StartAsync(cluster.Client, authRequestsPerMinute: 3);
            var account = await host.CreateAccountAsync(); // register, activate and login: three requests

            var limited = await host.PostAsync("/api/v1/auth/login", new { email = account.Email, password = account.Password });
            var problem = await AdminApiHost.ReadProblemAsync(limited, HttpStatusCode.TooManyRequests);
            Assert.Contains("Too many requests", problem.GetProperty("detail").GetString());
            Assert.True(limited.Headers.RetryAfter?.Delta > TimeSpan.Zero, "A 429 should say when to retry.");
            await AdminApiHost.ReadProblemAsync(
                await host.PostAsync("/api/v1/auth/request-password-reset", new { email = account.Email }), HttpStatusCode.TooManyRequests);

            // Authenticated endpoints are not part of the auth limit.
            Assert.Equal(HttpStatusCode.OK, (await host.GetAsync("/api/v1/me", account.Token)).StatusCode);
        }

        [Fact]
        public void RateLimitPartitionsGroupIpv6ByNetwork()
        {
            Assert.Equal("unknown", AuthRateLimiterPolicy.PartitionKey(null));
            Assert.Equal("203.0.113.7", AuthRateLimiterPolicy.PartitionKey(IPAddress.Parse("203.0.113.7")));
            Assert.Equal("203.0.113.7", AuthRateLimiterPolicy.PartitionKey(IPAddress.Parse("::ffff:203.0.113.7")));
            Assert.Equal(
                AuthRateLimiterPolicy.PartitionKey(IPAddress.Parse("2001:db8:1:2::1")),
                AuthRateLimiterPolicy.PartitionKey(IPAddress.Parse("2001:db8:1:2:aaaa:bbbb:cccc:dddd")));
            Assert.NotEqual(
                AuthRateLimiterPolicy.PartitionKey(IPAddress.Parse("2001:db8:1:2::1")),
                AuthRateLimiterPolicy.PartitionKey(IPAddress.Parse("2001:db8:1:3::1")));
        }

        [Fact]
        public async Task MissingSigningKeyFailsStartupOutsideDevelopment()
        {
            var exception = await Assert.ThrowsAsync<OptionsValidationException>(() =>
                StartBareAsync("Production", new Dictionary<string, string>()));
            Assert.Contains("JwtSigningKey", exception.Message);

            exception = await Assert.ThrowsAsync<OptionsValidationException>(() =>
                StartBareAsync("Production", new Dictionary<string, string> { ["WildCat:Admin:JwtSigningKey"] = "c2hvcnQ=" }));
            Assert.Contains("at least 32 bytes", exception.Message);

            exception = await Assert.ThrowsAsync<OptionsValidationException>(() =>
                StartBareAsync("Development", new Dictionary<string, string> { ["WildCat:Admin:CorsOrigins:0"] = "localhost:5173" }));
            Assert.Contains("CorsOrigins", exception.Message);
        }

        [Fact]
        public async Task DevelopmentWithoutSigningKeyUsesARandomKey()
        {
            await using var app = await StartBareAsync("Development", new Dictionary<string, string>());
            var client = app.GetTestClient();
            var response = await client.GetAsync("/api/v1/games").WaitAsync(TestSetup.Timeout);
            Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
        }

        [Fact]
        public async Task CorsAllowsTheConfiguredOriginsOnly()
        {
            await using var host = await AdminApiHost.StartAsync(cluster.Client,
                settings: new Dictionary<string, string> { ["WildCat:Admin:CorsOrigins:0"] = "http://localhost:5173" });

            var allowed = new HttpRequestMessage(HttpMethod.Options, "/api/v1/games");
            allowed.Headers.Add("Origin", "http://localhost:5173");
            allowed.Headers.Add("Access-Control-Request-Method", "GET");
            allowed.Headers.Add("Access-Control-Request-Headers", "authorization");
            var response = await host.Client.SendAsync(allowed).WaitAsync(TestSetup.Timeout);
            Assert.Equal(HttpStatusCode.NoContent, response.StatusCode);
            Assert.Equal("http://localhost:5173", Assert.Single(response.Headers.GetValues("Access-Control-Allow-Origin")));

            var refused = new HttpRequestMessage(HttpMethod.Options, "/api/v1/games");
            refused.Headers.Add("Origin", "https://evil.example.com");
            refused.Headers.Add("Access-Control-Request-Method", "GET");
            response = await host.Client.SendAsync(refused).WaitAsync(TestSetup.Timeout);
            Assert.False(response.Headers.Contains("Access-Control-Allow-Origin"));
        }

        [Fact]
        public async Task ServesThePanelWithClientSideRouting()
        {
            string contentRoot = Path.Combine(Path.GetTempPath(), TestSetup.UniqueId("panel"));
            string panel = Path.Combine(contentRoot, "wwwroot", "panel");
            Directory.CreateDirectory(Path.Combine(panel, "assets"));
            await File.WriteAllTextAsync(Path.Combine(panel, "index.html"), "<!doctype html><title>WildCat panel</title>");
            await File.WriteAllTextAsync(Path.Combine(panel, "assets", "app-1234.js"), "console.log('panel');");
            await File.WriteAllTextAsync(Path.Combine(contentRoot, "secret.txt"), "outside the panel folder");
            try
            {
                await using var host = await AdminApiHost.StartAsync(cluster.Client, contentRoot: contentRoot);

                var redirect = await host.GetAsync("/panel");
                Assert.Equal(HttpStatusCode.Redirect, redirect.StatusCode);
                Assert.Equal("/panel/", redirect.Headers.Location?.ToString());

                foreach (var route in new[] { "/panel/", "/panel/games/123", "/panel/activate?email=a%40example.com&code=X" })
                {
                    var page = await host.GetAsync(route);
                    Assert.Equal(HttpStatusCode.OK, page.StatusCode);
                    Assert.Equal("text/html", page.Content.Headers.ContentType?.MediaType);
                    Assert.Contains("WildCat panel", await page.Content.ReadAsStringAsync());
                }

                var script = await host.GetAsync("/panel/assets/app-1234.js");
                Assert.Equal(HttpStatusCode.OK, script.StatusCode);
                Assert.Equal("text/javascript", script.Content.Headers.ContentType?.MediaType);
                Assert.Contains("immutable", script.Headers.CacheControl?.ToString());

                Assert.Equal(HttpStatusCode.NotFound, (await host.GetAsync("/panel/assets/missing.js")).StatusCode);
                Assert.Equal(HttpStatusCode.NotFound, (await host.GetAsync("/panel/..%2Fsecret.txt")).StatusCode);
            }
            finally
            {
                Directory.Delete(contentRoot, recursive: true);
            }
        }

        [Fact]
        public async Task NoPanelFolderMeansNoPanelRoutes()
        {
            await using var host = await AdminApiHost.StartAsync(cluster.Client);
            Assert.Equal(HttpStatusCode.NotFound, (await host.GetAsync("/panel/")).StatusCode);
        }

        private async Task<WebApplication> StartBareAsync(string environment, Dictionary<string, string> settings)
        {
            var builder = WebApplication.CreateBuilder(new WebApplicationOptions
            {
                EnvironmentName = environment,
                ContentRootPath = AppContext.BaseDirectory,
            });
            builder.WebHost.UseTestServer();
            builder.Logging.ClearProviders();
            builder.Configuration.AddInMemoryCollection(settings);
            builder.Services.AddSingleton<IClusterClient>(cluster.Client);
            builder.Services.AddSingleton<IGrainFactory>(cluster.Client);
            builder.Services.AddWildCatAdmin(builder.Configuration);
            var app = builder.Build();
            try
            {
                app.UseAuthentication();
                app.UseAuthorization();
                app.UseRateLimiter();
                app.MapWildCatAdmin();
                await app.StartAsync();
                return app;
            }
            catch
            {
                await app.DisposeAsync();
                throw;
            }
        }
    }
}
