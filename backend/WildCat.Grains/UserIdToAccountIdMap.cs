using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using Orleans;
using WildCat.Interfaces;

namespace WildCat.Grains
{
    class UserIdToAccountIdMap : Grain<UserIdAccountIDMapState>, IUserIdToAccountIdMap
    {
        public Task Clear()
        {
            return ClearStateAsync();
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
                await WriteStateAsync();
                isAllowed = true;
            }
            return isAllowed || State.AccountID == accountId;
        }
    }
}
