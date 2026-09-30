using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using System.Security.Cryptography;
using Orleans;
using Orleans.Runtime;
using WildCat.Interfaces;
using Newtonsoft.Json.Linq;

namespace WildCat.Grains
{
    public class Developer : Grain, IDeveloper
    {
        private readonly IPersistentState<DeveloperState> state;

        public Developer([PersistentState(GrainStateNames.Developer, GrainStorageNames.Default)] IPersistentState<DeveloperState> state)
        {
            this.state = state;
        }

        private DeveloperState State => state.State;

        private bool isCreated;


        public async override Task OnActivateAsync(CancellationToken cancellationToken)
        {
            isCreated = State.IsCreated;
            if (isCreated && State.DeveloperSecret == Guid.Empty)
            {
                await GenerateSecret();
            }
            await base.OnActivateAsync(cancellationToken);
        }

        public async Task<Guid> AddGame(GameInfo game)
        {
            AssertIsCreated();
            if (string.IsNullOrWhiteSpace(game.Name))
                throw new ArgumentException("The game name cannot be null or empty");
            if (State.GameInfos.Any(x => x.Name == game.Name))
                throw new InvalidOperationException("A game with the same name already exists");
            game.Key = Guid.NewGuid();
            State.GameInfos.Add(game);
            await state.WriteStateAsync();
            return game.Key;
        }

        public async Task<Guid> AddGame(GameInfo game,Guid id)
        {
            AssertIsCreated();
            if (string.IsNullOrWhiteSpace(game.Name))
                throw new ArgumentException("The game name cannot be null or empty");
            if (State.GameInfos.Any(x => x.Name == game.Name))
                throw new InvalidOperationException("A game with the same name already exists");
            game.Key = id;
            State.GameInfos.Add(game);
            await state.WriteStateAsync();
            return game.Key;
        }

        public async Task<Guid> GenerateSecret()
        {
            AssertIsCreated();
            State.DeveloperSecret = Guid.NewGuid();
            await state.WriteStateAsync();
            return State.DeveloperSecret;
        }

        public async Task EditGame(Guid gameKey, GameInfo game)
        {
            AssertIsCreated();
            var foundGame = State.GameInfos.FirstOrDefault(x => x.Key == gameKey);
            game.Key = foundGame.Key;
            State.GameInfos.Remove(foundGame);
            State.GameInfos.Add(game);
            await state.WriteStateAsync();
        }

        public async Task<Guid> InitializeSession(Guid gameKey)
        {
            AssertIsCreated();
            bool isGameValid = IsGameValidSync(gameKey);
            if (!isGameValid)
                throw new InvalidOperationException($"The game key {gameKey} is not valid");
            var ticket = Guid.NewGuid();
            var session = GrainFactory.GetGrain<ISession>(ticket);
            await session.InitializeSession(this.GetPrimaryKey(), gameKey);
            return ticket;
        }

        public Task ChangeName(string first, string last)
        {
            AssertIsCreated();
            State.FirstName = first;
            State.LastName = last;
            return state.WriteStateAsync();
        }

        /// <summary>
        /// Creates the developer. <see cref="DeveloperState.PasswordHash"/> carries the plain
        /// password on the way in and is replaced by its hash; an empty value creates a developer
        /// without a password (the seeded development accounts), who cannot log in with one.
        /// </summary>
        public async Task CreateDeveloper(DeveloperState data)
        {
            if (data.GameInfos.Count > 0)
                throw new ArgumentException();
            data.PasswordHash = string.IsNullOrEmpty(data.PasswordHash) ? "" : await PasswordHasher.HashAsync(data.PasswordHash);
            data.DeveloperSecret = Guid.NewGuid();
            state.State = data;
            State.IsCreated = true;
            isCreated = true;
            await state.WriteStateAsync();
        }

        public Task<DeveloperState> GetDeveloperData()
        {
            AssertIsCreated();
            return Task.FromResult(State);
        }

        public async Task UpdateDeveloperData(DeveloperState data)
        {
            AssertIsCreated();
            State.Address = data.Address;
            State.FirstName = data.FirstName;
            State.LastName = data.LastName;
            State.Description = data.Description;
            State.PhoneNumber = data.PhoneNumber;
            State.AdditionalData = data.AdditionalData;
            await state.WriteStateAsync();
        }

        public Task<IEnumerable<GameInfo>> GetGames()
        {
            AssertIsCreated();
            return Task.FromResult<IEnumerable<GameInfo>>(State.GameInfos.ToList().AsReadOnly());
        }

        public Task<bool> IsGameValid(Guid gameKey)
        {
            AssertIsCreated();
            var isGameAvailable = State.GameInfos.Any(x => x.Key == gameKey);
            return Task.FromResult(isGameAvailable);
        }

        public bool IsGameValidSync(Guid gameKey)
        {
            AssertIsCreated();
            var isGameAvailable = State.GameInfos.Any(x => x.Key == gameKey);
            return isGameAvailable;
        }

        public Task RemoveGame(Guid game)
        {
            AssertIsCreated();
            var gameToRemove = State.GameInfos.FirstOrDefault(x => x.Key == game);
            if (gameToRemove != null)
            {
                State.GameInfos.Remove(gameToRemove);
                return state.WriteStateAsync();
            }
            else
                throw new InvalidOperationException("The game " + game + " doesn't exist in the game list");

        }

