using Microsoft.Extensions.DependencyInjection;
using Orleans;
using Orleans.Runtime;
using Orleans.Storage;
using Orleans.TestingHost;
using WildCat.Grains;
using WildCat.Interfaces;
using Xunit;

namespace WildCat.Tests
{
    /// <summary>
    /// Email activation and password reset codes: separate per purpose, used up on success,
    /// expiring after Player.EmailCodeLifetime and dropped after Player.MaxEmailCodeAttempts
    /// wrong tries.
    /// </summary>
    [Collection(ClusterCollection.Name)]
    public class EmailCodeTests
    {
        private const string Password = "first-password";
        private readonly InProcessTestCluster cluster;
        private IGrainFactory GrainFactory => cluster.Client;

        public EmailCodeTests(ClusterFixture fixture)
        {
            cluster = fixture.Cluster;
        }

        private async Task<(IPlayer Player, string Email, string ActivationCode)> CreateEmailPlayer()
        {
            string email = $"{TestSetup.UniqueId("player")}@example.com";
            var created = await GrainFactory.GetGrain<IPlayerCreator>(0).CreateAccountWithEmail(email, Password);
            Assert.Equal(AdditionResult.Success, created.result);
            var player = GrainFactory.GetGrain<IPlayer>(created.Account);
            var code = (await player.GetState()).ActivationCodes[email];
            Assert.Matches("^[A-Z0-9]{6}$", code.Code);
            Assert.True(DateTime.UtcNow - code.IssuedAtUtc < TimeSpan.FromMinutes(1));
            Assert.Equal(0, code.FailedAttempts);
            return (player, email, code.Code);
        }

        private async Task<bool> CanLogIn(IPlayer player, string email, string password)
        {
            var (_, _, sessionKey) = await TestSetup.CreateDeveloperGameAndSession(GrainFactory);
            return await player.LoginWithEmail(email, password, sessionKey, Guid.NewGuid()) != null;
        }

        [Fact]
        public async Task ActivationUsesUpTheCodeAndItNeverResetsThePassword()
        {
            var (player, email, activationCode) = await CreateEmailPlayer();
            Assert.DoesNotContain("code", (await player.GetPlayerEmailAccounts())[email].Keys);

            // Before activation the activation code is no reset code either.
            Assert.False(await player.ChangePasswordForEmailByCode(activationCode, "hijacked-password"));

            Assert.Equal(email, await player.Activate(activationCode));
            Assert.Empty((await player.GetState()).ActivationCodes);

            Assert.False(await player.ChangePasswordForEmailByCode(activationCode, "hijacked-password"));
            await Assert.ThrowsAsync<ArgumentException>(() => player.Activate(activationCode));
            Assert.True(await CanLogIn(player, email, Password));
            Assert.False(await CanLogIn(player, email, "hijacked-password"));
        }

        [Fact]
        public async Task ResetCodeIsNoActivationCode()
        {
            var (player, email, _) = await CreateEmailPlayer();

            string resetCode = await player.SendPasswordChangeCodeToEmail(email);

            await Assert.ThrowsAsync<ArgumentException>(() => player.Activate(resetCode));
            Assert.False(await CanLogIn(player, email, Password));
            Assert.True(await player.ChangePasswordForEmailByCode(resetCode, "second-password"));
            Assert.Empty((await player.GetState()).PasswordResetCodes);
        }

        [Fact]
        public async Task NewResetCodeReplacesTheOldOne()
        {
            var (player, email, activationCode) = await CreateEmailPlayer();
            await player.Activate(activationCode);

            string first = await player.SendPasswordChangeCodeToEmail(email);
            string second = await player.SendPasswordChangeCodeToEmail(email);

            if (first != second)
                Assert.False(await player.ChangePasswordForEmailByCode(first, "from-first-code"));
            Assert.True(await player.ChangePasswordForEmailByCode(second, "from-second-code"));
            Assert.True(await CanLogIn(player, email, "from-second-code"));
        }

        [Fact]
        public async Task UnacceptableNewPasswordDoesNotUseUpTheResetCode()
        {
            var (player, email, activationCode) = await CreateEmailPlayer();
            await player.Activate(activationCode);
            string resetCode = await player.SendPasswordChangeCodeToEmail(email);

            await Assert.ThrowsAsync<ArgumentException>(() => player.ChangePasswordForEmailByCode(resetCode, ""));

            Assert.True(await player.ChangePasswordForEmailByCode(resetCode, "second-password"));
        }

