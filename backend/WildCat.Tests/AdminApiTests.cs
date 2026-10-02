using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Orleans.TestingHost;
using WildCat.Admin;
using Xunit;

namespace WildCat.Tests
{
    /// <summary>
    /// The admin REST API end to end on TestServer against the shared test cluster. Every test
    /// registers its own developers with unique emails, so tests do not depend on each other.
    /// </summary>
    [Collection(ClusterCollection.Name)]
    public class AdminApiTests : IAsyncLifetime
    {
        private readonly InProcessTestCluster cluster;
        private AdminApiHost host;

        public AdminApiTests(ClusterFixture fixture)
        {
            cluster = fixture.Cluster;
        }

        public async Task InitializeAsync()
        {
            host = await AdminApiHost.StartAsync(cluster.Client);
        }

        public async Task DisposeAsync()
        {
            await host.DisposeAsync();
        }

        [Fact]
        public async Task RegisterActivateLoginAndManageTheProfile()
        {
            string email = $"{TestSetup.UniqueId("flow")}@example.com";
            const string password = "first password 1";

            var register = await host.PostAsync("/api/v1/auth/register", new { email = email.ToUpperInvariant(), password, firstName = "Ada", lastName = "Lovelace" });
            Assert.Equal(HttpStatusCode.Accepted, register.StatusCode);
            var activationEmail = await host.Emails.WaitForAsync(email);
            Assert.Contains("Hello Ada,", activationEmail.Body);
            string code = AdminApiHost.ExtractCode(activationEmail.Body);
            Assert.Contains($"https://games.example.com/panel/activate?email={Uri.EscapeDataString(email)}&code={code}", activationEmail.Body);

            var notActivated = await AdminApiHost.ReadProblemAsync(
                await host.PostAsync("/api/v1/auth/login", new { email, password }), HttpStatusCode.Forbidden);
            Assert.Contains("not activated", notActivated.GetProperty("detail").GetString());

            await AdminApiHost.ReadProblemAsync(
                await host.PostAsync("/api/v1/auth/activate", new { email, code = "WRONGCODE2" }), HttpStatusCode.BadRequest);
            var activate = await host.PostAsync("/api/v1/auth/activate", new { email, code = code.ToLowerInvariant() });
            Assert.Equal(HttpStatusCode.NoContent, activate.StatusCode);

            var wrongPassword = await AdminApiHost.ReadProblemAsync(
                await host.PostAsync("/api/v1/auth/login", new { email, password = "not the password" }), HttpStatusCode.Unauthorized);
            var unknownEmail = await AdminApiHost.ReadProblemAsync(
                await host.PostAsync("/api/v1/auth/login", new { email = "nobody-" + email, password }), HttpStatusCode.Unauthorized);
            Assert.Equal(wrongPassword.GetProperty("detail").GetString(), unknownEmail.GetProperty("detail").GetString());

            var loginResponse = await host.PostAsync("/api/v1/auth/login", new { email, password });
            Assert.Equal(HttpStatusCode.OK, loginResponse.StatusCode);
            string loginJson = await loginResponse.Content.ReadAsStringAsync();
            Assert.DoesNotContain("secret", loginJson, StringComparison.OrdinalIgnoreCase);
            Assert.DoesNotContain("hash", loginJson, StringComparison.OrdinalIgnoreCase);
            var login = JsonSerializer.Deserialize<LoginResponse>(loginJson, AdminApiHost.Json);
            Assert.False(string.IsNullOrEmpty(login.Token));
            Assert.InRange(login.ExpiresAt - DateTimeOffset.UtcNow, TimeSpan.FromHours(7.9), TimeSpan.FromHours(8));
            Assert.Equal(email, login.Developer.Email);
            Assert.Equal("Ada", login.Developer.FirstName);
            Assert.False(login.Developer.IsAdmin);

            var meResponse = await host.GetAsync("/api/v1/me", login.Token);
            string meJson = await meResponse.Content.ReadAsStringAsync();
            Assert.DoesNotContain("hash", meJson, StringComparison.OrdinalIgnoreCase);
            var me = JsonSerializer.Deserialize<MeResponse>(meJson, AdminApiHost.Json);
            Assert.Equal(login.Developer.Key, me.Key);
            Assert.NotEqual(Guid.Empty, me.Secret);

            var renamed = await AdminApiHost.ReadAsync<MeResponse>(
                await host.PutAsync("/api/v1/me", new { firstName = "Grace", lastName = "Hopper" }, login.Token));
            Assert.Equal("Grace", renamed.FirstName);
            Assert.Equal("Hopper", renamed.LastName);

            var regenerated = await AdminApiHost.ReadAsync<SecretResponse>(await host.PostAsync("/api/v1/me/secret", null, login.Token));
            Assert.NotEqual(me.Secret, regenerated.Secret);
            Assert.Equal(regenerated.Secret, (await AdminApiHost.ReadAsync<MeResponse>(await host.GetAsync("/api/v1/me", login.Token))).Secret);

            await AdminApiHost.ReadProblemAsync(await host.PostAsync("/api/v1/auth/change-password",
                new { oldPassword = "not the password", newPassword = "second password 2" }, login.Token), HttpStatusCode.BadRequest);
            var changed = await host.PostAsync("/api/v1/auth/change-password",
                new { oldPassword = password, newPassword = "second password 2" }, login.Token);
            Assert.Equal(HttpStatusCode.NoContent, changed.StatusCode);
            await AdminApiHost.ReadProblemAsync(await host.PostAsync("/api/v1/auth/login", new { email, password }), HttpStatusCode.Unauthorized);
            await host.LoginAsync(email, "second password 2");
        }

