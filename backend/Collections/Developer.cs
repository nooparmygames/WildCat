using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Orleans;
using Interfaces;
using Newtonsoft.Json.Linq;

namespace Collections
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

        public Task CreateDeveloper(DeveloperState data)
        {
            data.PasswordHash = data.PasswordHash.ToSha1();
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

        public Task<bool> IsPasswordCorrect(string passwordHash)
        {
            var Sha1edPassword = passwordHash.ToSha1();
            return Task.FromResult(State.PasswordHash == Sha1edPassword);
        }



        public async Task<AccountingCollection> GetAccountingInfo(DateTime StartDate, DateTime EndDate)
        {
            var accountingResult = new AccountingCollection
            {
                StartDate = StartDate,
                EndDate = EndDate,
                Games = new List<AccountingInfo>()
            };
            foreach (var g in State.GameInfos)
            {
                var gk = g.Key;
                var data = await GrainFactory.GetGrain<IAccountingSystem>(gk, InvoiceUtility.GetInvoiceMonthKey(EndDate), null).GetData();
                accountingResult.Games.Add(new AccountingInfo
                {
                    GameName = g.Name,
                    GameKey = g.Key.ToString(),
                    MessageCount = data.MessageCount,
                    CallCount = (int)data.CallCount
                });
            }
            //var dataList = await queryResult.ToListAsync();
            return accountingResult;
        }

        public Task ChangePassword(string newPassword)
        {
            State.PasswordHash = newPassword.ToSha1();
            return WriteStateAsync();
        }
    }

}