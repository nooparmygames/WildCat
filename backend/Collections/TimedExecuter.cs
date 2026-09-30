using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Interfaces;
using Microsoft.Extensions.Logging;
using Orleans;
using Orleans.Concurrency;

namespace Collections
{
    public class TimedExecuter : Grain<TimedExecuterState>, ITimedExecuter
    {
        private IBackendStorageServer storage;
        private ILogger logger;

        public TimedExecuter(IBackendStorageServer storageProvider, ILogger<ITimedExecuter> logger)
        {
            this.storage = storageProvider;
            this.logger = logger;
        }
        
        private DateTime startTime;
        private IDisposable timer;

        public async override Task OnActivateAsync()
        {
            startTime = DateTime.Parse(this.GetPrimaryKeyString());
            if (State.state != TimedExecuterState.State.Finished)
                await GrainFactory.GetGrain<ITimedExecutionManager>("main").AddGroup(startTime);
            await base.OnActivateAsync();
        }

        public async Task Finish()
        {
            var prev = State.state;
            State.state = TimedExecuterState.State.Finished;
            try
            {
                await WriteStateAsync();
                timer?.Dispose();
            }
            catch
            {
                State.state = prev;
            }
        }
        private bool isExecuting = false;
        public async Task StartExecuting()
        {
            if (isExecuting)
                return;
            isExecuting = true;
            List<Task> runningTasks = new List<Task>();
            ExecutionTask[] tasksToRun = State.tasks.Where(x => !x.isSent).ToArray();
            if (tasksToRun.Length > 0)
            {
                try
                {
                    foreach (var task in tasksToRun)
                    {
                        runningTasks.Add(Execute(task));
                    }

                    await Task.WhenAll(runningTasks);
                    if (State.tasks.All(x => x.isSent))
                    {
                        await Finish();
                    }
                }
                catch (Exception exception)
                {
                    timer = RegisterTimer(OnExecutionTimer, 10, TimeSpan.FromSeconds(10), TimeSpan.FromSeconds(10));
                }
            }
            else
            {
                await Finish();
            }
            isExecuting = false;
        }

        public override Task OnDeactivateAsync()
        {
            timer?.Dispose();
            timer = null;
            return base.OnDeactivateAsync();
        }

        private async Task OnExecutionTimer(object obj)
        {
            await StartExecuting();
        }

        public async Task Execute(ExecutionTask task)
        {
            switch (task.type)
            {
                case ExecutionTask.Type.Notification:
                    var (code, msg) = await storage.SendNotification(task.player, task.data);
                    if (code == 0)
                        task.isSent = true;
                    else
                        throw new System.Exception($"Failed to send notification. Error: {code} {msg}");
                    break;
                case ExecutionTask.Type.TournamentRunner:
                    //var (code2, msg2) = await GrainFactory.GetGrain<ITournamentManager>(task.player).StartTournaments(DateTime.UtcNow);
                    //if (code2 == 0)
                    //    task.isSent = true;
                    //else
                    //    throw new System.Exception($"Failed to start tournament. Error: {code2} {msg2}");
                    break;
            }
        }

        public Task<TimedExecuterState.State> GetState()
        {
            return Task.FromResult(State.state);
        }

        public async Task AddTask(ExecutionTask task)
        {
            State.tasks.Add(task);
            try
            {
                await WriteStateAsync();
            }
            catch(Exception ex)
            {
                logger.LogError(ex.ToString());
                State.tasks.RemoveAll(x => x.id == task.id);
                throw ex;
            }
        }

        public async Task CancelTask(ExecutionTask task)
        {
            State.tasks.RemoveAll(x => x.id == task.id);
            try
            {
                await WriteStateAsync();
            }
            catch(Exception exception)
            {
                logger.LogError(exception.ToString());
                State.tasks.Add(task);
                throw exception;
            }
        }
    }
}