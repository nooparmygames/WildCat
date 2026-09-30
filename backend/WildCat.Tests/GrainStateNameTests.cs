using System.Reflection;
using Orleans;
using Orleans.Runtime;
using WildCat.Grains;
using WildCat.Interfaces;
using Xunit;

namespace WildCat.Tests
{
    /// <summary>
    /// The ADO.NET grain storage keys a row by service id, grain key and state name; neither the
    /// grain class nor the provider name is part of the key. Two grain classes that share a key
    /// (Developer and DeveloperStorage, PlayerFriendList and PlayerChallengeHandler, a leaderboard
    /// and a leaderboard chunk) therefore need different state names, or they overwrite each
    /// other's row. These tests check that rule over the whole grain assembly, so a new grain cannot
    /// bring the problem back. PostgreSqlStorageTests shows the collisions against a real database.
    /// </summary>
    public class GrainStateNameTests
    {
        private static readonly Assembly GrainAssembly = typeof(Player).Assembly;

        private static IEnumerable<Type> GrainClasses() =>
            GrainAssembly.GetTypes().Where(t => t.IsClass && !t.IsAbstract && typeof(Grain).IsAssignableFrom(t));

        private static IEnumerable<(Type Grain, PersistentStateAttribute Attribute)> PersistentStates() =>
            GrainClasses().SelectMany(t => t
                .GetConstructors(BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance)
                .SelectMany(c => c.GetParameters())
                .Select(p => p.GetCustomAttribute<PersistentStateAttribute>())
                .Where(a => a != null)
                .Select(a => (t, a)));

        [Fact]
        public void NoGrainDerivesFromTheLegacyGenericGrainBaseClass()
        {
            var legacy = GrainClasses()
                .Where(t => BaseTypes(t).Any(b => b.IsGenericType && b.GetGenericTypeDefinition() == typeof(Grain<>)))
                .Select(t => t.FullName)
                .ToList();

            // Grain<T> stores every class under the state name "state".
            Assert.Empty(legacy);
        }

        [Fact]
        public void EveryPersistentStateNameIsUsedByOneGrainClassOnly()
        {
            var states = PersistentStates().ToList();
            Assert.NotEmpty(states);

            var shared = states
                .GroupBy(s => s.Attribute.StateName, StringComparer.OrdinalIgnoreCase)
                .Where(g => g.Count() > 1)
                .Select(g => $"{g.Key}: {string.Join(", ", g.Select(s => s.Grain.Name))}")
                .ToList();

            Assert.Empty(shared);
        }

        [Fact]
        public void EveryPersistentStateNamesAStateAndARegisteredProvider()
        {
            foreach (var (grain, attribute) in PersistentStates())
            {
                Assert.False(string.IsNullOrWhiteSpace(attribute.StateName), grain.Name);
                Assert.Contains(attribute.StorageName, new[] { GrainStorageNames.Default, GrainStorageNames.Blob });
            }
        }

        [Fact]
        public void TheGrainsThatShareKeysAreAllStateful()
        {
            // The pairs from the storage collision review; each needs its own [PersistentState].
            var stateful = PersistentStates().Select(s => s.Grain).ToHashSet();
            foreach (var name in new[]
                     {
                         "Developer", "DeveloperStorage", "PlayerFriendList", "PlayerChallengeHandler",
                         "LeaderBoard", "LeaderBoardStorage", "Player", "Session", "ScriptEventCompiler",
                     })
            {
                Assert.Contains(stateful, t => t.Name == name);
            }
        }

        [Fact]
        public void LeaderBoardChunkKeysDoNotCollideWithOtherBoardsOrChunks()
        {
            string game = Guid.NewGuid().ToString();
            string l1 = ResourcesUtility.GetLeaderBoardKey(game, "l1");
            string l10 = ResourcesUtility.GetLeaderBoardKey(game, "l10");
            string l11 = ResourcesUtility.GetLeaderBoardKey(game, "l11");

            // "{game}_L_l1" + "0" used to be the key of the board l10, and "{game}_L_l1" + "10" the key
            // of chunk 0 of l11.
            Assert.NotEqual(l10, ResourcesUtility.GetLeaderBoardChunkKey(l1, 0));
            Assert.NotEqual(ResourcesUtility.GetLeaderBoardChunkKey(l11, 0), ResourcesUtility.GetLeaderBoardChunkKey(l1, 10));

            var keys = new[] { "l1", "l10", "l11", "l1.0", "l" }
                .SelectMany(name => Enumerable.Range(0, 12)
                    .Select(i => ResourcesUtility.GetLeaderBoardChunkKey(ResourcesUtility.GetLeaderBoardKey(game, name), i)))
                .ToList();
            Assert.Equal(keys.Count, keys.Distinct().Count());
        }

        private static IEnumerable<Type> BaseTypes(Type type)
        {
            for (var t = type.BaseType; t != null; t = t.BaseType)
                yield return t;
        }
    }
}