        [Fact]
        public async Task RegisterRejectsInvalidInputAndTakenEmails()
        {
            var invalid = await AdminApiHost.ReadProblemAsync(
                await host.PostAsync("/api/v1/auth/register", new { email = "not an email", password = "short" }), HttpStatusCode.BadRequest);
            var errors = invalid.GetProperty("errors").EnumerateArray().Select(x => x.GetString()).ToList();
            Assert.Equal(2, errors.Count);
            Assert.Contains(errors, x => x.Contains("email"));
            Assert.Contains(errors, x => x.Contains("at least 8"));
            Assert.False(string.IsNullOrEmpty(invalid.GetProperty("detail").GetString()));

            var account = await host.CreateAccountAsync();
            var taken = await AdminApiHost.ReadProblemAsync(
                await host.PostAsync("/api/v1/auth/register", new { email = account.Email.ToUpperInvariant(), password = "another password" }),
                HttpStatusCode.Conflict);
            Assert.Contains("already exists", taken.GetProperty("detail").GetString());

            var malformed = new HttpRequestMessage(HttpMethod.Post, "/api/v1/auth/register")
            {
                Content = new StringContent("{ not json", System.Text.Encoding.UTF8, "application/json")
            };
            await AdminApiHost.ReadProblemAsync(await host.Client.SendAsync(malformed), HttpStatusCode.BadRequest);
        }

        [Fact]
        public async Task PasswordResetUsesTheEmailedCodeOnce()
        {
            var account = await host.CreateAccountAsync();

            string unknown = $"{TestSetup.UniqueId("nobody")}@example.com";
            Assert.Equal(HttpStatusCode.Accepted, (await host.PostAsync("/api/v1/auth/request-password-reset", new { email = unknown })).StatusCode);

            Assert.Equal(HttpStatusCode.Accepted, (await host.PostAsync("/api/v1/auth/request-password-reset", new { email = account.Email })).StatusCode);
            // The first email to this address was the activation code.
            var resetEmail = await host.Emails.WaitForAsync(account.Email, count: 2);
            Assert.Contains("reset", resetEmail.Subject, StringComparison.OrdinalIgnoreCase);
            string code = AdminApiHost.ExtractCode(resetEmail.Body);
            Assert.Contains($"https://games.example.com/panel/reset-password?email={Uri.EscapeDataString(account.Email)}&code={code}", resetEmail.Body);

            await AdminApiHost.ReadProblemAsync(await host.PostAsync("/api/v1/auth/reset-password",
                new { email = account.Email, code = "WRONGCODE2", newPassword = "brand new password" }), HttpStatusCode.BadRequest);
            await AdminApiHost.ReadProblemAsync(await host.PostAsync("/api/v1/auth/reset-password",
                new { email = account.Email, code, newPassword = "short" }), HttpStatusCode.BadRequest);
            var reset = await host.PostAsync("/api/v1/auth/reset-password", new { email = account.Email, code, newPassword = "brand new password" });
            Assert.Equal(HttpStatusCode.NoContent, reset.StatusCode);

            await AdminApiHost.ReadProblemAsync(await host.PostAsync("/api/v1/auth/login",
                new { email = account.Email, password = account.Password }), HttpStatusCode.Unauthorized);
            await host.LoginAsync(account.Email, "brand new password");
            await AdminApiHost.ReadProblemAsync(await host.PostAsync("/api/v1/auth/reset-password",
                new { email = account.Email, code, newPassword = "yet another password" }), HttpStatusCode.BadRequest);

            await Task.Delay(200);
            Assert.Empty(host.Emails.SentTo(unknown));
        }

