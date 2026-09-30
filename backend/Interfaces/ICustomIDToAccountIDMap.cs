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

    [GenerateSerializer]
    [Alias("wildcat.CustomAccountIDMapState")]
    public class CustomAccountIDMapState
    {
        [Id(0)] public string AccountID { get; set; }
        [Id(1)] public string CustomId { get; set; }
    }
}
