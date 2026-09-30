namespace WildCat.Scripting
{
    /// <summary>
    /// Identifies one compiled version of a script event. The hash covers the event name,
    /// parameter types and source, so an edited script gets a new key on every silo and stale
    /// entries simply age out; no invalidation messages are needed.
    /// </summary>
    public readonly record struct ScriptCacheKey(string GameKey, string EventName, string ScriptHash);

    /// <summary>
    /// The compiled scripts of one silo: thread safe, bounded, least recently used entries evicted
    /// first. Each entry is a lazily started compilation, so concurrent runs of a script that is
    /// not cached yet share one compilation. Any silo can fill its cache from the source held by
    /// the event's IScriptEventCompiler grain.
    ///
    /// Roslyn script assemblies are not collectible, so an evicted entry frees the cache slot but
    /// not the loaded assembly; the capacity bounds the cache, not the process.
    /// </summary>
    public sealed class ScriptCache
    {
        /// <summary>
        /// The cache the grains use. Its capacity follows <see cref="ScriptOptions.CacheCapacity"/>.
        /// </summary>
        public static ScriptCache Shared { get; } = new ScriptCache(ScriptOptions.DefaultCacheCapacity);

        private readonly object gate = new object();
        private readonly Dictionary<ScriptCacheKey, LinkedListNode<Entry>> entries = new Dictionary<ScriptCacheKey, LinkedListNode<Entry>>();
        private readonly LinkedList<Entry> recency = new LinkedList<Entry>();
        private int capacity;
        private long compilations;

        public ScriptCache(int capacity)
        {
            this.capacity = Math.Max(1, capacity);
        }

        public int Capacity
        {
            get
            {
                lock (gate)
                    return capacity;
            }
            set
            {
                lock (gate)
                {
                    capacity = Math.Max(1, value);
                    Trim();
                }
            }
        }

        public int Count
        {
            get
            {
                lock (gate)
                    return entries.Count;
            }
        }

        /// <summary>
        /// How many compilations this cache has started, for tests and diagnostics.
        /// </summary>
        public long Compilations => Interlocked.Read(ref compilations);

        public bool Contains(ScriptCacheKey key)
        {
            lock (gate)
                return entries.ContainsKey(key);
        }

        /// <summary>
        /// Returns the cached compilation for <paramref name="key"/>, starting
        /// <paramref name="compile"/> on a miss. <paramref name="compile"/> should return quickly
        /// with a task (run the CPU work with Task.Run); it runs outside the cache lock.
        /// </summary>
        public Task<CompiledScript> GetOrAdd(ScriptCacheKey key, Func<Task<CompiledScript>> compile)
        {
            Lazy<Task<CompiledScript>> lazy;
            lock (gate)
            {
                if (entries.TryGetValue(key, out var node))
                {
                    recency.Remove(node);
                    recency.AddFirst(node);
                    lazy = node.Value.Compilation;
                }
                else
                {
                    lazy = new Lazy<Task<CompiledScript>>(() =>
                    {
                        Interlocked.Increment(ref compilations);
                        return compile();
                    }, LazyThreadSafetyMode.ExecutionAndPublication);
                    entries[key] = recency.AddFirst(new Entry(key, lazy));
                    Trim();
                }
            }
            return lazy.Value;
        }

        /// <summary>
        /// Stores a script compiled elsewhere (the compiler grain compiles while validating).
        /// </summary>
        public void Set(ScriptCacheKey key, CompiledScript compiled)
        {
            var lazy = new Lazy<Task<CompiledScript>>(Task.FromResult(compiled));
            lock (gate)
            {
                if (entries.TryGetValue(key, out var existing))
                    recency.Remove(existing);
                entries[key] = recency.AddFirst(new Entry(key, lazy));
                Trim();
            }
        }

        public bool Remove(ScriptCacheKey key)
        {
            lock (gate)
            {
                if (!entries.Remove(key, out var node))
                    return false;
                recency.Remove(node);
                return true;
            }
        }

        public void Clear()
        {
            lock (gate)
            {
                entries.Clear();
                recency.Clear();
            }
        }

        private void Trim()
        {
            while (entries.Count > capacity)
            {
                var last = recency.Last;
                recency.RemoveLast();
                entries.Remove(last.Value.Key);
            }
        }

        private sealed record Entry(ScriptCacheKey Key, Lazy<Task<CompiledScript>> Compilation);
    }
}
