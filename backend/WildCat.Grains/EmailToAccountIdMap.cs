using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using WildCat.Interfaces;
using Orleans;
using Orleans.Runtime;

namespace WildCat.Grains
{
    class EmailToAccountIdMap : Grain, IEmailToAccountIdMap
    {
        private readonly IPersistentState<EmailAccountIDMapState> state;

        public EmailToAccountIdMap([PersistentState(GrainStateNames.EmailToAccountIdMap, GrainStorageNames.Default)] IPersistentState<EmailAccountIDMapState> state)
        {
            this.state = state;
        }

        private EmailAccountIDMapState State => state.State;

        public Task Clear()
        {
            return state.ClearStateAsync();
        }

        public Task<string> GetAccountID()
        {
            return Task.FromResult(State.AccountID);
        }

        public async Task<bool> SetAccountID(string account)
        {
            bool isAllowed = false;
            if(string.IsNullOrEmpty(State.AccountID))
            {
                State.Email = this.GetPrimaryKeyString();
                State.AccountID = account;
                await state.WriteStateAsync();
                isAllowed = true;
            }
            return isAllowed || State.AccountID == account;
        }
    }
}
