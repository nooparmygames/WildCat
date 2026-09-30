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

    public class NameAccountIDMapState
    {
        public string AccountID { get; set; }
        public string Name { get; set; }
    }
}
