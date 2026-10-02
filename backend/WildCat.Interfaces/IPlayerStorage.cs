using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using Orleans;

namespace WildCat.Interfaces
{
    /// <summary>
    /// Used for storing player data per game instead of storing it per player.
    /// </summary>
    public interface IPlayerStorage:IGrainWithStringKey
    {
        Task SetPlayerString(string key,string value);
        Task<Tuple<bool,string>> GetPlayerString(string key);
        Task SetPlayerBinary(string key, byte[] value);
        Task<Tuple<bool, byte[]>> GetPlayerBinary(string key);
        Task SetPlayerPublicString(string key, string value);
        Task<Tuple<bool, string>> GetPlayerPublicString(string key);
        Task<Dictionary<string, string>> GetPlayerPublicStrings(string[] keys);
        Task SetPlayerPublicBinary(string key, byte[] value);
        Task<Tuple<bool, byte[]>> GetPlayerPublicBinary(string key);
    }

    [GenerateSerializer]
    [Alias("wildcat.PlayerStorageState")]
    public class PlayerStorageState
    {
        [Id(0)] public string GameKey { get; set; }
        [Id(1)] public Dictionary<string, string> StringValues { get; set; }
        [Id(2)] public Dictionary<string, byte[]> BinaryValues { get; set; }
        [Id(3)] public Dictionary<string, string> PublicStringValues { get; set; }
        [Id(4)] public Dictionary<string, byte[]> PublicBinaryValues { get; set; }

        public PlayerStorageState()
        {
            StringValues = new Dictionary<string, string>();
            BinaryValues = new Dictionary<string, byte[]>();
            PublicStringValues = new Dictionary<string, string>();
            PublicBinaryValues = new Dictionary<string, byte[]>();
        }
    }
}
