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

    public class EmailAccountIDMapState
    {
        public string AccountID { get; set; }
        public string Email { get; set; }
    }
}
