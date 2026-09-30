using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using Orleans;
using Interfaces;

namespace Collections
{
    public class DeveloperManager : Grain<DeveloperManagerState>, IDeveloperManager
    {

        public override Task OnActivateAsync()
        {
            return base.OnActivateAsync();
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
    }
}