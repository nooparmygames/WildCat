using System.Threading.Tasks;
using Interfaces;
using Orleans;

namespace Collections
{
    public class NameAccountIdMap : Grain<NameAccountIDMapState>, INameToAccountIdMap
    {
        public Task Clear()
        {
            return ClearStateAsync();
        }

        public Task<string> GetId()
        {
            return Task.FromResult(State.Name);
        }

        public Task SetId(string name)
        {
            //if (!string.IsNullOrEmpty(State.Name))
            //    throw new System.InvalidOperationException("The name is taken");
            State.AccountID = this.GetPrimaryKeyString();
            State.Name = name;
            return WriteStateAsync();
        }
    }
}
