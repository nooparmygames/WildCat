using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using Orleans;
using Orleans.Runtime;
using WildCat.Interfaces;

namespace WildCat.Grains
{
    /// <summary>
    /// Publisher key/value data. Game clients read single values without a secret (messages 34/35);
    /// everything else needs the developer secret, which is checked against the Developer grain on
    /// every call so a regenerated secret takes effect at once.
    /// </summary>
    public class DeveloperStorage : Grain, IDeveloperStorage
    {
        private readonly IPersistentState<DeveloperStorageState> state;

        public DeveloperStorage([PersistentState(GrainStateNames.DeveloperStorage, GrainStorageNames.Default)] IPersistentState<DeveloperStorageState> state)
        {
            this.state = state;
        }

        private DeveloperStorageState State => state.State;

        /// <summary>
        /// Throws <see cref="InvalidOperationException"/> for a wrong secret; the gateway maps that
        /// exception type to its "Secret is wrong" response.
        /// </summary>
        private async Task AssertIsSecretCorrect(Guid secret)
        {
            bool valid = await GrainFactory.GetGrain<IDeveloper>(this.GetPrimaryKey()).IsSecretValid(secret);
            if (!valid)
                throw new InvalidOperationException("The secret is not valid");
        }

        public async Task<List<Tuple<string, bool>>> GetAllKeys(Guid secret)
        {
            await AssertIsSecretCorrect(secret);
            List<Tuple<string, bool>> returnValues = new List<Tuple<string, bool>>();
            foreach (var key in State.StringValues.Keys)
                returnValues.Add(Tuple.Create(key, true));
            foreach (var key in State.BinaryValues.Keys)
                returnValues.Add(Tuple.Create(key, false));
            return returnValues;
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

        public async Task<string> GetStringForDeveloper(string key, Guid secret)
        {
            await AssertIsSecretCorrect(secret);
            return State.StringValues.TryGetValue(key, out var value) ? value : null;
        }

        public async Task SetBinary(string key, byte[] value, Guid secret)
        {
            await AssertIsSecretCorrect(secret);
            State.BinaryValues[key] = value;
            await state.WriteStateAsync();
        }

        public async Task SetString(string key, string value, Guid secret)
        {
            await AssertIsSecretCorrect(secret);
            State.StringValues[key] = value;
            await state.WriteStateAsync();
        }

        public async Task RemoveString(string key, Guid secret)
        {
            await AssertIsSecretCorrect(secret);
            if (State.StringValues.Remove(key))
                await state.WriteStateAsync();
        }

        public async Task RemoveBinary(string key, Guid secret)
        {
            await AssertIsSecretCorrect(secret);
            if (State.BinaryValues.Remove(key))
                await state.WriteStateAsync();
        }

        public async Task<Dictionary<string, string>> GetAllStringData(Guid secret)
        {
            await AssertIsSecretCorrect(secret);
            return State.StringValues;
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
