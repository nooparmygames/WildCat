using System;
using System.Threading.Tasks;
using Orleans;
using Interfaces;


namespace Collections
{
    public class Session : Grain, ISession
    {
        private bool IsLoggedIn { get { return !string.IsNullOrEmpty(State.UserID); } }
        private SessionState State = new SessionState();

        public async Task<SessionGameInfo> TouchAndGetInfo()
        {
            await Touch();
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

        public Task StartAuthenticatedUserSession(string ticket, string userId)
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
            return Task.CompletedTask;
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

        public Task Touch()
        {
            AssertSessionInitialized();
            State.LastCallTime = DateTime.Now;
            return Task.CompletedTask;
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

