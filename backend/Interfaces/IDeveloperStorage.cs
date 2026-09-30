using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using Orleans;

namespace Interfaces
{
    public interface IDeveloperStorage : IGrainWithGuidKey
    {
        Task<List<Tuple<string,bool>>> GetAllKeys(Guid secret);
        Task<Dictionary<string, string>> GetAllStringData(Guid secret);
        Task SetString(string key, string value,Guid secret);
        Task SetBinary(string key, byte[] value,Guid secret);
        Task<string> GetString(string key);
        Task<byte[]> GetBinary(string key);
        Task RemoveString(string key);
        Task RemoveBinary(string key);
    }
}
