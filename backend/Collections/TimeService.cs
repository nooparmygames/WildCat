using System;
using System.Threading.Tasks;
using Orleans;
using Interfaces;
using Orleans.Concurrency;

namespace Collections
{
    [StatelessWorker]
    [Reentrant]
    public class TimeService : Grain, ITimeService
    {
        public Task<string> GetServerTime()
        {
            return Task.FromResult(DateTime.Now.ToUniversalTime().ToString());
        }
    }
}
