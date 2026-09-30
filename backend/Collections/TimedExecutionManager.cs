using System;
using System.Linq;
using System.Collections.Generic;
using System.Threading.Tasks;
using Orleans;
using Interfaces;
using Orleans.Concurrency;
using Orleans.Streams;
using Orleans.Runtime;

namespace Collections
{
    class TimedExecutionManager : Grain<TimedExecutionManagerState>, IRemindable, ITimedExecutionManager
    {
        private bool isRunning;

        public Task AddGroup(DateTime dt)
        {
            if (dt.Second != 0)
                dt.AddSeconds(-dt.Second);
            if (!State.groups.Contains(dt))
            {
                State.groups.Add(dt);
                return WriteStateAsync();
            }
            return Task.CompletedTask;
        }

        public async override Task OnActivateAsync()
        {
            await RegisterOrUpdateReminder("StartTasks", TimeSpan.FromMinutes(1), TimeSpan.FromMinutes(1));
            await base.OnActivateAsync();
        }

        public Task ReceiveReminder(string reminderName, TickStatus status)
        {
            DateTime dt = DateTime.UtcNow;
            if (dt.Second != 0)
                dt.AddSeconds(-dt.Second);
            if (!State.groups.Contains(dt))
                State.groups.Add(dt);
            return StartRunningTasks();
        }

        public async Task StartRunningTasks()
        {
            try
            {
                for (int i = State.groups.Count - 1; i >= 0; i--)
                {
                    var dt = State.groups[i];
                    await ExecuteTask(dt);
                }
            }
            catch (Exception ex)
            {

            }
            finally
            {
                await WriteStateAsync();
            }
        }

        private async Task ExecuteTask(DateTime dt)
        {
            await GrainFactory.GetGrain<ITimedExecuter>(dt.ToString()).StartExecuting();
            State.groups.Remove(dt);
        }
    }
}