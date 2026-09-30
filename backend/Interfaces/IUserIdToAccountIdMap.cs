using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using Orleans;

namespace Interfaces
{
    public interface IUserIdToAccountIdMap :IGrainWithStringKey
    {
        Task<string> GetAccountID();
        Task<bool> SetAccountID(string id);
        Task Clear();
    }

    public class UserIdAccountIDMapState
    {
        public string AccountID { get; set; }
        public string UserId { get; set; }
    }
}
