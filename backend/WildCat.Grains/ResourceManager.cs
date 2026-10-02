using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using Orleans;
using Orleans.Runtime;
using WildCat.Interfaces;

namespace WildCat.Grains
{
    public class ResourceManager : Grain, IResourceManager
    {
        private readonly IPersistentState<ResourceManagerState> state;

        public ResourceManager([PersistentState(GrainStateNames.ResourceManager, GrainStorageNames.Default)] IPersistentState<ResourceManagerState> state)
        {
            this.state = state;
        }

        private ResourceManagerState State => state.State;

        public async Task<bool> AddResource(IManagedResource resource)
        {
            string key = await resource.GetKey();
            if (State.resources.ContainsKey(key))
                return false;
            string resourceName = await resource.GetName();
            State.resources.Add(key,resourceName);
            await state.WriteStateAsync();
            return true;
        }

        public Task<List<IManagedResource>> GetResources(string className)
        {
            return Task.FromResult(State.resources.
                Keys
                .Select(x=>GrainFactory.GetGrain<IManagedResource>(x,className))
                .ToList());
        }

        public Task<string> GetResourcesCSharpCode(string className)
        {
            var names = State.resources.Values;
            StringBuilder sb = new StringBuilder();
            sb.AppendFormat("public class {0}", className);
            sb.AppendLine();
            sb.AppendLine("{");
            foreach(var val in names)
            {
                sb.AppendFormat("public const string {2}_{0} = \"{1}\";",val.ToUpper(),val,className);
                sb.AppendLine();
            }
            sb.AppendLine("}");
            string code = sb.ToString();
            return Task.FromResult(code);
        }

        public Task<bool> HasResource(string key)
        {
            return Task.FromResult(State.resources.ContainsKey(key));
        }

        public async Task<bool> RemoveResource(IManagedResource resource)
        {
            string key = await resource.GetKey();
            if(State.resources.ContainsKey(key))
            {
                State.resources.Remove(key);
                await state.WriteStateAsync();
                return true;
            }
            return false;

        }

        public async Task<bool> RemoveResourceByKey(string key)
        {
            if (key == null || !State.resources.Remove(key))
                return false;
            await state.WriteStateAsync();
            return true;
        }
    }

    [GenerateSerializer]
    [Alias("wildcat.ResourceManagerState")]
    public class ResourceManagerState
    {
        [Id(0)] public Dictionary<string, string> resources { get; set; }

        public ResourceManagerState()
        {
            resources = new Dictionary<string, string>();
        }

    }
}
