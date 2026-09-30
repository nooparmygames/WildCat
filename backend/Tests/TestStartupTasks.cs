using System.Threading;
using System.Threading.Tasks;
using Interfaces;
using Orleans.Runtime;

namespace Tests
{
    public class TestStartupTasks : IStartupTask
    {
        private IBackendStorageServer storage;

        public TestStartupTasks(IBackendStorageServer storage)
        {
            this.storage = storage;
        }

        public Task Execute(CancellationToken cancellationToken)
        {
            storage.SetTitleId("");
            return Task.CompletedTask;
        }
    }
}