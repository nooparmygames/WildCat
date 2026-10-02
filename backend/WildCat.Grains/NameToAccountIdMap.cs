using System.Threading.Tasks;
using WildCat.Interfaces;
using Orleans;
using Orleans.Runtime;

namespace WildCat.Grains
{
    public class NameToAccountIdMap : Grain, INameToAccountIdMap
    {
        private readonly IPersistentState<NameAccountIDMapState> state;

        public NameToAccountIdMap([PersistentState(GrainStateNames.NameToAccountIdMap, GrainStorageNames.Default)] IPersistentState<NameAccountIDMapState> state)
        {
            this.state = state;
        }

        private NameAccountIDMapState State => state.State;

        public Task Clear()
        {
            return state.ClearStateAsync();
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
            return state.WriteStateAsync();
        }
    }
}
