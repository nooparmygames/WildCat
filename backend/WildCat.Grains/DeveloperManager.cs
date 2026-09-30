using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using Orleans;
using WildCat.Interfaces;

namespace WildCat.Grains
{
    public class DeveloperManager : Grain<DeveloperManagerState>, IDeveloperManager
    {

        public override Task OnActivateAsync(CancellationToken cancellationToken)
        {
            return base.OnActivateAsync(cancellationToken);
        }

        public async Task<Guid> AddDeveloper(DeveloperState data)
        {
            if (string.IsNullOrEmpty(data.Email))
                throw new ArgumentException("email is invalid or already exists");
            if (State.developers.Keys.Contains(data.Email))
                return State.developers.First(x => x.Key == data.Email).Value;
            //Set developer ID here and get all others.
            var id = Guid.NewGuid();
            await GrainFactory.GetGrain<IDeveloper>(id).CreateDeveloper(data);
            State.developers.Add(data.Email, id);
            await WriteStateAsync();
            return id;
        }

        public async Task<Guid> AddDeveloper(DeveloperState data, Guid id)
        {
            if (string.IsNullOrEmpty(data.Email) || State.developers.Keys.Contains(data.Email))
                throw new ArgumentException("email is invalid or already exists");
            //Set developer ID here and get all others.
            await GrainFactory.GetGrain<IDeveloper>(id).CreateDeveloper(data);
            State.developers.Add(data.Email, id);
            await WriteStateAsync();
            return id;
        }

        public Task<IDeveloper> GetDeveloper(string email)
        {
            Guid foundDeveloper;
            if (State.developers.TryGetValue(email, out foundDeveloper))
                return Task.FromResult(GrainFactory.GetGrain<IDeveloper>(State.developers[email]));
            return Task.FromResult<IDeveloper>(null);
        }

        public Task<List<IDeveloper>> GetDevelopers()
        {
            return Task.FromResult(State.developers.Select(x => GrainFactory.GetGrain<IDeveloper>(x.Value)).ToList());
        }

        public Task<IDeveloper> GetDeveloperByKey(string key)
        {
            Guid foundDeveloper;
            if (State.SecondaryIndex.TryGetValue(key, out foundDeveloper))
            {
                return Task.FromResult(GrainFactory.GetGrain<IDeveloper>(foundDeveloper));
            }
            return Task.FromResult<IDeveloper>(null);
        }

        public Task AddDeveloperToSIByKey(string key, IDeveloper developer)
        {
            State.SecondaryIndex.Add(key, developer.GetPrimaryKey());
            return WriteStateAsync();
        }

        public Task RemoveDeveloperFromSIKeys(string key)
        {
            State.SecondaryIndex.Remove(key);
            return WriteStateAsync();
        }

        public async Task<DeveloperRegistration> RegisterDeveloper(string email, string password, string firstName, string lastName)
        {
            if (string.IsNullOrWhiteSpace(email))
                throw new ArgumentException("The email cannot be empty.", nameof(email));
            PasswordHasher.EnsureAcceptable(password);
            if (State.developers.Keys.Any(x => string.Equals(x, email, StringComparison.OrdinalIgnoreCase)))
                return null;

            var id = Guid.NewGuid();
            var developer = GrainFactory.GetGrain<IDeveloper>(id);
            // CreateDeveloper replaces the plain password in PasswordHash with its hash.
            await developer.CreateDeveloper(new DeveloperState
            {
                Email = email,
                PasswordHash = password,
                FirstName = firstName,
                LastName = lastName,
                Status = DeveloperState.DeveloperStatus.NotActivated
            });
            State.developers.Add(email, id);
            await WriteStateAsync();
            string code = await developer.CreateActivationCode();
            return new DeveloperRegistration { Key = id, ActivationCode = code };
        }

        public Task<DeveloperSearchResult> SearchDevelopers(string search, int skip, int take)
        {
            IEnumerable<KeyValuePair<string, Guid>> matches = State.developers;
            if (!string.IsNullOrWhiteSpace(search))
            {
                string term = search.Trim();
                matches = matches.Where(x => x.Key.Contains(term, StringComparison.OrdinalIgnoreCase));
            }
            var ordered = matches
                .OrderBy(x => x.Key, StringComparer.OrdinalIgnoreCase)
                .ThenBy(x => x.Key, StringComparer.Ordinal)
                .ToList();
            return Task.FromResult(new DeveloperSearchResult
            {
                Total = ordered.Count,
                Items = ordered
                    .Skip(Math.Max(0, skip))
                    .Take(Math.Max(0, take))
                    .Select(x => new DeveloperIndexEntry { Email = x.Key, Key = x.Value })
                    .ToList()
            });
        }

        public Task SetGameOwner(Guid gameKey, Guid developerKey)
        {
            State.GameOwners ??= new Dictionary<Guid, Guid>();
            if (State.GameOwners.TryGetValue(gameKey, out var existing) && existing == developerKey)
                return Task.CompletedTask;
            State.GameOwners[gameKey] = developerKey;
            return WriteStateAsync();
        }

        public Task RemoveGameOwner(Guid gameKey)
        {
            if (State.GameOwners == null || !State.GameOwners.Remove(gameKey))
                return Task.CompletedTask;
            return WriteStateAsync();
        }

        public Task<Guid> GetGameOwner(Guid gameKey)
        {
            if (State.GameOwners != null && State.GameOwners.TryGetValue(gameKey, out var owner))
                return Task.FromResult(owner);
            return Task.FromResult(Guid.Empty);
        }
    }
}