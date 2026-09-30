using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using Orleans;

namespace Interfaces
{
    public interface IAccountingSystem : IGrainWithGuidCompoundKey
    {
        Task AddAccountingData(ulong callCount, ulong messageCount);
        Task<MonthlyAccountingInfo> GetData();
    }

    public class MonthlyAccountingInfo
    {
        public string Month { get; set; }
        public Guid GameKey { get; set; }
        public ulong CallCount { get; set; }
        public ulong MessageCount { get; set; }
    }
}
