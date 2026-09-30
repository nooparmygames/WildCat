using System;
using System.Collections.Generic;
using System.Text;
using Orleans;
using System.Threading.Tasks;
using Orleans.Concurrency;

namespace Interfaces
{
    public interface ITimedExecutionManager : IGrainWithStringKey
    {
        Task StartRunningTasks();
        [AlwaysInterleave]
        Task AddGroup(DateTime dt);
    }

    [GenerateSerializer]
    [Alias("wildcat.TimedExecutionManagerState")]
    public class TimedExecutionManagerState
    {
        [Id(0)] public List<DateTime> groups;
        [Id(1)] public List<DateTime> finishedGroups;
        
        public TimedExecutionManagerState()
        {
            groups = new List<DateTime>();
            finishedGroups = new List<DateTime>();
        }
    }
}