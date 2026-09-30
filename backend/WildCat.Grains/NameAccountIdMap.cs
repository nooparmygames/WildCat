using System.Threading.Tasks;
using WildCat.Interfaces;
using Orleans;

namespace WildCat.Grains
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
