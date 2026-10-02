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
    class UserIdToAccountIdMap : Grain, IUserIdToAccountIdMap
    {
        private readonly IPersistentState<UserIdAccountIDMapState> state;

        public UserIdToAccountIdMap([PersistentState(GrainStateNames.UserIdToAccountIdMap, GrainStorageNames.Default)] IPersistentState<UserIdAccountIDMapState> state)
        {
            this.state = state;
        }

        private UserIdAccountIDMapState State => state.State;

        public Task Clear()
        {
            return state.ClearStateAsync();
        }

        public Task<string> GetAccountID()
        {
            return Task.FromResult(State.AccountID);
        }

        public async Task<bool> SetAccountID(string accountId)
        {
            bool isAllowed = false;
            if(string.IsNullOrEmpty(State.AccountID))
            {
                State.UserId = this.GetPrimaryKeyString();
                State.AccountID = accountId;
                await state.WriteStateAsync();
                isAllowed = true;
            }
            return isAllowed || State.AccountID == accountId;
        }
    }
}
