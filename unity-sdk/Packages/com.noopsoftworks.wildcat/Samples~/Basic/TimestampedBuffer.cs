using System;
using System.Collections.Generic;

namespace WildCat.Samples.Basic
{
    /// <summary>
    /// Values with the local time they arrived, oldest first, read back interpolated at a moment
    /// slightly in the past so remote movement looks smooth despite uneven arrival times.
    /// </summary>
    public sealed class TimestampedBuffer<T>
    {
        private readonly Func<T, T, float, T> interpolate;
        private readonly Queue<(float Time, T Value)> buffer = new Queue<(float, T)>();
        private (float Time, T Value)? past;

        /// <param name="interpolate">Blends two values; the float runs from 0 (first) to 1 (second).</param>
        public TimestampedBuffer(Func<T, T, float, T> interpolate)
        {
            this.interpolate = interpolate ?? throw new ArgumentNullException(nameof(interpolate));
        }

        public int Count
        {
            get { return buffer.Count; }
        }

        /// <summary>Adds a value; values must come in time order.</summary>
        public void Add(float time, T value)
        {
            if (past == null)
                past = (time, value);
            buffer.Enqueue((time, value));
        }

        public void Clear()
        {
            buffer.Clear();
            past = null;
        }

        /// <summary>
        /// The value at <paramref name="time"/>, interpolated between the values around it. Values
        /// older than that are dropped. False when nothing has arrived yet.
        /// </summary>
        public bool TryGetValue(float time, out T value)
        {
            while (buffer.Count > 1 && time > buffer.Peek().Time)
                past = buffer.Dequeue();

            if (buffer.Count == 0 || past == null)
            {
                value = past.HasValue ? past.Value.Value : default(T);
                return past.HasValue;
            }

            var next = buffer.Peek();
            float span = next.Time - past.Value.Time;
            // Two values with the same time (or the first value alone) would divide by zero.
            float t = span <= 0f ? 1f : Math.Max(0f, Math.Min(1f, (time - past.Value.Time) / span));
            value = interpolate(past.Value.Value, next.Value, t);
            return true;
        }
    }
}
