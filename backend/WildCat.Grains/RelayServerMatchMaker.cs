using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging;
using Orleans;
using Orleans.Concurrency;
using Orleans.Runtime;
using WildCat.Interfaces;

namespace WildCat.Grains
{
    [Reentrant]
    class RelayServerMatchMaker : Grain, ICustomMatchMaker
    {
        private const int CheckPeriodSeconds = 3;
        private readonly Dictionary<string, Search> searches = new();
        private readonly ILogger<RelayServerMatchMaker> logger;
        private IGrainTimer timer;
        private IGameModeManager modes;
        private string gameKey;
        private string modeName;

        public RelayServerMatchMaker(ILogger<RelayServerMatchMaker> logger)
        {
            this.logger = logger;
        }

        public override Task OnActivateAsync(CancellationToken cancellationToken)
        {
            var game = this.GetPrimaryKey(out modeName);
            gameKey = game.ToString();
            modes = GrainFactory.GetGrain<IGameModeManager>(game);
            timer = this.RegisterGrainTimer<object>(OnTimer, null, new GrainTimerCreationOptions
            {
                DueTime = TimeSpan.FromSeconds(0.1),
                Period = TimeSpan.FromSeconds(CheckPeriodSeconds),
                Interleave = true,
            });
            return base.OnActivateAsync(cancellationToken);
        }

        public override Task OnDeactivateAsync(DeactivationReason reason, CancellationToken cancellationToken)
        {
            timer?.Dispose();
            foreach (var search in searches.Values.ToArray())
                Complete(search, null);
            return base.OnDeactivateAsync(reason, cancellationToken);
        }

        public Task<int> GetOnlinePlayersCount() => Task.FromResult(searches.Count);

        private bool IsCurrent(Search search) =>
            searches.TryGetValue(search.Player.GetPrimaryKeyString(), out var current) && ReferenceEquals(search, current);

        private void Complete(Search search, FoundRelayMatchResult result)
        {
            if (!IsCurrent(search))
                return;
            searches.Remove(search.Player.GetPrimaryKeyString());
            search.Completion.TrySetResult(result);
        }

        private async Task OnTimer(object unused)
        {
            if (searches.Count == 0)
                return;
            // A pass uses one snapshot, including while StartGame awaits other grains. The next
            // pass sees mode edits and deletions even when this activation has never gone idle.
            var mode = await modes.GetGameModeData(modeName) ?? GameMode.CreateDefault();
            foreach (var search in searches.Values.ToArray())
            {
                if (!await IsAvailable(search))
                    Complete(search, null);
            }

            foreach (var search in searches.Values.ToArray())
            {
                if (!IsCurrent(search) || search.Pending)
                    continue;
                if (!await IsAvailable(search))
                {
                    Complete(search, null);
                    continue;
                }
                var now = DateTime.UtcNow;
                var criterionIndex = GetCriterionIndex(mode, search, now);
                var criterion = mode.Criterias[criterionIndex];
                int required = criterion.IsMinPlayersEnough ? mode.MinPlayers - 1 : mode.MaxPlayers - 1;
                var opponents = searches.Values
                    .Where(candidate => !ReferenceEquals(candidate, search) && !candidate.Pending
                        && !HasExpired(candidate, now)
                        && GetCriterionIndex(mode, candidate, now) <= criterionIndex
                        && SkillDistance(candidate.Skill, search.Skill) <= criterion.AcceptableThreshold)
                    .OrderBy(candidate => SkillDistance(candidate.Skill, search.Skill))
                    .Take(mode.MaxPlayers - 1)
                    .ToArray();
                if (opponents.Length < required)
                    continue;

                var group = opponents.Append(search).ToArray();
                foreach (var member in group)
                    member.Pending = true;
                IRelayServerGame game = null;
                try
                {
                    game = GrainFactory.GetGrain<IRelayServerGame>(Guid.NewGuid(), gameKey, null);
                    await game.StartGame(group.Select(member => member.Player).ToArray(), mode);
                    foreach (var member in group)
                    {
                        if (!await IsAvailable(member))
                            Complete(member, null);
                    }
                    foreach (var member in group)
                    {
                        if (HasExpired(member, DateTime.UtcNow))
                            Complete(member, null);
                    }
                    if (group.Any(member => !IsCurrent(member)))
                    {
                        // A canceled player may already have been added to RunningGames by
                        // StartGame. Retire this incomplete match and leave its other searches queued.
                        await RetireGame(game, group);
                        continue;
                    }

                    foreach (var member in group)
                        Complete(member, new FoundRelayMatchResult(game));
                }
                catch (Exception ex)
                {
                    logger.LogWarning(ex, "Could not create a match in game {GameKey}, mode {ModeName}", gameKey, modeName);
                    if (game != null)
                        await RetireGame(game, group);
                }
                finally
                {
                    foreach (var member in group)
                        member.Pending = false;
                }
            }
        }

