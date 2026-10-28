using System.Net;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;
using Microsoft.IdentityModel.JsonWebTokens;
using Microsoft.IdentityModel.Tokens;
using Orleans;
using Orleans.TestingHost;
using WildCat.Admin;
using WildCat.Interfaces;
using Xunit;

namespace WildCat.Tests;

[Collection(ClusterCollection.Name)]
public sealed class AdminSessionRevocationTests(ClusterFixture fixture)
{
    private InProcessTestCluster Cluster => fixture.Cluster;

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task PasswordResetRevokesAllPreviousTokensAndPersistsTheNewStamp(bool isAdmin)
    {
        await using var host = await AdminApiHost.StartAsync(Cluster.Client);
        var account = await host.CreateAccountAsync(isAdmin ? host.AdminEmail : null);
        var secondLogin = await host.LoginAsync(account.Email, account.Password);
        var unaffected = await host.CreateAccountAsync();
        var developer = Cluster.Client.GetGrain<IDeveloper>(account.Key);
        var original = await developer.GetDeveloperData().WaitAsync(TestSetup.Timeout);

        var requested = await host.PostAsync("/api/v1/auth/request-password-reset", new { email = account.Email });
        Assert.Equal(HttpStatusCode.Accepted, requested.StatusCode);
        string code = AdminApiHost.ExtractCode((await host.Emails.WaitForAsync(account.Email, count: 2)).Body);
        Assert.Equal(HttpStatusCode.OK, (await host.GetAsync("/api/v1/me", account.Token)).StatusCode);

        await AdminApiHost.ReadProblemAsync(await host.PostAsync("/api/v1/auth/reset-password",
            new { email = account.Email, code = "WRONGCODE2", newPassword = "a replacement password" }), HttpStatusCode.BadRequest);
        await AdminApiHost.ReadProblemAsync(await host.PostAsync("/api/v1/auth/reset-password",
            new { email = account.Email, code, newPassword = "short" }), HttpStatusCode.BadRequest);
        Assert.Equal(HttpStatusCode.OK, (await host.GetAsync("/api/v1/me", account.Token)).StatusCode);

        var reset = await host.PostAsync("/api/v1/auth/reset-password",
            new { email = account.Email, code, newPassword = "a replacement password" });
        Assert.Equal(HttpStatusCode.NoContent, reset.StatusCode);
        await AssertRevoked(host, account.Token);
        await AssertRevoked(host, secondLogin.Token);
        Assert.Equal(HttpStatusCode.OK, (await host.GetAsync("/api/v1/me", unaffected.Token)).StatusCode);

        await Cluster.Deactivate(developer).WaitAsync(TestSetup.Timeout);
        var reloaded = await developer.GetDeveloperData().WaitAsync(TestSetup.Timeout);
        Assert.NotEqual(Guid.Empty, reloaded.SecurityStamp);
        Assert.NotEqual(original.SecurityStamp, reloaded.SecurityStamp);
        await AssertRevoked(host, account.Token);
        var newLogin = await host.LoginAsync(account.Email, "a replacement password");
        Assert.Equal(HttpStatusCode.OK, (await host.GetAsync("/api/v1/me", newLogin.Token)).StatusCode);
        await AdminApiHost.ReadProblemAsync(await host.PostAsync("/api/v1/auth/reset-password",
            new { email = account.Email, code, newPassword = "another replacement password" }), HttpStatusCode.BadRequest);
        Assert.Equal(HttpStatusCode.OK, (await host.GetAsync("/api/v1/me", newLogin.Token)).StatusCode);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task PasswordChangeRevokesAllPreviousTokensAndPendingResetCodes(bool isAdmin)
    {
        await using var host = await AdminApiHost.StartAsync(Cluster.Client);
        var account = await host.CreateAccountAsync(isAdmin ? host.AdminEmail : null);
        var secondLogin = await host.LoginAsync(account.Email, account.Password);
        var developer = Cluster.Client.GetGrain<IDeveloper>(account.Key);
        string resetCode = await developer.CreatePasswordResetCode().WaitAsync(TestSetup.Timeout);

        await AdminApiHost.ReadProblemAsync(await host.PostAsync("/api/v1/auth/change-password",
            new { oldPassword = "the wrong password", newPassword = "a changed password" }, account.Token), HttpStatusCode.BadRequest);
        await AdminApiHost.ReadProblemAsync(await host.PostAsync("/api/v1/auth/change-password",
            new { oldPassword = account.Password, newPassword = "short" }, account.Token), HttpStatusCode.BadRequest);
        Assert.Equal(HttpStatusCode.OK, (await host.GetAsync("/api/v1/me", account.Token)).StatusCode);

        var changed = await host.PostAsync("/api/v1/auth/change-password",
            new { oldPassword = account.Password, newPassword = "a changed password" }, account.Token);
        Assert.Equal(HttpStatusCode.NoContent, changed.StatusCode);
        await AssertRevoked(host, account.Token);
        await AssertRevoked(host, secondLogin.Token);
        await AdminApiHost.ReadProblemAsync(await host.PostAsync("/api/v1/auth/reset-password",
            new { email = account.Email, code = resetCode, newPassword = "a reset password" }), HttpStatusCode.BadRequest);

        var newLogin = await host.LoginAsync(account.Email, "a changed password");
        Assert.Equal(HttpStatusCode.OK, (await host.GetAsync("/api/v1/me", newLogin.Token)).StatusCode);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("not-a-stamp")]
    [InlineData("00000000000000000000000000000000")]
    public async Task SignedTokensWithoutAValidAccountStampAreRejected(string stamp)
    {
        await using var host = await AdminApiHost.StartAsync(Cluster.Client);
        var account = await host.CreateAccountAsync();
        var claims = new Dictionary<string, object>
        {
            [AdminClaims.Subject] = account.Key.ToString(),
            [AdminClaims.Email] = account.Email,
            [AdminClaims.Role] = AdminClaims.DeveloperRole,
        };
        if (stamp != null)
            claims[AdminClaims.SecurityStamp] = stamp;
        string key = host.App.Services.GetRequiredService<IOptions<WildCatAdminOptions>>().Value.JwtSigningKey;
        var token = new JsonWebTokenHandler().CreateToken(new SecurityTokenDescriptor
        {
            Issuer = AdminTokenService.Issuer,
            Audience = AdminTokenService.Audience,
            IssuedAt = DateTime.UtcNow,
            NotBefore = DateTime.UtcNow,
            Expires = DateTime.UtcNow.AddMinutes(10),
            Claims = claims,
            SigningCredentials = new SigningCredentials(new SymmetricSecurityKey(Convert.FromBase64String(key)), SecurityAlgorithms.HmacSha256),
        });

        await AssertRevoked(host, token);
        Assert.Equal(HttpStatusCode.OK, (await host.GetAsync("/api/v1/me", account.Token)).StatusCode);
    }

    [Fact]
    public async Task AStaleAuthenticatedPasswordChangeCannotOverwriteAResetPassword()
    {
        var developer = Cluster.Client.GetGrain<IDeveloper>(
            await TestSetup.CreateDeveloper(Cluster.Client, "the original password").WaitAsync(TestSetup.Timeout));
        var original = await developer.GetDeveloperData().WaitAsync(TestSetup.Timeout);
        string code = await developer.CreatePasswordResetCode().WaitAsync(TestSetup.Timeout);
        Assert.True(await developer.ResetPassword(code, "the reset password").WaitAsync(TestSetup.Timeout));

        Assert.False(await developer.TryChangePassword("the reset password", "an unwanted password", original.SecurityStamp)
            .WaitAsync(TestSetup.Timeout));
        Assert.True(await developer.IsPasswordCorrect("the reset password").WaitAsync(TestSetup.Timeout));
    }

    [Fact]
    public async Task ConcurrentResetsUseTheCodeOnlyOnce()
    {
        var developer = Cluster.Client.GetGrain<IDeveloper>(
            await TestSetup.CreateDeveloper(Cluster.Client, "the original password").WaitAsync(TestSetup.Timeout));
        string code = await developer.CreatePasswordResetCode().WaitAsync(TestSetup.Timeout);
        var results = await Task.WhenAll(
            developer.ResetPassword(code, "the first reset password"),
            developer.ResetPassword(code, "the second reset password")).WaitAsync(TestSetup.Timeout);

        Assert.Single(results, success => success);
        string expected = results[0] ? "the first reset password" : "the second reset password";
        Assert.True(await developer.IsPasswordCorrect(expected).WaitAsync(TestSetup.Timeout));
    }

    private static async Task AssertRevoked(AdminApiHost host, string token)
    {
        var response = await host.GetAsync("/api/v1/me", token);
        await AdminApiHost.ReadProblemAsync(response, HttpStatusCode.Unauthorized);
        Assert.Contains(response.Headers.WwwAuthenticate, value => value.Scheme == "Bearer");
        await AdminApiHost.ReadProblemAsync(await host.PostAsync("/api/v1/me/secret", null, token), HttpStatusCode.Unauthorized);
    }
}
