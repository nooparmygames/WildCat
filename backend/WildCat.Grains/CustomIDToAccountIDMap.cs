using System.Threading.Tasks;
using WildCat.Interfaces;
using Orleans;
using Orleans.Runtime;

namespace WildCat.Grains
{
    public class CustomIDToAccountIDMap : Grain, ICustomIDToAccountIDMap
    {
        private readonly IPersistentState<CustomAccountIDMapState> state;

        public CustomIDToAccountIDMap([PersistentState(GrainStateNames.CustomIdToAccountIdMap, GrainStorageNames.Default)] IPersistentState<CustomAccountIDMapState> state)
        {
            this.state = state;
        }

        private CustomAccountIDMapState State => state.State;

        public Task<string> GetAccountID()
        {
            return Task.FromResult(State.AccountID);
        }

        public async Task<bool> SetAccountID(string accountId,string customId)
        {
            bool isAllowed = false;
            if (string.IsNullOrEmpty(State.AccountID))
            {
                State.AccountID = accountId;
                State.CustomId = customId;
                await state.WriteStateAsync();
                isAllowed = true;
            }
            return isAllowed || State.AccountID == accountId;
        }

        public Task Clear()
        {
            return state.ClearStateAsync();
        }

    }

    
}