        [Fact]
        public async Task GamesLeaderboardsGameModesAndPublisherDataRoundTrip()
        {
            var account = await host.CreateAccountAsync();
            string token = account.Token;

            var create = await host.PostAsync("/api/v1/games",
                new { name = "Space Cats", description = "A test game", studioName = "Example Studio", website = "https://example.com", genre = 3, region = 1, platform = "Android", ageRating = 2 },
                token);
            Assert.Equal(HttpStatusCode.Created, create.StatusCode);
            Guid gameKey = (await AdminApiHost.ReadAsync<CreatedKeyResponse>(create)).Key;
            Assert.EndsWith($"/api/v1/games/{gameKey}", create.Headers.Location?.ToString());
            await AdminApiHost.ReadProblemAsync(await host.PostAsync("/api/v1/games", new { name = "Space Cats" }, token), HttpStatusCode.Conflict);

            var games = await AdminApiHost.ReadAsync<List<GameResponse>>(await host.GetAsync("/api/v1/games", token));
            var game = Assert.Single(games);
            Assert.Equal(new GameResponse(gameKey, "Space Cats", "A test game", "Example Studio", "https://example.com", 3, 1, "Android", 2), game);

            var updated = await AdminApiHost.ReadAsync<GameResponse>(await host.PutAsync($"/api/v1/games/{gameKey}",
                new { name = "Space Cats 2", genre = 0, region = 0, ageRating = 0 }, token));
            Assert.Equal("Space Cats 2", updated.Name);
            Assert.Null(updated.Description);
            Assert.Equal("Space Cats 2", (await AdminApiHost.ReadAsync<GameResponse>(await host.GetAsync($"/api/v1/games/{gameKey}", token))).Name);

            string games1 = $"/api/v1/games/{gameKey}";

            // Leaderboards
            var board = await host.PostAsync($"{games1}/leaderboards", new { name = "weekly", bestScore = true }, token);
            Assert.Equal(HttpStatusCode.Created, board.StatusCode);
            await host.PostAsync($"{games1}/leaderboards", new { name = "last-run", bestScore = false }, token);
            await AdminApiHost.ReadProblemAsync(await host.PostAsync($"{games1}/leaderboards", new { name = "weekly", bestScore = false }, token), HttpStatusCode.Conflict);
            await AdminApiHost.ReadProblemAsync(await host.PostAsync($"{games1}/leaderboards", new { name = "has space", bestScore = false }, token), HttpStatusCode.BadRequest);
            var boards = await AdminApiHost.ReadAsync<List<LeaderboardResponse>>(await host.GetAsync($"{games1}/leaderboards", token));
            Assert.Equal(
                new[] { new LeaderboardResponse("last-run", $"{gameKey}_L_last-run", false), new LeaderboardResponse("weekly", $"{gameKey}_L_weekly", true) },
                boards);
            Assert.Equal(HttpStatusCode.NoContent, (await host.DeleteAsync($"{games1}/leaderboards/weekly", token)).StatusCode);
            await AdminApiHost.ReadProblemAsync(await host.DeleteAsync($"{games1}/leaderboards/weekly", token), HttpStatusCode.NotFound);
            Assert.Single(await AdminApiHost.ReadAsync<List<LeaderboardResponse>>(await host.GetAsync($"{games1}/leaderboards", token)));

            // Game modes
            var mode = new
            {
                minPlayers = 2,
                maxPlayers = 4,
                criteria = new[]
                {
                    new { isMinPlayersEnough = false, acceptableThreshold = 100, startTimeInSeconds = 0 },
                    new { isMinPlayersEnough = true, acceptableThreshold = 500, startTimeInSeconds = 10 },
                }
            };
            var savedMode = await AdminApiHost.ReadAsync<GameModeResponse>(await host.PutAsync($"{games1}/game-modes/ranked", mode, token));
            Assert.Equal(4, savedMode.MaxPlayers);
            Assert.True(savedMode.FinishGameWithOneDisconnect);
            var modes = await AdminApiHost.ReadAsync<List<GameModeResponse>>(await host.GetAsync($"{games1}/game-modes", token));
            var listedMode = Assert.Single(modes);
            Assert.Equal("ranked", listedMode.Name);
            Assert.Equal(new[] { new MatchmakingCriterionDto(false, 100, 0), new MatchmakingCriterionDto(true, 500, 10) }, listedMode.Criteria);

            var badMode = await AdminApiHost.ReadProblemAsync(await host.PutAsync($"{games1}/game-modes/broken", new
            {
                minPlayers = 1,
                maxPlayers = 4,
                criteria = new[] { new { isMinPlayersEnough = false, acceptableThreshold = 100, startTimeInSeconds = 5 } }
            }, token), HttpStatusCode.BadRequest);
            var modeErrors = badMode.GetProperty("errors").EnumerateArray().Select(x => x.GetString()).ToList();
            Assert.Contains(modeErrors, x => x.Contains("minimum player count"));
            Assert.Contains(modeErrors, x => x.Contains("0 seconds"));

            Assert.Equal(HttpStatusCode.NoContent, (await host.DeleteAsync($"{games1}/game-modes/ranked", token)).StatusCode);
            Assert.Empty(await AdminApiHost.ReadAsync<List<GameModeResponse>>(await host.GetAsync($"{games1}/game-modes", token)));
            await AdminApiHost.ReadProblemAsync(await host.DeleteAsync($"{games1}/game-modes/ranked", token), HttpStatusCode.NotFound);

            // Publisher data
            var entry = await AdminApiHost.ReadAsync<PublisherDataEntry>(await host.PutAsync("/api/v1/publisher-data/motd", new { value = "Hello players" }, token));
            Assert.Equal(new PublisherDataEntry("motd", "Hello players"), entry);
            await host.PutAsync("/api/v1/publisher-data/config", new { value = "{}" }, token);
            var data = await AdminApiHost.ReadAsync<List<PublisherDataEntry>>(await host.GetAsync("/api/v1/publisher-data", token));
            Assert.Equal(new[] { new PublisherDataEntry("config", "{}"), new PublisherDataEntry("motd", "Hello players") }, data);
            Assert.Equal(HttpStatusCode.NoContent, (await host.DeleteAsync("/api/v1/publisher-data/motd", token)).StatusCode);
            await AdminApiHost.ReadProblemAsync(await host.DeleteAsync("/api/v1/publisher-data/motd", token), HttpStatusCode.NotFound);
            Assert.Single(await AdminApiHost.ReadAsync<List<PublisherDataEntry>>(await host.GetAsync("/api/v1/publisher-data", token)));

            // Delete the game
            Assert.Equal(HttpStatusCode.NoContent, (await host.DeleteAsync(games1, token)).StatusCode);
            await AdminApiHost.ReadProblemAsync(await host.GetAsync(games1, token), HttpStatusCode.NotFound);
            Assert.Empty(await AdminApiHost.ReadAsync<List<GameResponse>>(await host.GetAsync("/api/v1/games", token)));
        }

