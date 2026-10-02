using System;
using System.Threading.Tasks;
using Orleans;
using WildCat.Interfaces;
using Orleans.Concurrency;

namespace WildCat.Grains
{
    [StatelessWorker]
    [Reentrant]
    public class TimeService : Grain, ITimeService
    {
        public Task<DateTime> GetServerTime()
        {
            return Task.FromResult(DateTime.UtcNow);
        }
    }
}
