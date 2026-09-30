using Interfaces;
using Orleans;
using Orleans.Runtime;
using System;
using System.Collections.Generic;
using System.Text;
using System.Threading;
using System.Threading.Tasks;

namespace SiloHost
{
    public class StartupTasks : IStartupTask
    {
        private const string CONFIG_PATH = "devsconfig.json";
        private readonly IGrainFactory grainFactory;
        private readonly IBackendStorageServer storage;

        public StartupTasks(IGrainFactory grainFactory, IBackendStorageServer storagemanager)
        {
            this.grainFactory = grainFactory;
            this.storage = storagemanager;
        }

        public async Task Execute(CancellationToken cancellationToken)
        {
            var configPath = FindConfigFile();
            if (configPath != null)
            {
                string json = System.IO.File.ReadAllText(configPath);
                var devs = Newtonsoft.Json.JsonConvert.DeserializeObject<DeveloperConfigCollection>(json);
                if (devs.developers.Count>1)
                {
                    return;
                }
                await CreateDeveloperAndGame(devs.developers[0]);
                
            }
            else
            {
                DeveloperConfigCollection ccc = new DeveloperConfigCollection();
                ccc.developers = new List<DeveloperConfig>();
                ccc.developers.Add(new DeveloperConfig
                {
                    name = "n1",
                    email = "developer@example.com",
                    playfabId = "",
                    key = Guid.NewGuid(),
                    games = new List<DeveloperConfig.GameInfo> { new DeveloperConfig.GameInfo { name = "g1", key = Guid.NewGuid() } }
                });
                try
                {
                    string json = Newtonsoft.Json.JsonConvert.SerializeObject(ccc);
                    Console.WriteLine(">>>>>>>>>   " + json);
                    System.IO.File.WriteAllText(CONFIG_PATH, json);
                }
                catch (Exception ex)
                {
                    Console.WriteLine(ex.Message);
                }
            }
        }

        /// <summary>
        /// Looks for the seed file in the working directory first, then next to the binaries
        /// (the project copies it to the output directory). Returns null when neither exists.
        /// </summary>
        private static string FindConfigFile()
        {
            if (System.IO.File.Exists(CONFIG_PATH))
                return CONFIG_PATH;
            var besideBinaries = System.IO.Path.Combine(AppContext.BaseDirectory, CONFIG_PATH);
            return System.IO.File.Exists(besideBinaries) ? besideBinaries : null;
        }

        private async Task CreateDeveloperAndGame(DeveloperConfig config)
        {
            var developer = await grainFactory.GetGrain<IDeveloperManager>("main").GetDeveloper(config.email);
            if (developer == null)
            {
                var developerKey = await grainFactory.GetGrain<IDeveloperManager>("main").AddDeveloper(CreateDeveloper(config), config.key);
                developer = grainFactory.GetGrain<IDeveloper>(developerKey);
            }
            
            storage.SetTitleId(config.playfabId);
            await storage.WaitForInitialization();
            
            Guid gameKey = Guid.Empty;
            var allGames = await developer.GetGames();
            for (int i = 0; i < config.games.Count; ++i)
            {
                var gameExistFlag = false;
                foreach (var gameInfo in allGames)
                {
                    if (gameInfo.Name == config.games[i].name)
                    {
                        gameKey = gameInfo.Key;
                        gameExistFlag = true;
                        break;
                    }
                }

                if (!gameExistFlag)
                {
                    gameKey = await developer.AddGame(new GameInfo { Name = config.games[i].name }, config.games[i].key);
                }
                var leaderBoard =
                    grainFactory.GetGrain<ILeaderBoard>(ResourcesUtility.GetLeaderBoardKey(gameKey.ToString(), "l1"));
                await leaderBoard.Initialize("l1", DateTime.Now, DateTime.MaxValue, true);
                var success =
                    await grainFactory.GetGrain<IResourceManager>(ResourcesUtility.GetLeaderboardResourceManagerName(gameKey.ToString()))
                        .AddResource(leaderBoard);
            }
                
            
            Console.WriteLine("**********");
            // Console.WriteLine($"DeveloperKey: {developerKey}\tGameKey: {gameKey}");
            Console.WriteLine("Leaderboard: l1");
            Console.WriteLine("**********");
        }

        private DeveloperState CreateDeveloper(DeveloperConfig config)
        {
            DeveloperState data = new DeveloperState
            {
                FirstName = config.name,
                Email = config.email
            };
            return data;
        }
    }

    /// <summary>
    /// We read this type as a json config to add developers to the game automatically
    /// </summary>
    public class DeveloperConfigCollection
    {
        public List<DeveloperConfig> developers;
        public bool isPlayfab;
    }

    public class DeveloperConfig
    {
        public Guid key;
        public string name;
        public string playfabId;
        public string email;
        public List<GameInfo> games;
        public class GameInfo
        {
            public Guid key;
            public string name;
        }
    }
}