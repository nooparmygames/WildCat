using System;
using System.Threading.Tasks;
using Microsoft.Extensions.Options;
using Orleans;
using Orleans.Runtime;
using WildCat.Interfaces;


namespace WildCat.Grains
{
    /// <summary>
    /// Settings for <see cref="Session"/>. The defaults match the legacy behavior; bind the class
    /// with services.Configure&lt;SessionOptions&gt;(...) to change them.
    /// </summary>
    public class SessionOptions
    {
        /// <summary>
        /// A session unused for longer than this expires and is cleared. Default 60 minutes.
        /// </summary>
        public TimeSpan IdleTimeout { get; set; } = TimeSpan.FromMinutes(60);

        /// <summary>
        /// Touches update the last call time in memory on every call but write it to storage at
        /// most this often, so a busy session does not write on every message. Default 1 minute.
        /// </summary>
        public TimeSpan TouchWriteInterval { get; set; } = TimeSpan.FromMinutes(1);
    }

    /// <summary>
    /// The bearer ticket a client sends with every message. State is persisted when it changes in
    /// a way that matters (initialize, user binding, end) and on touches at most once per
    /// <see cref="SessionOptions.TouchWriteInterval"/>, so sessions survive deactivation and silo
    /// restarts. The idle check runs before the touch, so an expired session is rejected and
    /// cleared instead of being revived by the call that finds it.
    /// </summary>
    public class Session : Grain, ISession
    {
        private readonly IPersistentState<SessionState> state;
        private readonly SessionOptions options;

        // LastCallTime as it was last written, to throttle the writes made by touches.
        private DateTime lastWrittenCallTime;

        public Session(
            [PersistentState("session")] IPersistentState<SessionState> state,
            IOptions<SessionOptions> options)
        {
            this.state = state;
            this.options = options.Value;
        }

        private SessionState State => state.State;

        public override Task OnActivateAsync(CancellationToken cancellationToken)
        {
            lastWrittenCallTime = State.LastCallTime;
            return base.OnActivateAsync(cancellationToken);
        }

        public async Task<SessionGameInfo> TouchAndGetInfo()
        {
            await EnsureNotExpired();
            await MarkUsed();
            return GetSessionInfoSync();
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
            return Task.FromResult(IsIdleTooLong());
        }

        private bool IsIdleTooLong()
        {
            return DateTime.UtcNow - State.LastCallTime > options.IdleTimeout;
        }

        /// <summary>
        /// Throws <see cref="SessionNotInitializedException"/> for an unknown or ended session and
        /// <see cref="SessionExpiredException"/> for one idle past the timeout, which it also clears.
        /// </summary>
        private async Task EnsureNotExpired()
        {
            AssertSessionInitialized();
            if (IsIdleTooLong())
            {
                await Clear();
                throw new SessionExpiredException("The session expired after being idle too long.");
            }
        }

        private Task MarkUsed()
        {
            State.LastCallTime = DateTime.UtcNow;
            return State.LastCallTime - lastWrittenCallTime >= options.TouchWriteInterval
                ? WriteState()
                : Task.CompletedTask;
        }

        private async Task WriteState()
        {
            await state.WriteStateAsync();
            lastWrittenCallTime = State.LastCallTime;
        }

        private async Task Clear()
        {
            if (state.RecordExists)
                await state.ClearStateAsync();
            state.State = new SessionState();
            lastWrittenCallTime = default;
        }

        public Task EndSession()
        {
            return Clear();
        }

        public async Task StartAuthenticatedUserSession(string ticket, string userId)
        {
            await EnsureNotExpired();
            State.UserID = userId;
            State.Ticket = ticket;
            State.StartTime = DateTime.UtcNow;
            State.LastCallTime = State.StartTime;
            await WriteState();
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

        public async Task Touch()
        {
            await EnsureNotExpired();
            await MarkUsed();
        }

        public async Task InitializeSession(Guid developerKey, Guid gameKey)
        {
            State.DeveloperKey = developerKey;
            State.GameKey = gameKey;
            State.IsInitialized = true;
            State.StartTime = DateTime.UtcNow;
            // Start the idle clock here, not on the first touch, so a session that is never used
            // still expires.
            State.LastCallTime = State.StartTime;
            await WriteState();
        }

        public Task<SessionGameInfo> GetDeveloperKey()
        {
            return Task.FromResult(GetSessionInfoSync());
        }
    }

    [GenerateSerializer]
    [Alias("wildcat.SessionState")]
    public class SessionState
    {
        [Id(0)] public string UserID { get; set; }

        /// <summary>
        /// UTC time of the last call that touched the session.
        /// </summary>
        [Id(1)] public DateTime LastCallTime { get; set; }

        /// <summary>
        /// UTC time the session was initialized or last bound to a user.
        /// </summary>
        [Id(2)] public DateTime StartTime { get; set; }
        [Id(3)] public string Ticket { get; set; }
        [Id(4)] public Guid DeveloperKey { get; set; }
        [Id(5)] public Guid GameKey { get; set; }
        [Id(6)] public bool IsInitialized { get; set; }


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

