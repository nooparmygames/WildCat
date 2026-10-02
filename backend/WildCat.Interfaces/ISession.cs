using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using Orleans;

namespace WildCat.Interfaces
{
    public interface ISession : IGrainWithGuidKey
    {
        Task StartAuthenticatedUserSession(string ticket, string userId);
        Task EndSession();

        /// <summary>
        /// Checks that the session is initialized and not expired, then marks it as used now.
        /// A session expires after 60 idle minutes by default (SessionOptions.IdleTimeout in
        /// WildCat.Grains); the clock starts in <see cref="InitializeSession"/>. Throws
        /// SessionNotInitializedException for an unknown or ended session and
        /// SessionExpiredException for an expired one, which is cleared.
        /// </summary>
        Task Touch();
        Task<bool> IsExpired();

        /// <summary>
        /// Same checks as <see cref="Touch"/>, then returns the session's developer, game and user.
        /// The expiry check runs before the touch, so an idle session is rejected, not revived.
        /// </summary>
        Task<SessionGameInfo> TouchAndGetInfo();

        Task<SessionGameInfo> GetDeveloperKey();
        Task<bool> IsInitialized();
        Task InitializeSession(Guid developerKey, Guid gameKey);
    }

    [GenerateSerializer]
    [Alias("wildcat.SessionGameInfo")]
    public class SessionGameInfo
    {
        [Id(0)] public Guid DeveloperKey { get; set; }
        [Id(1)] public Guid GameKey { get; set; }
        [Id(2)] public string UserID { get; set; }
    }
}
