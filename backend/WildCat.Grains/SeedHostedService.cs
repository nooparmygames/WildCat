using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using WildCat.Interfaces;

namespace WildCat.Grains
{
    /// <summary>
    /// Creates the developers and games listed in <see cref="SeedOptions"/>, each game with the
    /// leaderboard "l1", when they do not exist yet. Existing developers (matched by email), games
    /// (matched by key, then by name) and leaderboards are left alone, so it is safe on every start.
    /// Register it after UseOrleans so the silo is running when it starts; hosted services start
    /// in registration order, so the web server only accepts clients once the seed is done.
    /// </summary>
    public class SeedHostedService : IHostedService
    {
        /// <summary>The leaderboard every seeded game gets, as the legacy seed did.</summary>
        public const string LeaderBoardName = "l1";

        private const string DeveloperManagerKey = "main";

        private readonly IGrainFactory grains;
        private readonly SeedOptions options;
        private readonly ILogger<SeedHostedService> logger;

        public SeedHostedService(IGrainFactory grains, IOptions<SeedOptions> options, ILogger<SeedHostedService> logger)
        {
            this.grains = grains;
            this.options = options.Value;
            this.logger = logger;
        }

        public async Task StartAsync(CancellationToken cancellationToken)
        {
            foreach (var developer in options.Developers ?? new List<SeedDeveloper>())
            {
                cancellationToken.ThrowIfCancellationRequested();
                await SeedDeveloper(developer);
            }
        }

        public Task StopAsync(CancellationToken cancellationToken) => Task.CompletedTask;

        private async Task SeedDeveloper(SeedDeveloper seed)
        {
            if (seed.Key == Guid.Empty || string.IsNullOrWhiteSpace(seed.Email))
                throw new InvalidOperationException($"Every {SeedOptions.SectionName}:Developers entry needs a Key and an Email.");

            var manager = grains.GetGrain<IDeveloperManager>(DeveloperManagerKey);
            var developer = await manager.GetDeveloper(seed.Email);
            if (developer == null)
            {
                var existing = await manager.GetDevelopers();
                if (existing.Any(d => d.GetPrimaryKey() == seed.Key))
                {
                    logger.LogWarning(
                        "Skipping the seed developer {Email}: its key {Key} already belongs to a developer with another email.",
                        seed.Email, seed.Key);
                    return;
                }
                await manager.AddDeveloper(new DeveloperState { FirstName = seed.Name, Email = seed.Email }, seed.Key);
                developer = grains.GetGrain<IDeveloper>(seed.Key);
                logger.LogInformation("Seeded developer {Email} with key {Key}", seed.Email, seed.Key);
            }
            else if (developer.GetPrimaryKey() != seed.Key)
            {
                logger.LogWarning(
                    "The seed developer {Email} already exists with key {ExistingKey}, not {Key}; its games are seeded there.",
                    seed.Email, developer.GetPrimaryKey(), seed.Key);
            }

            var games = (await developer.GetGames()).ToList();
            foreach (var game in seed.Games ?? new List<SeedGame>())
            {
                if (game.Key == Guid.Empty || string.IsNullOrWhiteSpace(game.Name))
                    throw new InvalidOperationException(
                        $"Every game of the seed developer {seed.Email} needs a Key and a Name.");

                var found = games.FirstOrDefault(g => g.Key == game.Key) ?? games.FirstOrDefault(g => g.Name == game.Name);
                Guid gameKey;
                if (found == null)
                {
                    gameKey = await developer.AddGame(new GameInfo { Name = game.Name }, game.Key);
                    logger.LogInformation("Seeded game {Name} with key {Key}", game.Name, gameKey);
                }
                else
                {
                    gameKey = found.Key;
                    if (found.Key != game.Key)
                        logger.LogWarning(
                            "The seed game {Name} already exists with key {ExistingKey}, not {Key}.",
                            game.Name, found.Key, game.Key);
                }
                await SeedLeaderBoard(gameKey);
            }
        }

        private async Task SeedLeaderBoard(Guid gameKey)
        {
            var resources = grains.GetGrain<IResourceManager>(ResourcesUtility.GetLeaderboardResourceManagerName(gameKey.ToString()));
            string key = ResourcesUtility.GetLeaderBoardKey(gameKey.ToString(), LeaderBoardName);
            if (await resources.HasResource(key))
                return;
            var leaderBoard = grains.GetGrain<ILeaderBoard>(key);
            await leaderBoard.Initialize(LeaderBoardName, DateTime.Now, DateTime.MaxValue, true);
            await resources.AddResource(leaderBoard);
            logger.LogInformation("Seeded leaderboard {Name} for game {GameKey}", LeaderBoardName, gameKey);
        }
    }
}
