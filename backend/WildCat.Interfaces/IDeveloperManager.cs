using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using Orleans;
using Orleans.Concurrency;

namespace WildCat.Interfaces
{
    public interface IDeveloperManager : IGrainWithStringKey
    {
        Task<Guid> AddDeveloper(DeveloperState data);
        Task<Guid> AddDeveloper(DeveloperState data, Guid id);
        Task<IDeveloper> GetDeveloper(string email);
        Task<List<IDeveloper>> GetDevelopers();

        Task<IDeveloper> GetDeveloperByKey(string key);
        Task AddDeveloperToSIByKey(string key,IDeveloper developer);
        Task RemoveDeveloperFromSIKeys(string key);

        /// <summary>
        /// Self-registration from the admin panel. Creates a developer with status
        /// <see cref="DeveloperState.DeveloperStatus.NotActivated"/>, hashes the password and returns
        /// the new key with an activation code to email. Returns null when the email is already
        /// registered (compared without case). Throws <see cref="ArgumentException"/> for an empty
        /// email or an unacceptable password.
        /// </summary>
        Task<DeveloperRegistration> RegisterDeveloper(string email, string password, string firstName, string lastName);

        /// <summary>
        /// One page of registered developers ordered by email. <paramref name="search"/> matches a
        /// part of the email without case; null or empty returns everyone.
        /// </summary>
        [ReadOnly]
        Task<DeveloperSearchResult> SearchDevelopers(string search, int skip, int take);

        /// <summary>
        /// Records which developer owns a game so admins can reach any game by its key. The admin
        /// API keeps this index up to date for the games it creates and deletes.
        /// </summary>
        Task SetGameOwner(Guid gameKey, Guid developerKey);
        Task RemoveGameOwner(Guid gameKey);

        /// <summary>
        /// The owner recorded by <see cref="SetGameOwner"/>, or Guid.Empty when the game is unknown.
        /// </summary>
        [ReadOnly]
        Task<Guid> GetGameOwner(Guid gameKey);
    }

    [GenerateSerializer]
    [Alias("wildcat.DeveloperManagerState")]
    public class DeveloperManagerState
    {
        [Id(0)] public Dictionary<string, Guid> developers { get; set; }
        [Id(1)] public Dictionary<string, Guid> SecondaryIndex { get; set; }
        [Id(2)] public Dictionary<Guid, Guid> GameOwners { get; set; }

        public DeveloperManagerState()
        {
            developers = new Dictionary<string, Guid>();
            SecondaryIndex = new Dictionary<string, Guid>();
            GameOwners = new Dictionary<Guid, Guid>();
        }
    }

    [GenerateSerializer]
    [Alias("wildcat.DeveloperRegistration")]
    public class DeveloperRegistration
    {
        [Id(0)] public Guid Key { get; set; }
        [Id(1)] public string ActivationCode { get; set; }
    }

    [GenerateSerializer]
    [Alias("wildcat.DeveloperSearchResult")]
    public class DeveloperSearchResult
    {
        [Id(0)] public int Total { get; set; }
        [Id(1)] public List<DeveloperIndexEntry> Items { get; set; } = new List<DeveloperIndexEntry>();
    }

    [GenerateSerializer]
    [Alias("wildcat.DeveloperIndexEntry")]
    public class DeveloperIndexEntry
    {
        [Id(0)] public string Email { get; set; }
        [Id(1)] public Guid Key { get; set; }
    }
}