        [Fact]
        public async Task ItemsRoundTripWithPrices()
        {
            var account = await host.CreateAccountAsync();
            string token = account.Token;
            Guid gameKey = (await AdminApiHost.ReadAsync<CreatedKeyResponse>(await host.PostAsync("/api/v1/games", new { name = "Shop" }, token))).Key;
            string items = $"/api/v1/games/{gameKey}/items";

            var coin = await AdminApiHost.ReadAsync<ItemResponse>(await host.PutAsync($"{items}/coin", new
            {
                description = "Soft currency",
                stackable = true,
                maxAllowedToHave = 100000,
                metaData = new Dictionary<string, string> { ["icon"] = "coin.png" },
            }, token));
            Assert.Equal("coin", coin.Name);
            Assert.Empty(coin.Price);

            await AdminApiHost.ReadProblemAsync(await host.PutAsync($"{items}/ship", new
            {
                price = new[] { new { item = "gem", count = 5 } },
            }, token), HttpStatusCode.BadRequest);

            var ship = await AdminApiHost.ReadAsync<ItemResponse>(await host.PutAsync($"{items}/ship", new
            {
                description = "A ship",
                effectTime = "1.02:03:04",
                isConsumable = true,
                automaticallyConsumed = false,
                isRegenerating = true,
                regenerationAmount = 1,
                regenerationPeriod = "00:10:00",
                price = new[] { new { item = "coin", count = 250 } },
            }, token));
            Assert.Equal(new TimeSpan(1, 2, 3, 4), ship.EffectTime);
            Assert.Equal(TimeSpan.FromMinutes(10), ship.RegenerationPeriod);
            string shipJson = await (await host.GetAsync(items, token)).Content.ReadAsStringAsync();
            Assert.Contains("\"effectTime\":\"1.02:03:04\"", shipJson);
            Assert.Contains("\"regenerationPeriod\":\"00:10:00\"", shipJson);

            var badDuration = await AdminApiHost.ReadProblemAsync(await host.PutAsync($"{items}/gem", new { effectTime = "-00:00:01" }, token), HttpStatusCode.BadRequest);
            Assert.Contains("effect time", badDuration.GetProperty("detail").GetString());
            Assert.Equal(new[] { new ItemPrice("coin", 250) }, ship.Price);

            var listed = await AdminApiHost.ReadAsync<List<ItemResponse>>(await host.GetAsync(items, token));
            Assert.Equal(new[] { "coin", "ship" }, listed.Select(x => x.Name));
            Assert.Equal("coin.png", listed[0].MetaData["icon"]);
            Assert.Equal(new[] { new ItemPrice("coin", 250) }, listed[1].Price);

            var edited = await AdminApiHost.ReadAsync<ItemResponse>(await host.PutAsync($"{items}/coin", new { description = "Changed" }, token));
            Assert.Equal("Changed", edited.Description);
            Assert.Equal(2, (await AdminApiHost.ReadAsync<List<ItemResponse>>(await host.GetAsync(items, token))).Count);

            Assert.Equal(HttpStatusCode.NoContent, (await host.DeleteAsync($"{items}/ship", token)).StatusCode);
            await AdminApiHost.ReadProblemAsync(await host.DeleteAsync($"{items}/ship", token), HttpStatusCode.NotFound);
            Assert.Single(await AdminApiHost.ReadAsync<List<ItemResponse>>(await host.GetAsync(items, token)));
        }

