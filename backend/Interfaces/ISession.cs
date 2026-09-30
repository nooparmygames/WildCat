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
        Task<SessionGameInfo> CheckSessionAndGetInfo();
        Task AddUserCall(int count=1);
        Task AddUserMessage(int messages);
        Task<bool> IsExpired();
        Task<SessionGameInfo> AddUserCallCheckSessionAndGetInfo();
        Task<SessionGameInfo> AddUserMessagesCheckSessionAndGetInfo(int messageCount);

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
