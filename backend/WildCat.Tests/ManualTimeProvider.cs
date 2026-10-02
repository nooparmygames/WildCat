namespace WildCat.Tests
{
    /// <summary>
    /// A clock that only moves when a test calls <see cref="Advance"/>.
    /// </summary>
    internal sealed class ManualTimeProvider : TimeProvider
    {
        private long ticks;

        public override long TimestampFrequency => TimeSpan.TicksPerSecond;

        public override long GetTimestamp()
        {
            return Interlocked.Read(ref ticks);
        }

        public void Advance(TimeSpan by)
        {
            Interlocked.Add(ref ticks, by.Ticks);
        }
    }
}