        private async Task RetireGame(IRelayServerGame game, Search[] group)
        {
            foreach (var member in group)
            {
                try
                {
                    await game.PlayerFinished(member.Player);
                }
                catch (Exception ex)
                {
                    logger.LogWarning(ex, "Could not remove player {PlayerId} from an incomplete match",
                        member.Player.GetPrimaryKeyString());
                }
            }
        }

        private static long SkillDistance(uint first, uint second) => Math.Abs((long)first - second);

        private static bool HasExpired(Search search, DateTime now) =>
            search.Cancellation.IsCancellationRequested || now - search.Started >= TimeSpan.FromSeconds(search.WaitSeconds);

        private static async Task<bool> IsAvailable(Search search)
        {
            if (HasExpired(search, DateTime.UtcNow))
                return false;
            if (search.LoginTicket != Guid.Empty && !await search.Player.IsLoginCurrent(search.LoginTicket))
                return false;
            return !HasExpired(search, DateTime.UtcNow);
        }

        private static int GetCriterionIndex(GameMode mode, Search search, DateTime now)
        {
            double elapsed = (now - search.Started).TotalSeconds;
            for (int index = mode.Criterias.Count - 1; index >= 0; index--)
            {
                if (elapsed >= mode.Criterias[index].StartTimeInSeconds)
                    return index;
            }
            throw new InvalidOperationException("The first matchmaking criterion must start at zero seconds.");
        }

        /// <summary>Waits for a match, or returns null when the search expires or is canceled.</summary>
        public async Task<FoundRelayMatchResult> FindMatch(IPlayer player, uint skill, uint maximumWaitTime,
            Guid loginTicket = default, CancellationToken cancellationToken = default)
        {
            cancellationToken.ThrowIfCancellationRequested();
            // Each call owns its cancellation. Replacing an older search must not let the old
            // socket's eventual cancellation remove the new socket's search for the same account.
            if (searches.TryGetValue(player.GetPrimaryKeyString(), out var previous))
                Complete(previous, null);
            var search = new Search(player, skill, maximumWaitTime, loginTicket, cancellationToken);
            searches.Add(player.GetPrimaryKeyString(), search);
            using var registration = cancellationToken.Register(() => Complete(search, null));
            return await search.Completion.Task;
        }

        public Task<bool> RemoveDisconnectedPlayer(IPlayer player)
        {
            if (searches.TryGetValue(player.GetPrimaryKeyString(), out var search))
                Complete(search, null);
            return Task.FromResult(true);
        }

        private sealed class Search(IPlayer player, uint skill, uint waitSeconds, Guid loginTicket, CancellationToken cancellation)
        {
            public IPlayer Player { get; } = player;
            public uint Skill { get; } = skill;
            public uint WaitSeconds { get; } = waitSeconds;
            public Guid LoginTicket { get; } = loginTicket;
            public DateTime Started { get; } = DateTime.UtcNow;
            public CancellationToken Cancellation { get; } = cancellation;
            public TaskCompletionSource<FoundRelayMatchResult> Completion { get; } =
                new(TaskCreationOptions.RunContinuationsAsynchronously);
            public bool Pending { get; set; }
        }
    }
}
