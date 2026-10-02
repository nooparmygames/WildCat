namespace WildCat.Scripting
{
    /// <summary>
    /// Limits for script events. The defaults apply without any registration (grains read the
    /// class through IOptions); bind it with services.Configure&lt;ScriptOptions&gt;(...) to change
    /// them, for example from the "Scripts" configuration section.
    /// </summary>
    public class ScriptOptions
    {
        public const string SectionName = "Scripts";

        public const int DefaultCacheCapacity = 500;

        /// <summary>
        /// Wall-clock limit for one script run, including the grain calls it makes. Default 3 seconds.
        /// </summary>
        public TimeSpan Timeout { get; set; } = TimeSpan.FromSeconds(3);

        /// <summary>
        /// How deeply script methods, local functions, lambdas and accessors may nest. Default 256.
        /// </summary>
        public int MaxCallDepth { get; set; } = 256;

        /// <summary>
        /// Compiled scripts each silo keeps, least recently used first out. Default 500.
        /// </summary>
        public int CacheCapacity { get; set; } = DefaultCacheCapacity;

        /// <summary>
        /// Threads of the dedicated pool that runs script code (<see cref="ScriptWorkerPool"/>),
        /// so a runaway script can only hold one of these, never a thread-pool thread the silo and
        /// the gateway need. 0 (the default) means one per processor. The process creates the
        /// pool on the first script run, so a change needs a restart.
        /// </summary>
        public int WorkerThreads { get; set; }

        /// <summary>
        /// Circuit breaker per game: while more than this many of a game's runs are still going
        /// after their time limit (stuck in a long library call the guard cannot interrupt, or
        /// waiting for a free worker), new runs of that game fail at once with
        /// ScriptThrottledException instead of queueing for the pool. Default 2.
        /// </summary>
        public int MaxOverdueRunsPerGame { get; set; } = 2;

        internal TimeSpan EffectiveTimeout => Timeout > TimeSpan.Zero ? Timeout : TimeSpan.FromSeconds(3);

        internal int EffectiveMaxCallDepth => MaxCallDepth > 0 ? MaxCallDepth : 256;

        internal int EffectiveWorkerThreads => WorkerThreads > 0 ? WorkerThreads : Environment.ProcessorCount;

        internal int EffectiveMaxOverdueRunsPerGame => MaxOverdueRunsPerGame >= 0 ? MaxOverdueRunsPerGame : 2;
    }
}
