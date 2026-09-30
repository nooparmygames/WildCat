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

    [GenerateSerializer]
    [Alias("wildcat.MonthlyAccountingInfo")]
    public class MonthlyAccountingInfo
    {
        [Id(0)] public string Month { get; set; }
        [Id(1)] public Guid GameKey { get; set; }
        [Id(2)] public ulong CallCount { get; set; }
        [Id(3)] public ulong MessageCount { get; set; }
    }
}
