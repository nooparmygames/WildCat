using System;
using System.Threading.Tasks;
using Orleans;
using Orleans.Concurrency;
using Interfaces;
using Orleans.Runtime;

namespace Collections
{
    public class AccountingSystem : Grain<MonthlyAccountingInfo>, IAccountingSystem
    {
        private DateTime lastSaveTime;
        private ushort receivedAccountingInfoCount = 0;
        private ulong lastStoredMessageCount;
        private ulong lastStoredCallCount;
        private IGrainTimer timer;

        public override Task OnActivateAsync(CancellationToken cancellationToken)
        {
            string monthString;
            Guid gameKey = this.GetPrimaryKey(out monthString);

            State.GameKey = gameKey;
            State.Month = monthString;
            timer = this.RegisterGrainTimer<object>(OnTimer, null, new GrainTimerCreationOptions
            {
                DueTime = TimeSpan.FromMinutes(10),
                Period = TimeSpan.FromMinutes(10),
                Interleave = true
            });
            return base.OnActivateAsync(cancellationToken);
        }

        private async Task OnTimer(object arg)
        {
            await SaveData();
        }

        public override Task OnDeactivateAsync(DeactivationReason reason, CancellationToken cancellationToken)
        {
            if (timer != null)
            {
                timer.Dispose();
                timer = null;
            }
            if (State.MessageCount > 0 || State.CallCount > 0)
                return WriteStateAsync();
            return base.OnDeactivateAsync(reason, cancellationToken);
        }

        public async Task AddAccountingData(ulong callCount, ulong messageCount)
        {
            State.CallCount += callCount;
            State.MessageCount += messageCount;
            receivedAccountingInfoCount++;
            await SaveData();
        }

        private async Task SaveData()
        {
            if ((receivedAccountingInfoCount > 5000 || (DateTime.Now - lastSaveTime).TotalMinutes > 25)
               && (State.CallCount != lastStoredCallCount || State.MessageCount != lastStoredMessageCount))
            {
                await WriteStateAsync();
                lastStoredCallCount = State.CallCount;
                lastStoredMessageCount = State.MessageCount;
                lastSaveTime = DateTime.Now;
                receivedAccountingInfoCount = 0;
            }
        }

        public Task<MonthlyAccountingInfo> GetData()
        {
            return Task.FromResult(new MonthlyAccountingInfo
            {
                CallCount = State.CallCount,
                MessageCount = State.MessageCount,
                GameKey = State.GameKey,
                Month = State.Month
            });
        }
    }
}
