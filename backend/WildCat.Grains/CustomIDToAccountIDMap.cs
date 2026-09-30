using System.Threading.Tasks;
using WildCat.Interfaces;
using Orleans;

namespace WildCat.Grains
{
    public class CustomIDToAccountIDMap : Grain<CustomAccountIDMapState>, ICustomIDToAccountIDMap
    {
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
                await WriteStateAsync();
                isAllowed = true;
            }
            return isAllowed || State.AccountID == accountId;
        }

        public Task Clear()
        {
            return ClearStateAsync();
        }

    }

    
}
