using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using Orleans;
using Interfaces;

namespace Collections
{
    public class DeveloperStorage : Grain<DeveloperStorageState>, IDeveloperStorage
    {
        private Guid developerSecret;

        public override async Task OnActivateAsync(CancellationToken cancellationToken)
        {
            var developer = GrainFactory.GetGrain<IDeveloper>(this.GetPrimaryKey());
            var developerData = await developer.GetDeveloperData();
            developerSecret = developerData.DeveloperSecret;
            await base.OnActivateAsync(cancellationToken);
        }

        private void AssertIsSecretCorrect(Guid secret)
        {
            if (secret != developerSecret)
                throw new InvalidOperationException("The secret is not valid");
        }

        public Task<List<Tuple<string, bool>>> GetAllKeys(Guid secret)
        {
            AssertIsSecretCorrect(secret);
            List<Tuple<string, bool>> returnValues = new List<Tuple<string, bool>>();
            foreach (var key in State.StringValues.Keys)
                returnValues.Add(Tuple.Create(key, true));
            foreach (var key in State.BinaryValues.Keys)
                returnValues.Add(Tuple.Create(key, false));
            return Task.FromResult(returnValues);
        }

        public Task<byte[]> GetBinary(string key)
        {
            if (State.BinaryValues.ContainsKey(key))
                return Task.FromResult(State.BinaryValues[key]);
            return Task.FromResult<byte[]>(null);
        }

        public Task<string> GetString(string key)
        {
            if (State.StringValues.ContainsKey(key))
                return Task.FromResult(State.StringValues[key]);
            return Task.FromResult<string>(null);
        }

        public Task SetBinary(string key, byte[] value, Guid secret)
        {
            AssertIsSecretCorrect(secret);
            State.BinaryValues[key] = value;
            return WriteStateAsync();
        }

        public Task SetString(string key, string value, Guid secret)
        {
            AssertIsSecretCorrect(secret);
            State.StringValues[key] = value;
            return WriteStateAsync();
        }

        public Task RemoveString(string key)
        {

            State.StringValues.Remove(key);
            return WriteStateAsync();
        }

        public Task RemoveBinary(string key)
        {
            State.BinaryValues.Remove(key);
            return WriteStateAsync();
        }

        public Task<Dictionary<string, string>> GetAllStringData(Guid secret)
        {
            AssertIsSecretCorrect(secret);
            return Task.FromResult(State.StringValues);
        }
    }

    [GenerateSerializer]
    [Alias("wildcat.DeveloperStorageState")]
    public class DeveloperStorageState
    {
        [Id(0)] public Dictionary<string, string> StringValues { get; set; }
        [Id(1)] public Dictionary<string, byte[]> BinaryValues { get; set; }

        public DeveloperStorageState()
        {
            StringValues = new Dictionary<string, string>();
            BinaryValues = new Dictionary<string, byte[]>();
        }
    }
}
