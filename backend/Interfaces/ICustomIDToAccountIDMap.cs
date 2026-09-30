using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using Orleans;

namespace Interfaces
{
    /// <summary>
    /// This interface is used to convert deviceID/FacebookID/...ID to accountID 
    /// </summary>
    public interface ICustomIDToAccountIDMap : IGrainWithStringKey
    {
        Task<string> GetAccountID();
        Task<bool> SetAccountID(string accountId, string customId);
        Task Clear();
    }

    public class CustomAccountIDMapState
    {
        public string AccountID { get; set; }
        public string CustomId { get; set; }
    }
}
