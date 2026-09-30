using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using Orleans;

namespace Interfaces
{
    public interface ISession : IGrainWithGuidKey
    {
        Task StartAuthenticatedUserSession(string ticket, string userId);
        Task EndSession();

        /// <summary>
        /// Marks the session as used now. A session expires after 60 idle minutes, and
        /// InitializeSession does not start that clock, so the gateway touches a new session
        /// right after creating it.
        /// </summary>
        Task Touch();
        Task<bool> IsExpired();

        /// <summary>
        /// Touches the session, then checks that it has not expired and returns its developer,
        /// game and user. Touching first means a session that is in use never expires.
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
