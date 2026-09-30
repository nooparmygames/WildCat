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

        internal TimeSpan EffectiveTimeout => Timeout > TimeSpan.Zero ? Timeout : TimeSpan.FromSeconds(3);

        internal int EffectiveMaxCallDepth => MaxCallDepth > 0 ? MaxCallDepth : 256;
    }
}
