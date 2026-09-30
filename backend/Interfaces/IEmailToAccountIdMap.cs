using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using Orleans;

namespace Interfaces
{
    public interface IEmailToAccountIdMap : IGrainWithStringKey
    {
        Task<string> GetAccountID();
        Task<bool> SetAccountID(string id);
        Task Clear();
    }

    [GenerateSerializer]
    [Alias("wildcat.EmailAccountIDMapState")]
    public class EmailAccountIDMapState
    {
        [Id(0)] public string AccountID { get; set; }
        [Id(1)] public string Email { get; set; }
    }
}
