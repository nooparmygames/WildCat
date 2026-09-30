using System;
using System.Threading.Tasks;
using Orleans;
using Interfaces;


namespace Collections
{
    public class Session : Grain, ISession
    {
        private bool IsLoggedIn { get { return !string.IsNullOrEmpty(State.UserID); } }
        private ulong CallCount;
        private ulong MessageCount;
        private SessionState State = new SessionState();

        public async Task<SessionGameInfo> AddUserCallCheckSessionAndGetInfo()
        {
            AssertSessionInitialized();
            await AddUserCall();
            bool isExpired = await IsExpired();
            if (!isExpired)
            {
                return GetSessionInfoSync();
            }
            throw new SessionExpiredException();
        }

        public async override Task OnDeactivateAsync(DeactivationReason reason, CancellationToken cancellationToken)
        {
            try
            {
                bool isExpired = await IsExpired();
                if (isExpired)
                {
                    //await ClearStateAsync();
                }
            }
            catch (SessionNotInitializedException)
            {

            }
            catch
            {
                throw;
            }
        }

        public async Task<SessionGameInfo> CheckSessionAndGetInfo()
        {
            AssertSessionInitialized();
            bool isExpired = await IsExpired();
            if (!isExpired)
            {
                return GetSessionInfoSync();
            }
            throw new SessionExpiredException();
        }

        public async Task<SessionGameInfo> AddUserMessagesCheckSessionAndGetInfo(int messageCount)
        {
            AssertSessionInitialized();
            await AddUserMessage(messageCount);
            bool isExpired = await IsExpired();
            if (!isExpired)
            {
                return GetSessionInfoSync();
            }
            throw new SessionExpiredException();
        }

        private SessionGameInfo GetSessionInfoSync()
        {
            return new SessionGameInfo
            {
                UserID = State.UserID,
                DeveloperKey = State.DeveloperKey,
                GameKey = State.GameKey
            };
        }

        public Task<bool> IsExpired()
        {
            AssertSessionInitialized();

            return Task.FromResult((DateTime.Now - State.LastCallTime).TotalMinutes > 60);
        }

        public Task EndSession()
        {
            State.IsInitialized = false;
            State.UserID = "";
            return Task.CompletedTask;
        }

        public async Task StartAuthenticatedUserSession(string ticket, string userId)
        {
            AssertSessionInitialized();
            if (IsLoggedIn)
            {
                //throw new InvalidOperationException();
            }
            State.UserID = userId;
            State.Ticket = ticket;
            State.StartTime = DateTime.Now;
            State.LastCallTime = DateTime.Now;
            lastSaveTime = DateTime.Now;
            await AddUserCall();//No cheating, this is for both initi and login
            //await WriteStateAsync();
        }

        public Task<bool> IsInitialized()
        {
            return Task.FromResult(State.IsInitialized);
        }

        private void AssertSessionInitialized()
        {
            if (!State.IsInitialized)
                throw new SessionNotInitializedException();
        }

        private ushort countsAfterSave = 0;
        private DateTime lastSaveTime;

        public async Task AddUserCall(int count = 1)
        {
            AssertSessionInitialized();
            State.LastCallTime = DateTime.Now;
            CallCount++;
            countsAfterSave++;
            await SendDataToAccountingIfNeeded();
        }

        private async Task SendDataToAccountingIfNeeded()
        {
            if (ShouldSaveForAccounting())
            {
                await GrainFactory.GetGrain<IAccountingSystem>(State.GameKey, InvoiceUtility.GetInvoiceMonthKey(DateTime.UtcNow), null).AddAccountingData(CallCount, MessageCount);
                countsAfterSave = 0;
                CallCount = 0;
                MessageCount = 0;
                lastSaveTime = DateTime.Now;
            }
        }

        private bool ShouldSaveForAccounting()
        {
            return (countsAfterSave > 20 || (DateTime.Now - lastSaveTime).TotalSeconds > 60)
                && (CallCount > 0 || MessageCount > 0);
        }

        public async Task AddUserMessage(int messageCount)
        {
            AssertSessionInitialized();
            State.LastCallTime = DateTime.Now;
            MessageCount += (ulong)messageCount;
            countsAfterSave++;
            await SendDataToAccountingIfNeeded();
        }

        public Task InitializeSession(Guid developerKey, Guid gameKey)
        {
            State.DeveloperKey = developerKey;
            State.GameKey = gameKey;
            State.IsInitialized = true;
            return Task.CompletedTask;
            //await WriteStateAsync();
        }

        public Task<SessionGameInfo> GetDeveloperKey()
        {
            return Task.FromResult(GetSessionInfoSync());
        }
        //
        // public Task GetReturnType(Object returnValue)
        // {
        //     
        //     return Task.CompletedTask;
        // }
        //
    }

    public class SessionState
    {
        public string UserID { get; set; }
        public DateTime LastCallTime { get; set; }
        public DateTime StartTime { get; set; }
        public string Ticket { get; set; }
        public Guid DeveloperKey { get; set; }
        public Guid GameKey { get; set; }
        public bool IsInitialized { get; set; }


        public SessionState()
        {
            GameKey = DeveloperKey = Guid.Empty;
        }
    }

    [Serializable]
    [GenerateSerializer]
    [Alias("wildcat.SessionExpiredException")]
    public class SessionExpiredException : Exception
    {
        public SessionExpiredException()
        {
        }

        public SessionExpiredException(string message) : base(message)
        {
        }

        public SessionExpiredException(string message, Exception innerException) : base(message, innerException)
        {
        }
    }

    [Serializable]
    [GenerateSerializer]
    [Alias("wildcat.SessionNotInitializedException")]
    public class SessionNotInitializedException : Exception
    {
        public SessionNotInitializedException()
        {
        }

        public SessionNotInitializedException(string message) : base(message)
        {
        }

        public SessionNotInitializedException(string message, Exception innerException) : base(message, innerException)
        {
        }
    }

    [Serializable]
    [GenerateSerializer]
    [Alias("wildcat.InvalidGameException")]
    public class InvalidGameException : Exception
    {
        public InvalidGameException()
        {
        }

        public InvalidGameException(string message) : base(message)
        {
        }

        public InvalidGameException(string message, Exception innerException) : base(message, innerException)
        {
        }
    }

}

