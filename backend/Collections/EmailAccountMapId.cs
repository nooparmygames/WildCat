using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using Interfaces;
using Orleans;

namespace Collections
{
    class EmailAccountMapId : Grain<EmailAccountIDMapState>, IEmailToAccountIdMap
    {
        public Task Clear()
        {
            return ClearStateAsync();
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
                await WriteStateAsync();
                isAllowed = true;
            }
            return isAllowed || State.AccountID == account;
        }
    }
}