        [Fact]
        public async Task ScriptEventsAreValidatedAndCompiled()
        {
            var account = await host.CreateAccountAsync();
            string token = account.Token;
            Guid gameKey = (await AdminApiHost.ReadAsync<CreatedKeyResponse>(await host.PostAsync("/api/v1/games", new { name = "Scripted" }, token))).Key;
            string events = $"/api/v1/games/{gameKey}/events";

            var parameters = new[] { new { name = "a", type = 3 }, new { name = "b", type = 3 } };
            var saved = await host.PutAsync($"{events}/add", new { parameters, script = "async Task<int> add(int a, int b) { return a + b; }" }, token);
            Assert.Equal(HttpStatusCode.OK, saved.StatusCode);
            var savedEvent = await AdminApiHost.ReadAsync<ScriptEventResponse>(saved);
            Assert.Equal(new[] { new EventParameterDto("a", 3), new EventParameterDto("b", 3) }, savedEvent.Parameters);

            // A script without a method named after the event is rejected with the compiler's reason,
            // for a new event and for an existing one, and the stored script stays as it was.
            var rejectedNew = await AdminApiHost.ReadProblemAsync(await host.PutAsync($"{events}/other",
                new { parameters, script = "async Task<int> add(int a, int b) { return a + b; }" }, token), HttpStatusCode.BadRequest);
            Assert.Contains("other", rejectedNew.GetProperty("errors")[0].GetString());
            var rejectedEdit = await AdminApiHost.ReadProblemAsync(await host.PutAsync($"{events}/add",
                new { parameters, script = "async Task<int> sum(int a, int b) { return a + b; }" }, token), HttpStatusCode.BadRequest);
            Assert.Contains("WC1007", rejectedEdit.GetProperty("errors")[0].GetString());
            var forbiddenType = await AdminApiHost.ReadProblemAsync(await host.PutAsync($"{events}/add",
                new { parameters, script = "async Task<int> add(int a, int b) { System.IO.File.Delete(\"x\"); return a + b; }" }, token), HttpStatusCode.BadRequest);
            Assert.Contains("System.IO.File", forbiddenType.GetProperty("detail").GetString());

            await AdminApiHost.ReadProblemAsync(await host.PutAsync($"{events}/add",
                new { parameters = new[] { new { name = "a", type = 9 } }, script = "async Task add(decimal a) { }" }, token), HttpStatusCode.BadRequest);

            var edited = await host.PutAsync($"{events}/add", new { parameters, script = "async Task<int> add(int a, int b) { return a * b; }" }, token);
            Assert.Equal(HttpStatusCode.OK, edited.StatusCode);

            var listed = await AdminApiHost.ReadAsync<List<ScriptEventResponse>>(await host.GetAsync(events, token));
            var only = Assert.Single(listed);
            Assert.Equal("add", only.Name);
            Assert.Contains("a * b", only.Script);

            var recreated = await host.PutAsync($"{events}/add", new { parameters, script = "async Task<int> add(int a, int b) { return a - b; }" }, token);
            Assert.Equal(HttpStatusCode.OK, recreated.StatusCode);
            Assert.Single(await AdminApiHost.ReadAsync<List<ScriptEventResponse>>(await host.GetAsync(events, token)));
        }

        [Fact]
        public async Task ActivateDoesNotRevealWhetherAnAccountIsActive()
        {
            string email = $"{TestSetup.UniqueId("probe")}@example.com";
            Assert.Equal(HttpStatusCode.Accepted, (await host.PostAsync("/api/v1/auth/register", new { email, password = "a long password" })).StatusCode);
            string code = AdminApiHost.ExtractCode((await host.Emails.WaitForAsync(email)).Body);

            string wrongWhileInactive = (await AdminApiHost.ReadProblemAsync(
                await host.PostAsync("/api/v1/auth/activate", new { email, code = "WRONGCODE2" }), HttpStatusCode.BadRequest)).GetProperty("detail").GetString();
            Assert.Equal(HttpStatusCode.NoContent, (await host.PostAsync("/api/v1/auth/activate", new { email, code })).StatusCode);

            // A wrong code for the now active account gets the same answer as before, and as an
            // unknown email; only the code that activated it succeeds again.
            string wrongWhileActive = (await AdminApiHost.ReadProblemAsync(
                await host.PostAsync("/api/v1/auth/activate", new { email, code = "WRONGCODE3" }), HttpStatusCode.BadRequest)).GetProperty("detail").GetString();
            string unknownEmail = (await AdminApiHost.ReadProblemAsync(
                await host.PostAsync("/api/v1/auth/activate", new { email = "nobody-" + email, code = "WRONGCODE3" }), HttpStatusCode.BadRequest)).GetProperty("detail").GetString();
            Assert.Equal(wrongWhileInactive, wrongWhileActive);
            Assert.Equal(wrongWhileInactive, unknownEmail);
            Assert.Equal(HttpStatusCode.NoContent, (await host.PostAsync("/api/v1/auth/activate", new { email, code })).StatusCode);
        }