        private void AssertIsCreated()
        {
            if (!isCreated)
                throw new InvalidOperationException($"such developer {this.GetPrimaryKey()} is not created");
        }

        public async Task<bool> IsPasswordCorrect(string password)
        {
            return isCreated && await PasswordHasher.VerifyAsync(password, State.PasswordHash);
        }

        public async Task ChangePassword(string newPassword)
        {
            AssertIsCreated();
            State.PasswordHash = await PasswordHasher.HashAsync(newPassword);
            await state.WriteStateAsync();
        }

        public Task<bool> IsSecretValid(Guid secret)
        {
            return Task.FromResult(isCreated && SecretsMatch(State.DeveloperSecret, secret));
        }

        public async Task<string> CreateActivationCode()
        {
            AssertIsCreated();
            if (State.Status != DeveloperState.DeveloperStatus.NotActivated)
                return null;
            string code = NewCode();
            State.ActivationCode = NewCodeState(code, ActivationCodeLifetime);
            await state.WriteStateAsync();
            return code;
        }

        public async Task<bool> Activate(string code)
        {
            AssertIsCreated();
            if (State.Status == DeveloperState.DeveloperStatus.Banned)
                return false;

            // The code is checked whatever the status, so the answer does not tell a caller without
            // the code whether the account is active. The code that activated the account stays
            // until it expires, so opening the activation link again still succeeds.
            var stored = State.ActivationCode;
            int failedAttempts = stored?.FailedAttempts ?? 0;
            bool valid = CheckCode(stored, code);
            bool changed = stored != null && stored.FailedAttempts != failedAttempts;
            if (valid && State.Status == DeveloperState.DeveloperStatus.NotActivated)
            {
                State.Status = DeveloperState.DeveloperStatus.Normal;
                changed = true;
            }
            else if (!valid && stored != null && stored.FailedAttempts >= MaxCodeAttempts)
            {
                State.ActivationCode = null;
                changed = true;
            }
            if (changed)
                await state.WriteStateAsync();
            return valid;
        }

        public async Task<string> CreatePasswordResetCode()
        {
            AssertIsCreated();
            string code = NewCode();
            State.PasswordResetCode = NewCodeState(code, PasswordResetCodeLifetime);
            await state.WriteStateAsync();
            return code;
        }

        public async Task<bool> ConsumePasswordResetCode(string code)
        {
            AssertIsCreated();
            bool valid = CheckCode(State.PasswordResetCode, code);
            if (valid || (State.PasswordResetCode != null && State.PasswordResetCode.FailedAttempts >= MaxCodeAttempts))
                State.PasswordResetCode = null;
            await state.WriteStateAsync();
            return valid;
        }

        private const int MaxCodeAttempts = 5;
        private const int CodeLength = 10;
        // No 0/O or 1/I, so a code read from an email is easy to type.
        private const string CodeAlphabet = "ABCDEFGHJKLMNPQRSTUVWXYZ23456789";
        private static readonly TimeSpan ActivationCodeLifetime = TimeSpan.FromDays(7);
        private static readonly TimeSpan PasswordResetCodeLifetime = TimeSpan.FromHours(1);

        private static string NewCode() => RandomNumberGenerator.GetString(CodeAlphabet, CodeLength);

        private static OneTimeCodeState NewCodeState(string code, TimeSpan lifetime) => new OneTimeCodeState
        {
            Hash = HashOneTimeCode(code),
            ExpiresAtUtc = DateTime.UtcNow + lifetime,
            FailedAttempts = 0
        };

        private static string HashOneTimeCode(string code)
        {
            string normalized = (code ?? "").Trim().ToUpperInvariant();
            return Convert.ToBase64String(SHA256.HashData(System.Text.Encoding.UTF8.GetBytes(normalized)));
        }

        /// <summary>
        /// True when <paramref name="given"/> matches an unexpired code. A mismatch counts as a failed
        /// attempt; the caller writes state and drops the code after <see cref="MaxCodeAttempts"/>.
        /// </summary>
        private static bool CheckCode(OneTimeCodeState stored, string given)
        {
            if (stored == null || string.IsNullOrEmpty(stored.Hash) || string.IsNullOrWhiteSpace(given))
                return false;
            if (stored.ExpiresAtUtc < DateTime.UtcNow || stored.FailedAttempts >= MaxCodeAttempts)
                return false;
            byte[] expected = Convert.FromBase64String(stored.Hash);
            byte[] actual = Convert.FromBase64String(HashOneTimeCode(given));
            if (CryptographicOperations.FixedTimeEquals(expected, actual))
                return true;
            stored.FailedAttempts++;
            return false;
        }

        /// <summary>
        /// Compares two developer secrets in constant time. Guid.Empty never matches, so a developer
        /// whose secret was never generated cannot be used with an empty secret.
        /// </summary>
        private static bool SecretsMatch(Guid expected, Guid given)
        {
            if (expected == Guid.Empty)
                return false;
            Span<byte> a = stackalloc byte[16];
            Span<byte> b = stackalloc byte[16];
            expected.TryWriteBytes(a);
            given.TryWriteBytes(b);
            return CryptographicOperations.FixedTimeEquals(a, b);
        }
    }

}