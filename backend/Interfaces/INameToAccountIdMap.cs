using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using Orleans;

namespace Interfaces
{
    public interface INameToAccountIdMap : IGrainWithStringKey
    {
        Task<string> GetId();
        Task SetId(string name);
        Task Clear();
    }

    [GenerateSerializer]
    [Alias("wildcat.NameAccountIDMapState")]
    public class NameAccountIDMapState
    {
        [Id(0)] public string AccountID { get; set; }
        [Id(1)] public string Name { get; set; }
    }
}
