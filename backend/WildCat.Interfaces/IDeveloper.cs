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
        /// </summary>
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