        [Fact]
        public async Task AnExistingScriptEventCanChangeItsParameterList()
        {
            var account = await host.CreateAccountAsync();
            Guid gameKey = (await AdminApiHost.ReadAsync<CreatedKeyResponse>(await host.PostAsync("/api/v1/games", new { name = "Params" }, account.Token))).Key;
            string url = $"/api/v1/games/{gameKey}/events/add";
            var two = new[] { new { name = "a", type = 3 }, new { name = "b", type = 3 } };
            var three = new[] { new { name = "a", type = 3 }, new { name = "b", type = 3 }, new { name = "c", type = 3 } };
            Assert.Equal(HttpStatusCode.OK, (await host.PutAsync(url, new { parameters = two, script = "int add(int a, int b) { return a + b; }" }, account.Token)).StatusCode);

            // The script is checked against the new parameter list, not the stored one.
            var widened = await host.PutAsync(url, new { parameters = three, script = "int add(int a, int b, int c) { return a + b + c; }" }, account.Token);
            Assert.Equal(HttpStatusCode.OK, widened.StatusCode);
            var saved = await AdminApiHost.ReadAsync<ScriptEventResponse>(widened);
            Assert.Equal(new[] { "a", "b", "c" }, saved.Parameters.Select(p => p.Name).ToArray());

            // A script that does not match the new list is rejected, and the stored event stays.
            var mismatch = await AdminApiHost.ReadProblemAsync(await host.PutAsync(url,
                new { parameters = two, script = "int add(int a, int b, int c) { return a + b + c; }" }, account.Token), HttpStatusCode.BadRequest);
            Assert.Contains("parameter", mismatch.GetProperty("detail").GetString());

            var (runner, session) = await CreateRunner(account.Key, gameKey);
            Assert.Equal(6, (int)await runner.Run(session, "add", 1, 2, 3).WaitAsync(TestSetup.Timeout));

            // Back to two parameters.
            Assert.Equal(HttpStatusCode.OK, (await host.PutAsync(url, new { parameters = two, script = "int add(int a, int b) { return a * b; }" }, account.Token)).StatusCode);
            Assert.Equal(12, (int)await runner.Run(session, "add", 3, 4).WaitAsync(TestSetup.Timeout));
        }

        [Fact]
        public async Task DeletingAScriptEventDeletesItsGrainStateAndCompiledCopy()
        {
            var account = await host.CreateAccountAsync();
            Guid gameKey = (await AdminApiHost.ReadAsync<CreatedKeyResponse>(await host.PostAsync("/api/v1/games", new { name = "Deleted" }, account.Token))).Key;
            string events = $"/api/v1/games/{gameKey}/events";
            var parameters = new[] { new { name = "a", type = 3 } };
            Assert.Equal(HttpStatusCode.OK, (await host.PutAsync($"{events}/twice", new { parameters, script = "int twice(int a) { return a * 2; }" }, account.Token)).StatusCode);
            var compiler = cluster.Client.GetGrain<WildCat.Interfaces.IScriptEventCompiler>(
                WildCat.Interfaces.ResourcesUtility.GetEventScriptKey(gameKey.ToString(), "twice"));
            var info = await compiler.GetInfo();
            var cacheKey = new WildCat.Scripting.ScriptCacheKey(info.GameKey, info.Name, info.ScriptHash);
            var (runner, session) = await CreateRunner(account.Key, gameKey);
            Assert.Equal(10, (int)await runner.Run(session, "twice", 5).WaitAsync(TestSetup.Timeout));
            Assert.True(WildCat.Scripting.ScriptCache.Shared.Contains(cacheKey));

            Assert.Equal(HttpStatusCode.NoContent, (await host.DeleteAsync($"{events}/twice", account.Token)).StatusCode);

            Assert.Empty(await AdminApiHost.ReadAsync<List<ScriptEventResponse>>(await host.GetAsync(events, account.Token)));
            await AdminApiHost.ReadProblemAsync(await host.DeleteAsync($"{events}/twice", account.Token), HttpStatusCode.NotFound);
            Assert.False(WildCat.Scripting.ScriptCache.Shared.Contains(cacheKey));
            var gone = await Assert.ThrowsAsync<WildCat.Interfaces.ScriptEventNotFoundException>(() => compiler.GetInfo());
            Assert.Contains("does not exist", gone.Message);
            // Gone from storage too, not only from the activation.
            await cluster.Deactivate(compiler);
            await Assert.ThrowsAsync<WildCat.Interfaces.ScriptEventNotFoundException>(() => compiler.GetInfo());
            var diagnostics = await compiler.GetDiagnostics();
            Assert.False(diagnostics.Success);

            // A client that still runs the event gets a clean error instead of the old code.
            var failed = await Assert.ThrowsAsync<WildCat.Interfaces.ScriptEventNotFoundException>(() => runner.Run(session, "twice", 5).WaitAsync(TestSetup.Timeout));
            Assert.Contains("does not exist", failed.Message);

            // The name can be used again, with a different parameter list.
            Assert.Equal(HttpStatusCode.OK, (await host.PutAsync($"{events}/twice", new { parameters = Array.Empty<object>(), script = "int twice() { return 2; }" }, account.Token)).StatusCode);
            Assert.Equal(2, (int)await runner.Run(session, "twice").WaitAsync(TestSetup.Timeout));
        }

