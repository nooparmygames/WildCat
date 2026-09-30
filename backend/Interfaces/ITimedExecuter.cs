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

    public class TimedExecuterState
    {
        public enum State
        {
            NotStarted,
            Started,
            Finished
        }
        public State state;
        public List<ExecutionTask> tasks;

        public TimedExecuterState()
        {
            tasks = new List<ExecutionTask>();
        }
    }

    public class ExecutionTask
    {
        public enum Type { Notification, TournamentRunner }
        public Type type;
        public DateTime dt;
        public string player;
        public string data;
        public bool isSent;
        public Guid id;
    }
}