using System;
using System.Threading.Tasks;

namespace WildCat.Sdk.Tests
{
    /// <summary>Bounded waits, so a test that goes wrong fails instead of hanging the run.</summary>
    internal static class Wait
    {
        /// <summary>Upper bound for anything a test waits for.</summary>
        public static readonly TimeSpan Long = TimeSpan.FromSeconds(30);

        /// <summary>Checks <paramref name="condition"/> every 20 ms until it holds, for at most <paramref name="timeout"/>.</summary>
        public static async Task UntilAsync(Func<bool> condition, string description, TimeSpan? timeout = null)
        {
            DateTime deadline = DateTime.UtcNow + (timeout ?? Long);
            while (!condition())
            {
                if (DateTime.UtcNow > deadline)
                    throw new TimeoutException($"Gave up waiting for {description}.");
                await Task.Delay(20);
            }
        }

        /// <summary>Awaits <paramref name="task"/> for at most <see cref="Long"/>.</summary>
        public static Task<T> For<T>(Task<T> task)
        {
            return task.WaitAsync(Long);
        }

        public static Task For(Task task)
        {
            return task.WaitAsync(Long);
        }
    }

    internal static class Unique
    {
        /// <summary>A unique id without underscores (some server keys split on them).</summary>
        public static string Id(string prefix)
        {
            return $"{prefix}-{Guid.NewGuid():N}";
        }
    }
}
