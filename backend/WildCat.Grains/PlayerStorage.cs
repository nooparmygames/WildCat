using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using Orleans;
using Orleans.Runtime;
using WildCat.Interfaces;

namespace WildCat.Grains
{
    public class PlayerStorage : Grain, IPlayerStorage
    {
        private readonly IPersistentState<PlayerStorageState> state;

        public PlayerStorage([PersistentState(GrainStateNames.PlayerStorage, GrainStorageNames.Default)] IPersistentState<PlayerStorageState> state)
        {
            this.state = state;
        }

        private PlayerStorageState State => state.State;

        public override async Task OnActivateAsync(CancellationToken cancellationToken)
        {
            var gameKey = this.GetPrimaryKeyString().Split('_')[0];
            if (string.IsNullOrEmpty(State.GameKey))
            {
                State.GameKey = gameKey;
            }
            await base.OnActivateAsync(cancellationToken);
        }

        public Task<Tuple<bool, byte[]>> GetPlayerBinary(string key)
        {
            if (State.BinaryValues.ContainsKey(key))
                return Task.FromResult(Tuple.Create(true, State.BinaryValues[key]));
            return Task.FromResult(Tuple.Create<bool, byte[]>(false, null));
        }

        public Task<Tuple<bool, string>> GetPlayerString(string key)
        {
            if (State.StringValues.ContainsKey(key))
                return Task.FromResult(Tuple.Create(true, State.StringValues[key]));
            return Task.FromResult(Tuple.Create<bool, string>(false, null));
        }

        public Task SetPlayerBinary(string key, byte[] value)
        {
            State.BinaryValues[key] = value;
            return state.WriteStateAsync();
        }

        public Task SetPlayerString(string key, string value)
        {
            State.StringValues[key] = value;
            return state.WriteStateAsync();
        }

        public Task SetPlayerPublicString(string key, string value)
        {
            State.PublicStringValues[key] = value;
            return state.WriteStateAsync();
        }

        public Task<Tuple<bool, string>> GetPlayerPublicString(string key)
        {
            if (State.PublicStringValues.ContainsKey(key))
                return Task.FromResult(Tuple.Create(true, State.PublicStringValues[key]));
            return Task.FromResult(Tuple.Create<bool, string>(false, null));
        }

        public Task<Dictionary<string, string>> GetPlayerPublicStrings(string[] keys)
        {
            Dictionary<string, string> result = new Dictionary<string, string>();
            if (keys == null || keys.Length == 0)
                return Task.FromResult(result);
            for (int i = 0; i < keys.Length; ++i)
            {
                if (State.PublicStringValues.ContainsKey(keys[i]))
                    result.Add(keys[i], State.PublicStringValues[keys[i]]);
            }
            return Task.FromResult(result);
        }

        public Task SetPlayerPublicBinary(string key, byte[] value)
        {
            State.PublicBinaryValues[key] = value;
            return state.WriteStateAsync();
        }

        public Task<Tuple<bool, byte[]>> GetPlayerPublicBinary(string key)
        {
            if (State.PublicBinaryValues.ContainsKey(key))
                return Task.FromResult(Tuple.Create(true, State.PublicBinaryValues[key]));
            return Task.FromResult(Tuple.Create<bool, byte[]>(false, null));
        }
    }
}
