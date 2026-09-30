using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using Orleans;

namespace Interfaces
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
        Task<bool> IsPasswordCorrect(string passwordHash);
        Task<Guid> InitializeSession(Guid gameKey);
        Task<AccountingCollection> GetAccountingInfo(DateTime StartDate, DateTime EndDate);
        Task UpdateDeveloperData(DeveloperState data);
        Task ChangePassword(string newPassword);
        Task<Guid> GenerateSecret();
    }

    [Serializable]
    public class AccountingCollection
    {
        public DateTime StartDate { get; set; }
        public DateTime EndDate { get; set; }
        public List<AccountingInfo> Games { get; set; }

        public AccountingCollection()
        {

        }
    }

    [Serializable]
    public class AccountingInfo
    {
        public string GameName { get; set; }
        public int CallCount { get; set; }
        public ulong MessageCount { get; set; }
        public string GameKey { get; set; }

        public AccountingInfo()
        {

        }
        public override string ToString()
        {
            return $"CallCount: {CallCount} BytesCount:{MessageCount} {GameName}";
        }
    }

    [Serializable]
    public class DeveloperState
    {
        public enum DeveloperStatus { Normal, Banned, Limited }

        public bool IsCreated { get; set; }
        public string PasswordHash { get; set; }
        public DeveloperStatus Status { get; set; }
        public string FirstName { get; set; }
        public string LastName { get; set; }
        public string Email { get; set; }
        public string PhoneNumber { get; set; }
        public string Address { get; set; }
        public string Description { get; set; }
        public Guid DeveloperSecret { get; set; }
        public ICollection<GameInfo> GameInfos { get; set; }
        public Dictionary<string, string> AdditionalData { get; set; }

        public DeveloperState()
        {
            PasswordHash = "";
            GameInfos = new List<GameInfo>();
            AdditionalData = new Dictionary<string, string>();
        }
    }
}
