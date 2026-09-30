using System;
using System.Collections.Generic;
using System.Text;
using System.Threading.Tasks;
using Orleans;

namespace Interfaces
{
    public interface ITimedExecuter : IGrainWithStringKey
    {
        Task StartExecuting();
        Task Finish();
        Task Execute(ExecutionTask task);
        Task AddTask(ExecutionTask task);
        Task CancelTask(ExecutionTask task);
        Task<TimedExecuterState.State> GetState();
    }

    [GenerateSerializer]
    [Alias("wildcat.TimedExecuterState")]
    public class TimedExecuterState
    {
        [GenerateSerializer, Alias("wildcat.TimedExecuterState.State")]
        public enum State
        {
            NotStarted,
            Started,
            Finished
        }
        [Id(0)] public State state;
        [Id(1)] public List<ExecutionTask> tasks;

        public TimedExecuterState()
        {
            tasks = new List<ExecutionTask>();
        }
    }

    [GenerateSerializer]
    [Alias("wildcat.ExecutionTask")]
    public class ExecutionTask
    {
        [GenerateSerializer, Alias("wildcat.ExecutionTask.Type")]
        public enum Type { Notification, TournamentRunner }
        [Id(0)] public Type type;
        [Id(1)] public DateTime dt;
        [Id(2)] public string player;
        [Id(3)] public string data;
        [Id(4)] public bool isSent;
        [Id(5)] public Guid id;
    }
}