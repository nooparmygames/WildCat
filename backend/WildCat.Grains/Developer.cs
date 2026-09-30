using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using System.Security.Cryptography;
using Orleans;
using WildCat.Interfaces;
using Newtonsoft.Json.Linq;

namespace WildCat.Grains
{
    public class Developer : Grain<DeveloperState>, IDeveloper
    {
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
            await WriteStateAsync();
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
            await WriteStateAsync();
            return game.Key;
        }

        public async Task<Guid> GenerateSecret()
        {
            AssertIsCreated();
            State.DeveloperSecret = Guid.NewGuid();
            await WriteStateAsync();
            return State.DeveloperSecret;
        }

        public async Task EditGame(Guid gameKey, GameInfo game)
        {
            AssertIsCreated();
            var foundGame = State.GameInfos.FirstOrDefault(x => x.Key == gameKey);
            game.Key = foundGame.Key;
            State.GameInfos.Remove(foundGame);
            State.GameInfos.Add(game);
            await WriteStateAsync();
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
            throw new NotImplementedException();
        }

        /// <summary>
        /// Creates the developer. <see cref="DeveloperState.PasswordHash"/> carries the plain
        /// password on the way in and is replaced by its hash; an empty value creates a developer
        /// without a password (the seeded development accounts), who cannot log in with one.
        /// </summary>
        public Task CreateDeveloper(DeveloperState data)
        {
            data.PasswordHash = string.IsNullOrEmpty(data.PasswordHash) ? "" : PasswordHasher.Hash(data.PasswordHash);
            data.DeveloperSecret = Guid.NewGuid();
            if (data.GameInfos.Count > 0)
                throw new ArgumentException();
            State = data;
            State.IsCreated = true;
            isCreated = true;
            return WriteStateAsync();
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
            await WriteStateAsync();
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
                return WriteStateAsync();
            }
            else
                throw new InvalidOperationException("The game " + game + " doesn't exist in the game list");

        }

        private void AssertIsCreated()
        {
            if (!isCreated)
                throw new InvalidOperationException($"such developer {this.GetPrimaryKey()} is not created");
        }

        public Task<bool> IsPasswordCorrect(string password)
        {
            return Task.FromResult(isCreated && PasswordHasher.Verify(password, State.PasswordHash));
        }

        public Task ChangePassword(string newPassword)
        {
            AssertIsCreated();
            State.PasswordHash = PasswordHasher.Hash(newPassword);
            return WriteStateAsync();
        }

        public Task<bool> IsSecretValid(Guid secret)
        {
            return Task.FromResult(isCreated && SecretsMatch(State.DeveloperSecret, secret));
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