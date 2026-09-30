using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using Orleans;

namespace WildCat.Interfaces
{
    /// <summary>
    /// Publisher key/value data, keyed by the developer key. Every method that takes a secret
    /// throws <see cref="InvalidOperationException"/> when it is not the developer's current secret.
    /// </summary>
    public interface IDeveloperStorage : IGrainWithGuidKey
    {
        Task<List<Tuple<string,bool>>> GetAllKeys(Guid secret);
        Task<Dictionary<string, string>> GetAllStringData(Guid secret);
        Task SetString(string key, string value,Guid secret);
        Task SetBinary(string key, byte[] value,Guid secret);

        /// <summary>
        /// Public read used by game clients (message 34); returns null for a missing key.
        /// </summary>
        Task<string> GetString(string key);

        /// <summary>
        /// Public read of binary data; returns null for a missing key.
        /// </summary>
        Task<byte[]> GetBinary(string key);

        /// <summary>
        /// Developer read (message 64): the same value as <see cref="GetString"/> but only with the
        /// developer secret.
        /// </summary>
        Task<string> GetStringForDeveloper(string key, Guid secret);
        Task RemoveString(string key, Guid secret);
        Task RemoveBinary(string key, Guid secret);
    }
}
