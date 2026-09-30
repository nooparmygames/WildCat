using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using Orleans;
using Orleans.Concurrency;

namespace WildCat.Interfaces
{
    public interface IDeveloper : IGrainWithGuidKey
    {
        Task CreateDeveloper(DeveloperState data);
        Task<DeveloperState> GetDeveloperData();
        Task ChangeName(string first, string last);
        Task<Guid> AddGame(GameInfo game);
        Task<Guid> AddGame(GameInfo game,Guid id);
        Task EditGame(Guid gameKey, GameInfo game);
        Task RemoveGame(Guid game);
        Task<bool> IsGameValid(Guid gameKey);
        Task<IEnumerable<GameInfo>> GetGames();
        /// <summary>
        /// Checks a plain password against the stored hash (see <see cref="PasswordHasher"/>).
        /// Read only, so password checks and <see cref="IsSecretValid"/> calls run while the
        /// hashing waits on the thread pool.
        /// </summary>
        [ReadOnly]
        Task<bool> IsPasswordCorrect(string password);
        Task<Guid> InitializeSession(Guid gameKey);
        Task UpdateDeveloperData(DeveloperState data);
        Task ChangePassword(string newPassword);
        Task<Guid> GenerateSecret();

        /// <summary>
        /// True when <paramref name="secret"/> is this developer's current secret. The comparison
        /// runs in constant time; an uncreated developer has no valid secret.
        /// </summary>
        [ReadOnly]
        Task<bool> IsSecretValid(Guid secret);

        /// <summary>
        /// Creates a new email activation code for a developer whose status is
        /// <see cref="DeveloperState.DeveloperStatus.NotActivated"/> and returns it so the caller can
        /// email it. Only a hash of the code is stored; an earlier code stops working. Returns null
        /// when the developer is already activated.
        /// </summary>
        Task<string> CreateActivationCode();

        /// <summary>
        /// Activates the developer when <paramref name="code"/> matches the current activation code.
        /// The code that activated the account keeps returning true until it expires, so a repeated
        /// request succeeds; any other code returns false whatever the account's status, so the
        /// answer does not reveal whether the account is active. A banned account always returns
        /// false. A code stops working after it expires or after five wrong attempts.
        /// </summary>
        Task<bool> Activate(string code);

        /// <summary>
        /// Creates a password reset code, valid for one hour, and returns it so the caller can email
        /// it. Only a hash of the code is stored; an earlier code stops working.
        /// </summary>
        Task<string> CreatePasswordResetCode();

        /// <summary>
        /// Checks <paramref name="code"/> against the current password reset code and, when it
        /// matches, uses it up. The caller then sets the new password with
        /// <see cref="ChangePassword"/>. A code stops working after it expires or after five wrong
        /// attempts.
        /// </summary>
        Task<bool> ConsumePasswordResetCode(string code);
    }

    /// <summary>
    /// A one-time code sent by email (activation or password reset). Only the SHA-256 hash of the
    /// code is kept.
    /// </summary>
    [GenerateSerializer]
    [Alias("wildcat.OneTimeCodeState")]
    public class OneTimeCodeState
    {
        [Id(0)] public string Hash { get; set; }
        [Id(1)] public DateTime ExpiresAtUtc { get; set; }
        [Id(2)] public int FailedAttempts { get; set; }
    }

    [Serializable]
    [GenerateSerializer]
    [Alias("wildcat.DeveloperState")]
    public class DeveloperState
    {
        /// <summary>
        /// Normal is an active account. NotActivated is a self-registered account that has not
        /// confirmed its email yet; accounts created any other way start as Normal.
        /// </summary>
        [GenerateSerializer, Alias("wildcat.DeveloperState.DeveloperStatus")]
        public enum DeveloperStatus { Normal, Banned, Limited, NotActivated }

        [Id(0)] public bool IsCreated { get; set; }
        [Id(1)] public string PasswordHash { get; set; }
        [Id(2)] public DeveloperStatus Status { get; set; }
        [Id(3)] public string FirstName { get; set; }
        [Id(4)] public string LastName { get; set; }
        [Id(5)] public string Email { get; set; }
        [Id(6)] public string PhoneNumber { get; set; }
        [Id(7)] public string Address { get; set; }
        [Id(8)] public string Description { get; set; }
        [Id(9)] public Guid DeveloperSecret { get; set; }
        [Id(10)] public ICollection<GameInfo> GameInfos { get; set; }
        [Id(11)] public Dictionary<string, string> AdditionalData { get; set; }
        [Id(12)] public OneTimeCodeState ActivationCode { get; set; }
        [Id(13)] public OneTimeCodeState PasswordResetCode { get; set; }

        public DeveloperState()
        {
            PasswordHash = "";
            GameInfos = new List<GameInfo>();
            AdditionalData = new Dictionary<string, string>();
        }
    }
}
