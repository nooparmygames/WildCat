namespace WildCat.Grains
{
    /// <summary>
    /// The grain storage providers the grains use. The host and the tests register them in
    /// <see cref="SiloBuilderExtensions.AddWildCatStorage"/>.
    /// </summary>
    public static class GrainStorageNames
    {
        /// <summary>The default provider, used by every grain except <see cref="LeaderBoard"/>.</summary>
        public const string Default = "Default";

        /// <summary>The leaderboards' provider. A separate name so it can move to other storage.</summary>
        public const string Blob = "blob";

        /// <summary>Stream subscriptions of the memory streams.</summary>
        public const string PubSub = "PubSubStore";
    }

    /// <summary>
    /// The state names the grains pass to [PersistentState], one per grain class. The ADO.NET
    /// provider keys a stored row by service id, grain key and state name only, not by grain class
    /// or provider name, so two classes with the same state name and the same key (Developer and
    /// DeveloperStorage share a Guid, PlayerFriendList and PlayerChallengeHandler a compound key)
    /// would read and overwrite each other's row. That is why the legacy <c>Grain&lt;T&gt;</c> base
    /// class, which stores every class under "state", is not used. GrainStateNameTests checks that
    /// the names stay unique. Renaming one orphans the rows already stored under the old name.
    /// </summary>
    public static class GrainStateNames
    {
        public const string CustomIdToAccountIdMap = "customIdMap";
        public const string Developer = "developer";
        public const string DeveloperManager = "developerManager";
        public const string DeveloperStorage = "developerStorage";
        public const string EmailToAccountIdMap = "emailMap";
        public const string GameModeManager = "gameModes";
        public const string Item = "item";
        public const string LeaderBoard = "leaderBoard";
        public const string LeaderBoardStorage = "leaderBoardChunk";
        public const string NameToAccountIdMap = "nameMap";
        public const string PackageContainer = "package";
        public const string Player = "player";
        public const string PlayerChallengeHandler = "challenges";
        public const string PlayerFriendList = "friendList";
        public const string PlayerInventory = "inventory";
        public const string PlayerStorage = "playerStorage";
        public const string RelayServerGame = "relayGame";
        public const string ResourceManager = "resources";
        public const string ScriptEventCompiler = "scriptEvent";
        public const string Session = "session";
        public const string UserIdToAccountIdMap = "userIdMap";
    }
}
