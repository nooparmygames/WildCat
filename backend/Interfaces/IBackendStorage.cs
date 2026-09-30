using PlayFab.ServerModels;
using System.Collections.Generic;
using System.Threading.Tasks;

namespace Interfaces
{
    public interface IBackendStorageServer
    {
        Task<(int, string, string)> GetPlayfabIdFromUserName(string userName);
        Task<bool> IsPlayfabRun();
        Task<string> GetPlayfabId();
        Task<(int, string, GetPlayerCombinedInfoResultPayload, string, string)> ValidateSessionTicket(string ticket);
        Task<(int, string)> CreateCharacter(string playfabId, string type, string name);
        Task<(int, string)> DeleteCharacter(string playfabId, string characterId);
        Task<(int, string, List<CharacterResult>)> GetCharacters(string playfabId);
        Task<(int, string, Dictionary<string, PlayFab.DataModels.ObjectResult>, int)> GetObjects(string entityId, string entityType);
        Task<(int, string, int)> SetObjects(string entityId, string entityType, List<PlayFab.DataModels.SetObject> objects, int? version);
        Task<(int, string, int)> SaveFile(string entityId, string entityTYpe, Dictionary<string, byte[]> fileNames, int? version);
        Task<(int, string, Dictionary<string, byte[]>, int)> LoadFile(string entityId, string entityType, string[] fileName);
        Task<(int, string)> SaveStats(string characterId, string playfabId, Dictionary<string, int> stats);
        Task<(int, string)> SavePlayerStats(string playfabId, List<StatisticUpdate> stats);
        string GetSecret();
        Task<(int, string)> ResetLeaderboard(string name);
        Task<(int, string, GetLeaderboardAroundUserResult)> GetLeaderboardAroundUser(string name, string playfabId);
        Task<(int, string, GetLeaderboardResult)> GetLeaderboard(string name);
        Task<(int, string, int)> AddCurrency(string playfabId, string characterId, string currency, int amount);
        Task<(int, string, GetCharacterInventoryResult)> LoadCharacterInventory(string playfabId, string characterId);
        Task<(int, string, Dictionary<string, string>)> LoadTitleData(List<string> keys);
        Task SaveTitleData(Dictionary<string, string> data);
        Task<(int, string)> CloseServerForMatchMaking(string lobbyId);
        Task<(int, string)> SendConformationEmail(string playfabId);
        System.DateTime GetCurrentTime();
        void AddDateTimeOffset(System.TimeSpan ts);
        Task<(int, string, GetCatalogItemsResult)> GetCatalog(string version);
        Task<(int, string)> ConsumeItem(string characterId, string playfabId, string itemId, int consumeAmount, int trycount = 5);
        Task<(int, string)> SendNotification(string playfabId, string template);
        void SetTitleId(string titleId);
        Task WaitForInitialization();
        string GetTitleId();
        Task<(int, string, string token, string[] players,int count)> GetPlayersForSegment(string segment, string token);
        Task<(int code, string msg, string entityId, string entityType)> GetPlayerEntityInfo(string player);
    }

    /// <summary>
    /// Contains all entity type names for playfab which combined with entity ID allows file and object CRUD operations
    /// </summary>
    public static class PlayfabEntityHelper
    {
        public const string NameSpace = "namespace";//publisher id is the entity id (none-real-time)
        public const string Title = "title";//title id is the entity id (non-real-time)
        public const string MasterPlayerAccount = "master_player_account";//playfabID is the entity id
        public const string TitlePlayerAccount = "title_player_account";//entity id is login result's entityToken.entityId in login result
        public const string Character = "character";//entity id is character id
        public const string Group = "group";//entity id is group's id in create group or list memberships groups
        public const string memberRoleId = "members";
        public const string adminRoleId = "admins";

        public static PlayFab.ClientModels.EntityKey EntityKeyMakerCM(string entityId, string entityType)
        {
            return new PlayFab.ClientModels.EntityKey { Id = entityId, Type = entityType };
        }

        public static PlayFab.GroupsModels.EntityKey EntityKeyMakerGM(string entityId, string entityType)
        {
            return new PlayFab.GroupsModels.EntityKey { Id = entityId, Type = entityType };
        }
    }

}