        /// <summary>
        /// A logged-in player on a new session of the game and the script runner the gateway would
        /// use for that player.
        /// </summary>
        private async Task<(WildCat.Interfaces.IScriptEventRunner Runner, WildCat.Interfaces.ISession Session)> CreateRunner(Guid developerKey, Guid gameKey)
        {
            var grains = cluster.Client;
            var sessionKey = await grains.GetGrain<WildCat.Interfaces.IDeveloper>(developerKey).InitializeSession(gameKey);
            var player = await TestSetup.CreateLoggedInPlayer(grains, sessionKey, Guid.NewGuid());
            var runner = grains.GetGrain<WildCat.Interfaces.IScriptEventRunner>(gameKey, player.GetPrimaryKeyString(), null);
            return (runner, grains.GetGrain<WildCat.Interfaces.ISession>(sessionKey));
        }

        [Fact]
        public async Task RequestsWithoutAValidTokenOrAccessAreRefused()
        {
            var noToken = await AdminApiHost.ReadProblemAsync(await host.GetAsync("/api/v1/games"), HttpStatusCode.Unauthorized);
            Assert.Contains("token", noToken.GetProperty("detail").GetString());
            await AdminApiHost.ReadProblemAsync(await host.GetAsync("/api/v1/me", "not-a-token"), HttpStatusCode.Unauthorized);
            await AdminApiHost.ReadProblemAsync(await host.GetAsync("/api/v1/nothing-here"), HttpStatusCode.NotFound);

            var owner = await host.CreateAccountAsync();
            var other = await host.CreateAccountAsync();
            Guid gameKey = (await AdminApiHost.ReadAsync<CreatedKeyResponse>(await host.PostAsync("/api/v1/games", new { name = "Mine" }, owner.Token))).Key;

            await AdminApiHost.ReadProblemAsync(await host.GetAsync($"/api/v1/games/{gameKey}", other.Token), HttpStatusCode.Forbidden);
            await AdminApiHost.ReadProblemAsync(await host.GetAsync($"/api/v1/games/{gameKey}/leaderboards", other.Token), HttpStatusCode.Forbidden);
            await AdminApiHost.ReadProblemAsync(await host.DeleteAsync($"/api/v1/games/{gameKey}", other.Token), HttpStatusCode.Forbidden);
            await AdminApiHost.ReadProblemAsync(await host.GetAsync($"/api/v1/games/{Guid.NewGuid()}", other.Token), HttpStatusCode.NotFound);
            await AdminApiHost.ReadProblemAsync(await host.GetAsync("/api/v1/admin/developers", other.Token), HttpStatusCode.Forbidden);

            // The admin (by email in WildCat:Admin:AdminEmails) may list developers and open any game.
            var admin = await host.CreateAccountAsync(host.AdminEmail);
            var adminLogin = await host.LoginAsync(admin.Email, admin.Password);
            Assert.True(adminLogin.Developer.IsAdmin);
            Assert.True((await AdminApiHost.ReadAsync<MeResponse>(await host.GetAsync("/api/v1/me", admin.Token))).IsAdmin);
            Assert.Equal("Mine", (await AdminApiHost.ReadAsync<GameResponse>(await host.GetAsync($"/api/v1/games/{gameKey}", admin.Token))).Name);

            string search = owner.Email.Split('@')[0];
            var page = await AdminApiHost.ReadAsync<DeveloperPage>(await host.GetAsync($"/api/v1/admin/developers?search={search}", admin.Token));
            Assert.Equal(1, page.Total);
            var listed = Assert.Single(page.Items);
            Assert.Equal(new AdminDeveloperResponse(owner.Key, owner.Email, "Test", "Developer", true), listed);

            var all = await AdminApiHost.ReadAsync<DeveloperPage>(await host.GetAsync("/api/v1/admin/developers?page=1&pageSize=2", admin.Token));
            Assert.True(all.Total >= 3);
            Assert.Equal(2, all.Items.Count);
            await AdminApiHost.ReadProblemAsync(await host.GetAsync("/api/v1/admin/developers?pageSize=500", admin.Token), HttpStatusCode.BadRequest);
        }

