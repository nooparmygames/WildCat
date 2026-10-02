using System.Collections.Concurrent;
using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Security.Cryptography;
using System.Text.Json;
using System.Text.RegularExpressions;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Orleans;
using WildCat.Admin;
using WildCat.Interfaces;
using Xunit;

namespace WildCat.Tests
{
    /// <summary>
    /// A minimal web app on TestServer that hosts only the admin API, wired like WildCat.Host
    /// (AddWildCatAdmin, UseAuthentication, UseAuthorization, UseRateLimiter, MapWildCatAdmin), with
    /// the shared test cluster's client as IClusterClient/IGrainFactory and a capturing email sender.
    /// Each host gets its own random signing key and its own admin email.
    /// </summary>
    internal sealed class AdminApiHost : IAsyncDisposable
    {
        public static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web);

        private AdminApiHost(WebApplication app, CapturingEmailSender emails, string adminEmail)
        {
            App = app;
            Emails = emails;
            AdminEmail = adminEmail;
            Client = app.GetTestClient();
        }

        public WebApplication App { get; }
        public HttpClient Client { get; }
        public CapturingEmailSender Emails { get; }
        public string AdminEmail { get; }

        public static async Task<AdminApiHost> StartAsync(IClusterClient cluster, int authRequestsPerMinute = 10_000,
            string contentRoot = null, IDictionary<string, string> settings = null)
        {
            var builder = WebApplication.CreateBuilder(new WebApplicationOptions
            {
                EnvironmentName = "Production",
                ContentRootPath = contentRoot ?? AppContext.BaseDirectory,
            });
            builder.WebHost.UseTestServer();
            builder.Logging.ClearProviders();

            string adminEmail = $"{TestSetup.UniqueId("admin")}@example.com";
            var configuration = new Dictionary<string, string>
            {
                ["WildCat:Admin:JwtSigningKey"] = Convert.ToBase64String(RandomNumberGenerator.GetBytes(32)),
                ["WildCat:Admin:AdminEmails:0"] = adminEmail,
                ["WildCat:Admin:AuthRequestsPerMinute"] = authRequestsPerMinute.ToString(),
                ["WildCat:Admin:PanelBaseUrl"] = "https://games.example.com",
            };
            foreach (var setting in settings ?? new Dictionary<string, string>())
                configuration[setting.Key] = setting.Value;
            builder.Configuration.AddInMemoryCollection(configuration);

            var emails = new CapturingEmailSender();
            builder.Services.AddSingleton<IClusterClient>(cluster);
            builder.Services.AddSingleton<IGrainFactory>(cluster);
            builder.Services.AddSingleton<IEmailSender>(emails);
            builder.Services.AddWildCatAdmin(builder.Configuration);

            var app = builder.Build();
            app.UseAuthentication();
            app.UseAuthorization();
            app.UseRateLimiter();
            app.MapWildCatAdmin();
            await app.StartAsync();
            return new AdminApiHost(app, emails, adminEmail);
        }

        public async ValueTask DisposeAsync()
        {
            Client.Dispose();
            await App.StopAsync();
            await App.DisposeAsync();
        }

        public Task<HttpResponseMessage> SendAsync(HttpMethod method, string url, string token = null, object body = null)
        {
            var request = new HttpRequestMessage(method, url);
            if (token != null)
                request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token);
            if (body != null)
                request.Content = JsonContent.Create(body, body.GetType(), options: Json);
            return Client.SendAsync(request).WaitAsync(TestSetup.Timeout);
        }

        public Task<HttpResponseMessage> GetAsync(string url, string token = null) => SendAsync(HttpMethod.Get, url, token);
        public Task<HttpResponseMessage> PostAsync(string url, object body, string token = null) => SendAsync(HttpMethod.Post, url, token, body ?? new { });
        public Task<HttpResponseMessage> PutAsync(string url, object body, string token = null) => SendAsync(HttpMethod.Put, url, token, body);
        public Task<HttpResponseMessage> DeleteAsync(string url, string token = null) => SendAsync(HttpMethod.Delete, url, token);

        /// <summary>
        /// Registers, activates with the emailed code and logs in. Returns the token and the key.
        /// </summary>
        public async Task<TestAccount> CreateAccountAsync(string email = null, string password = "correct horse battery")
        {
            email ??= $"{TestSetup.UniqueId("dev")}@example.com";
            var register = await PostAsync("/api/v1/auth/register", new { email, password, firstName = "Test", lastName = "Developer" });
            Assert.Equal(HttpStatusCode.Accepted, register.StatusCode);
            string code = ExtractCode((await Emails.WaitForAsync(email)).Body);
            var activate = await PostAsync("/api/v1/auth/activate", new { email, code });
            Assert.Equal(HttpStatusCode.NoContent, activate.StatusCode);
            var login = await LoginAsync(email, password);
            return new TestAccount(email, password, login.Token, login.Developer.Key);
        }

        public async Task<LoginResponse> LoginAsync(string email, string password)
        {
            var response = await PostAsync("/api/v1/auth/login", new { email, password });
            Assert.Equal(HttpStatusCode.OK, response.StatusCode);
            return await ReadAsync<LoginResponse>(response);
        }

        public static async Task<T> ReadAsync<T>(HttpResponseMessage response)
        {
            return await response.Content.ReadFromJsonAsync<T>(Json);
        }

        /// <summary>
        /// Reads a ProblemDetails body and checks its status and content type.
        /// </summary>
        public static async Task<JsonElement> ReadProblemAsync(HttpResponseMessage response, HttpStatusCode expected)
        {
            Assert.Equal(expected, response.StatusCode);
            Assert.Equal("application/problem+json", response.Content.Headers.ContentType?.MediaType);
            var problem = await response.Content.ReadFromJsonAsync<JsonElement>(Json);
            Assert.Equal((int)expected, problem.GetProperty("status").GetInt32());
            return problem;
        }

        public static string ExtractCode(string body)
        {
            var match = Regex.Match(body, @"(?m)^([A-HJ-NP-Z2-9]{10})\r?$");
            Assert.True(match.Success, "No code in the email: " + body);
            return match.Groups[1].Value;
        }
    }

    internal sealed record TestAccount(string Email, string Password, string Token, Guid Key);
}
