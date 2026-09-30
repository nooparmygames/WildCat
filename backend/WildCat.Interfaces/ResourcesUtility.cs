using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace WildCat.Interfaces
{
    /// <summary>
    /// Serves as a utility to create resource names and resource manager names consistently
    /// </summary>
    public static class ResourcesUtility
    {
        /// <summary>
        /// Gets class name of the Leaderboard implementation.
        /// </summary>
        /// <returns></returns>
        /// <remarks>This is mostly used for getting a resource grain of the leaderboard type.
        /// You have to pass a class name for a grain if the interface (IresourceBase here) is implemented
        /// by multiple classes.
        /// </remarks>
        public static string GetLeaderBoardClassName()
        {
            return "WildCat.Grains.LeaderBoard";
        }

        public static string GetEventScriptClassName()
        {
            return "WildCat.Scripting.ScriptEventCompiler";
        }

        /// <summary>
        /// Gets class name of the Item implementation.
        /// </summary>
        /// <returns></returns>
        /// <remarks>This is mostly used for getting a resource grain of the Item type.
        /// You have to pass a class name for a grain if the interface (IresourceBase here) is implemented
        /// by multiple classes.
        /// </remarks>
        public static string GetGameItemsClassName()
        {
            
            return "WildCat.Grains.Item";
        }

        public static string GetGamePackageClassName()
        {
            return "WildCat.Grains.PackageContainer";
        }

        /// <summary>
        /// Gets class name of the PackageContainer implementation.
        /// </summary>
        /// <returns></returns>
        /// <remarks>This is mostly used for getting a resource grain of the Item type.
        /// You have to pass a class name for a grain if the interface (IresourceBase here) is implemented
        /// by multiple classes.
        /// </remarks>
        public static string GetGamePackagesClassName()
        {
            return "WildCat.Grains.PackageContainer";
        }

        /// <summary>
        /// Returns the name of leaderboard resource manager for a specific game
        /// </summary>
        /// <param name="gameKey">game key</param>
        /// <returns></returns>
        public static string GetLeaderboardResourceManagerName(string gameKey)
        {
            return $"{gameKey}_leaderboards";
        }

        public static string GetEventScriptResourceManagerName(string gameKey)
        {
            return $"{gameKey}_eventScripts";
        }

        /// <summary>
        /// Returns the name of item resource manager for a specific game
        /// </summary>
        /// <param name="gameKey">game key</param>
        /// <returns></returns>
        public static string GetItemManagerName(string gameKey)
        {
            return gameKey + "_items";
        }

        /// <summary>
        /// Returns the name of package resource manager for a specific game
        /// </summary>
        /// <param name="gameKey">game key</param>
        /// <returns></returns>
        public static string GetPackageManagerName(string gameKey)
        {
            return gameKey + "_packages";
        }

        /// <summary>
        /// Returns a unique leaderboard key based on a game and a leaderboard name
        /// </summary>
        /// <param name="gameKey">Key of the title that the leaderboard belongs too</param>
        /// <param name="boardName">Name of the leaderboard being created</param>
        /// <returns>A unique leaderboard name which can be used</returns>
        public static string GetLeaderBoardKey(string gameKey, string boardName)
        {
            return $"{gameKey}_L_{boardName}";
        }

        public static string GetEventScriptKey(string gameKey, string eventScriptName)
        {
            return $"{gameKey}_G_{eventScriptName}";
        }

        /// <summary>
        /// Returns a unique leaderboard key based on a game and a leaderboard name
        /// </summary>
        /// <param name="gameKey">Key of the title that the leaderboard belongs too</param>
        /// <param name="itemName">Name of the leaderboard being created</param>
        /// <returns>A unique leaderboard name which can be used</returns>
        public static string GetItemKey(string gameKey, string itemName)
        {
            return $"{gameKey}_I_{itemName}";
        }

        /// <summary>
        /// Returns a unique leaderboard key based on a game and a leaderboard name
        /// </summary>
        /// <param name="gameKey">Key of the title that the leaderboard belongs too</param>
        /// <param name="packaeName">Name of the leaderboard being created</param>
        /// <returns>A unique leaderboard name which can be used</returns>
        public static string GetPackageKey(string gameKey, string packaeName)
        {
            return $"{gameKey}_P_{packaeName}";
        }

        public static string GetPlayPlayerStorageKey(string gameKey, string playerKey)
        {
            return $"{gameKey}_{playerKey}";
        }

        public static string GetPlayPlayerStorageKey(string gameKey, string playerKey, string fileName)
        {
            return $"{gameKey}_{playerKey}_{fileName}";
        }

        public static string GetPlayerInventoryKey(string gameKey, string player, string inventoryName = "")
        {
            string namePart = string.IsNullOrEmpty(inventoryName) ? "" : $"{inventoryName}_";
            return $"{gameKey}_{player}_{namePart}inv";
        }

        public static (string gameKey, string playerId, string inventoryName) GetPlayerInventoryparts(string all)
        {
            var parts = all.Split("_");
            return (parts[0], parts[1], (parts.Length > 2 ? parts[2] : ""));
        }

    }
}   