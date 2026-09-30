using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using Orleans;

namespace WildCat.Interfaces
{
    /// <summary>
    /// Managable resources like leaderboards and match-makers which only a certain
    /// number of them are valid are managed by this. The list is persisted and there is no
    /// need to manage currently active resources.
    /// </summary>
    /// <remarks>
    /// This only tells us for example if a leaderboard grain with a certain ID is valid or not
    /// regardless of having activations available at the moment or not.
    /// </remarks>
    public interface IResourceManager : IGrainWithStringKey
    {
        Task<bool> AddResource(IManagedResource resource);
        Task<bool> RemoveResource(IManagedResource resource);
        Task<List<IManagedResource>> GetResources(string className);
        Task<bool> HasResource(string key);
        Task<string> GetResourcesCSharpCode(string className);
    }
}