        [Fact]
        public async Task GameValidationErrorsListEveryProblem()
        {
            var account = await host.CreateAccountAsync();
            var problem = await AdminApiHost.ReadProblemAsync(await host.PostAsync("/api/v1/games",
                new { name = "", genre = 9, region = -1, ageRating = 4, website = "ftp://example.com" }, account.Token), HttpStatusCode.BadRequest);
            var errors = problem.GetProperty("errors").EnumerateArray().Select(x => x.GetString()).ToList();
            Assert.Equal(5, errors.Count);
            Assert.Contains(errors, x => x.Contains("name is required"));
            Assert.Contains(errors, x => x.Contains("genre"));
            Assert.Contains(errors, x => x.Contains("region"));
            Assert.Contains(errors, x => x.Contains("age rating"));
            Assert.Contains(errors, x => x.Contains("website"));
        }

        [Fact]
        public async Task OpenApiDocumentListsTheRoutes()
        {
            var response = await host.GetAsync("/openapi/v1.json");
            Assert.Equal(HttpStatusCode.OK, response.StatusCode);
            var document = await response.Content.ReadFromJsonAsync<JsonElement>();
            Assert.Equal("WildCat admin API", document.GetProperty("info").GetProperty("title").GetString());
            Assert.True(document.GetProperty("components").GetProperty("securitySchemes").TryGetProperty("Bearer", out _));

            var paths = document.GetProperty("paths");
            var expected = new Dictionary<string, string[]>
            {
                ["/api/v1/auth/register"] = ["post"],
                ["/api/v1/auth/activate"] = ["post"],
                ["/api/v1/auth/resend-activation"] = ["post"],
                ["/api/v1/auth/login"] = ["post"],
                ["/api/v1/auth/request-password-reset"] = ["post"],
                ["/api/v1/auth/reset-password"] = ["post"],
                ["/api/v1/auth/change-password"] = ["post"],
                ["/api/v1/me"] = ["get", "put"],
                ["/api/v1/me/secret"] = ["post"],
                ["/api/v1/games"] = ["get", "post"],
                ["/api/v1/games/{gameKey}"] = ["get", "put", "delete"],
                ["/api/v1/games/{gameKey}/leaderboards"] = ["get", "post"],
                ["/api/v1/games/{gameKey}/leaderboards/{name}"] = ["delete"],
                ["/api/v1/games/{gameKey}/items"] = ["get"],
                ["/api/v1/games/{gameKey}/items/{name}"] = ["put", "delete"],
                ["/api/v1/games/{gameKey}/events"] = ["get"],
                ["/api/v1/games/{gameKey}/events/{name}"] = ["put", "delete"],
                ["/api/v1/games/{gameKey}/game-modes"] = ["get"],
                ["/api/v1/games/{gameKey}/game-modes/{name}"] = ["put", "delete"],
                ["/api/v1/publisher-data"] = ["get"],
                ["/api/v1/publisher-data/{key}"] = ["put", "delete"],
                ["/api/v1/admin/developers"] = ["get"],
            };
            foreach (var (path, methods) in expected)
            {
                Assert.True(paths.TryGetProperty(path, out var item), $"The OpenAPI document has no {path}");
                var routeParameters = System.Text.RegularExpressions.Regex.Matches(path, @"\{(\w+)\}").Select(x => x.Groups[1].Value).ToList();
                foreach (var method in methods)
                {
                    Assert.True(item.TryGetProperty(method, out var operation), $"The OpenAPI document has no {method} {path}");
                    var declared = operation.TryGetProperty("parameters", out var parameters)
                        ? parameters.EnumerateArray().Where(x => x.GetProperty("in").GetString() == "path").Select(x => x.GetProperty("name").GetString()).ToList()
                        : new List<string>();
                    Assert.Equal(routeParameters.OrderBy(x => x), declared.OrderBy(x => x));
                }
            }
            Assert.Equal(expected.Count, paths.EnumerateObject().Count());

            var login = paths.GetProperty("/api/v1/auth/login").GetProperty("post");
            Assert.False(login.TryGetProperty("security", out _));
            Assert.True(paths.GetProperty("/api/v1/games").GetProperty("get").TryGetProperty("security", out _));
        }
    }
}
