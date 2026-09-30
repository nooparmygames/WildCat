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

    [GenerateSerializer]
    [Alias("wildcat.UserIdAccountIDMapState")]
    public class UserIdAccountIDMapState
    {
        [Id(0)] public string AccountID { get; set; }
        [Id(1)] public string UserId { get; set; }
    }
}
