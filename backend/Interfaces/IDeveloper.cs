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
    [GenerateSerializer]
    [Alias("wildcat.AccountingCollection")]
    public class AccountingCollection
    {
        [Id(0)] public DateTime StartDate { get; set; }
        [Id(1)] public DateTime EndDate { get; set; }
        [Id(2)] public List<AccountingInfo> Games { get; set; }

        public AccountingCollection()
        {

        }
    }

    [Serializable]
    [GenerateSerializer]
    [Alias("wildcat.AccountingInfo")]
    public class AccountingInfo
    {
        [Id(0)] public string GameName { get; set; }
        [Id(1)] public int CallCount { get; set; }
        [Id(2)] public ulong MessageCount { get; set; }
        [Id(3)] public string GameKey { get; set; }

        public AccountingInfo()
        {

        }
        public override string ToString()
        {
            return $"CallCount: {CallCount} BytesCount:{MessageCount} {GameName}";
        }
    }

    [Serializable]
    [GenerateSerializer]
    [Alias("wildcat.DeveloperState")]
    public class DeveloperState
    {
        [GenerateSerializer, Alias("wildcat.DeveloperState.DeveloperStatus")]
        public enum DeveloperStatus { Normal, Banned, Limited }

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

        public DeveloperState()
        {
            PasswordHash = "";
            GameInfos = new List<GameInfo>();
            AdditionalData = new Dictionary<string, string>();
        }
    }
}
