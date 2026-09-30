using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using Orleans;

namespace WildCat.Interfaces
{
    public interface IDeveloperManager : IGrainWithStringKey
    {
        Task<Guid> AddDeveloper(DeveloperState data);
        Task<Guid> AddDeveloper(DeveloperState data, Guid id);
        Task<IDeveloper> GetDeveloper(string email);
        Task<List<IDeveloper>> GetDevelopers();

        Task<IDeveloper> GetDeveloperByKey(string key);
        Task AddDeveloperToSIByKey(string key,IDeveloper developer);
        Task RemoveDeveloperFromSIKeys(string key);

    }

    [GenerateSerializer]
    [Alias("wildcat.DeveloperManagerState")]
    public class DeveloperManagerState
    {
        [Id(0)] public Dictionary<string, Guid> developers { get; set; }
        [Id(1)] public Dictionary<string, Guid> SecondaryIndex { get; set; }

        public DeveloperManagerState()
        {
            developers = new Dictionary<string, Guid>();
            SecondaryIndex = new Dictionary<string, Guid>();
        }
    }
}
