using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using Orleans;
using Interfaces;
using Newtonsoft.Json;
using Orleans.Serialization;

namespace Collections
{
    public class PlayerStorage : Grain<PlayerStorageState>, IPlayerStorage
    {
        private IBackendStorageServer storage;
        private string entityId, entityType, gameKey, playerId;
        private bool isPlayfabRun;
        private int? version = null;
        private string fileName;

        public PlayerStorage(IBackendStorageServer storage)
        {
            this.storage = storage;
        }

        public override async Task OnActivateAsync()
        {
            var jsonSettings = OrleansJsonSerializer.GetDefaultSerializerSettings(this.GrainFactory);
            JsonConvert.DefaultSettings = () => jsonSettings;
            var key = this.GetPrimaryKeyString();
            string[] keyParts = key.Split('_');
            var gameKey = keyParts[0];
            playerId = keyParts[1];
            if (keyParts.Length > 2)
                fileName = keyParts[2];
            else
                fileName = "main";
            if (string.IsNullOrEmpty(State.GameKey))
            {
                State.GameKey = gameKey;
            }
            isPlayfabRun = await storage.IsPlayfabRun();
            if (isPlayfabRun)
            {
                (entityId, entityType, gameKey) = await GrainFactory.GetGrain<IPlayer>(playerId).GetPlayfabEntityAndGame();
                await Load();
            }
            await base.OnActivateAsync();
        }

        private async Task Save()
        {
            if (isPlayfabRun)
            {
                string stateJson = JsonConvert.SerializeObject(State);
                var (code, msg, ver) = await storage.SaveFile(entityId, entityType,
                    new Dictionary<string, byte[]> { { fileName, System.Text.Encoding.UTF8.GetBytes(stateJson) } }, null);
                if (code != 0)
                    throw new Exception($"Storage failed: code = {code} message = {msg}");
            }
            else
            {
                await WriteStateAsync();
            }
        }

        private async Task Load()
        {
            var (code, msg, data, version) = await storage.LoadFile(entityId, entityType, new string[] { fileName });
            if (code != 0 && code != 1)
                throw new System.Exception($"Loading data from playfab failed {code} {msg}");
            if (code == 1)
            {
                State = new PlayerStorageState();
            }
            this.version = version;
            if (data.ContainsKey(fileName) && data[fileName] != null)
                State = JsonConvert.DeserializeObject<PlayerStorageState>(System.Text.Encoding.UTF8.GetString(data[fileName]));
            else
                State = new PlayerStorageState();
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
            return Save();
        }

        public Task SetPlayerString(string key, string value)
        {
            State.StringValues[key] = value;
            return Save();
        }

        public Task SetPlayerPublicString(string key, string value)
        {
            State.PublicStringValues[key] = value;
            return Save();
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
            return Save();
        }

        public Task<Tuple<bool, byte[]>> GetPlayerPublicBinary(string key)
        {
            if (State.PublicBinaryValues.ContainsKey(key))
                return Task.FromResult(Tuple.Create(true, State.PublicBinaryValues[key]));
            return Task.FromResult(Tuple.Create<bool, byte[]>(false, null));
        }
    }
}