        [Fact]
        public async Task ActivationCodeExpires()
        {
            var (player, email, activationCode) = await CreateEmailPlayer();

            await RewriteStoredState(player, state => state.ActivationCodes[email].IssuedAtUtc -= Player.EmailCodeLifetime + TimeSpan.FromMinutes(1));

            await Assert.ThrowsAsync<ArgumentException>(() => player.Activate(activationCode));
            Assert.Empty((await player.GetState()).ActivationCodes);
            Assert.False(await CanLogIn(player, email, Password));
        }

        [Fact]
        public async Task ActivationCodeWorksUntilItExpires()
        {
            var (player, email, activationCode) = await CreateEmailPlayer();

            await RewriteStoredState(player, state => state.ActivationCodes[email].IssuedAtUtc -= Player.EmailCodeLifetime - TimeSpan.FromMinutes(1));

            Assert.Equal(email, await player.Activate(activationCode));
            Assert.True(await CanLogIn(player, email, Password));
        }

        [Fact]
        public async Task ResetCodeExpires()
        {
            var (player, email, activationCode) = await CreateEmailPlayer();
            await player.Activate(activationCode);
            string resetCode = await player.SendPasswordChangeCodeToEmail(email);

            await RewriteStoredState(player, state => state.PasswordResetCodes[email].IssuedAtUtc -= Player.EmailCodeLifetime + TimeSpan.FromMinutes(1));

            Assert.False(await player.ChangePasswordForEmailByCode(resetCode, "second-password"));
            Assert.Empty((await player.GetState()).PasswordResetCodes);
            Assert.True(await CanLogIn(player, email, Password));
        }

        [Fact]
        public async Task TooManyWrongActivationCodesDropTheCode()
        {
            var (player, email, activationCode) = await CreateEmailPlayer();
            string wrong = activationCode == "WRONG1" ? "WRONG2" : "WRONG1";

            for (int i = 0; i < Player.MaxEmailCodeAttempts - 1; ++i)
                await Assert.ThrowsAsync<ArgumentException>(() => player.Activate(wrong));
            Assert.Equal(Player.MaxEmailCodeAttempts - 1, (await player.GetState()).ActivationCodes[email].FailedAttempts);

            // The attempt count is stored, so deactivating the grain does not reset it.
            await cluster.Deactivate(player);
            await Assert.ThrowsAsync<ArgumentException>(() => player.Activate(""));

            await Assert.ThrowsAsync<ArgumentException>(() => player.Activate(activationCode));
            Assert.Empty((await player.GetState()).ActivationCodes);
            Assert.False(await CanLogIn(player, email, Password));
        }

        [Fact]
        public async Task RightActivationCodeStillWorksBeforeTheLimit()
        {
            var (player, email, activationCode) = await CreateEmailPlayer();

            for (int i = 0; i < Player.MaxEmailCodeAttempts - 1; ++i)
                await Assert.ThrowsAsync<ArgumentException>(() => player.Activate(activationCode.ToLowerInvariant() + "x"));

            Assert.Equal(email, await player.Activate(activationCode));
        }

        [Fact]
        public async Task TooManyWrongResetCodesDropTheCode()
        {
            var (player, email, activationCode) = await CreateEmailPlayer();
            await player.Activate(activationCode);
            string resetCode = await player.SendPasswordChangeCodeToEmail(email);
            string wrong = resetCode == "WRONG1" ? "WRONG2" : "WRONG1";

            for (int i = 0; i < Player.MaxEmailCodeAttempts; ++i)
                Assert.False(await player.ChangePasswordForEmailByCode(wrong, "guessed-password"));

            Assert.False(await player.ChangePasswordForEmailByCode(resetCode, "second-password"));
            Assert.True(await CanLogIn(player, email, Password));

            // A new code can be sent and works.
            string newCode = await player.SendPasswordChangeCodeToEmail(email);
            Assert.True(await player.ChangePasswordForEmailByCode(newCode, "second-password"));
            Assert.True(await CanLogIn(player, email, "second-password"));
        }

        private IGrainStorage GetStorage()
        {
            var services = cluster.GetSiloServiceProvider(cluster.Silos[0].SiloAddress);
            return services.GetRequiredKeyedService<IGrainStorage>("Default");
        }

        /// <summary>
        /// Deactivates the player, changes its stored state and writes it back. The next call
        /// activates the player from the changed state.
        /// </summary>
        private async Task RewriteStoredState(IPlayer player, Action<PlayerState> change)
        {
            // Grain<T> stores its state under the name "state".
            const string stateName = "state";
            await cluster.Deactivate(player);
            var state = new GrainState<PlayerState>();
            await GetStorage().ReadStateAsync(stateName, player.GetGrainId(), state).WaitAsync(TestSetup.Timeout);
            Assert.True(state.RecordExists);
            change(state.State);
            await GetStorage().WriteStateAsync(stateName, player.GetGrainId(), state).WaitAsync(TestSetup.Timeout);
        }
    }
}
