using System;
using System.Collections.Generic;

/// <summary>
/// This is a buffer of values which are sorted by timestamps 
/// </summary>
/// <typeparam name="T"></typeparam>
public class TimestampedBuffer<T>
{
    /// <summary>
    /// The interpolation logic between two entries in the buffer
    /// </summary>
    private Func<T, T, double, T> interpolater;

    /// <summary>
    /// The queue of buffered values
    /// </summary>
    private Queue<TimestampedValue<T>> buffer;

    /// <summary>
    /// The value that we passed its time
    /// </summary>
    private TimestampedValue<T> pastTimestampedValue;

    /// <summary>
    /// The public accessor to the buffer
    /// </summary>
    public Queue<TimestampedValue<T>> items { get { return buffer; } }

    /// <summary>
    /// Gets the first item in the buffer and also returns its time in the float value passed by reference
    /// </summary>
    /// <param name="t">The method will put the time in here</param>
    /// <returns></returns>
    public T GetFirst(out float t)
    {
        var b = buffer.Dequeue();
        t = b.Timestamp;
        return b.Value;
    }

    /// <summary>
    /// Returns number of items in the queue
    /// </summary>
    /// <returns></returns>
    public int Count()
    {
        return buffer.Count;
    }

    /// <summary>
    /// Clears the buffer
    /// </summary>
    public void Clear()
    {
        pastTimestampedValue = null;
        buffer.Clear();
    }

    /// <summary>
    /// Constructs an instance of the class
    /// </summary>
    /// <param name="interpolation">The interpolation function which you want to be used</param>
    public TimestampedBuffer(Func<T, T, double, T> interpolation)
    {
        interpolater = interpolation;
        buffer = new Queue<TimestampedValue<T>>();
        pastTimestampedValue = null;
    }

    /// <summary>
    /// Adds an element to the buffer
    /// We put the value at the end of the queue assuming you'll not pass values older than those already in the buffer
    /// </summary>
    /// <param name="timestamp">The timestamp of the value</param>
    /// <param name="value">The value to be stored itself</param>
    public void Add(float timestamp, T value)
    {
        var timestampedValue = new TimestampedValue<T>(timestamp, value);
        if (pastTimestampedValue == null) pastTimestampedValue = timestampedValue;
        buffer.Enqueue(timestampedValue);
    }



    /// <summary>
    /// Gets a value at a specific point in time
    /// </summary>
    /// <param name="time">The point in time we want to retrieve a value for</param>
    /// <param name="hasValue">We fill this in by the fact that we could find a value or not</param>
    /// <returns>The interpolated value if exists</returns>
    /// <remarks>
    /// All values in times before the passed point of time will be thrown away and we don't assume you will need them
    /// </remarks>
    public T GetValue(double time, out bool hasValue)
    {
        while (buffer.Count > 1 && time > buffer.Peek().Timestamp)
        {
            pastTimestampedValue = buffer.Dequeue();
        }

        if (buffer.Count > 0)
        {
            hasValue = true;
            return interpolater(
                pastTimestampedValue.Value, buffer.Peek().Value,
                (time - pastTimestampedValue.Timestamp) / (buffer.Peek().Timestamp - pastTimestampedValue.Timestamp)
            );
        }
        else
        {

            hasValue = false;
            return (pastTimestampedValue != null) ? pastTimestampedValue.Value : default(T);
        }
    }

    /// <summary>
    /// A value which has a timestamp attached
    /// </summary>
    /// <typeparam name="TValue">Type of the value we want to store with the timestamp</typeparam>
    public class TimestampedValue<TValue>
    {
        /// <summary>
        /// Timestamp of the value
        /// </summary>
        public float Timestamp { get; private set; }

        /// <summary>
        /// The actual value we are holding
        /// </summary>
        public TValue Value { get; private set; }

        /// <summary>
        /// Constructs an instance of the object
        /// </summary>
        /// <param name="timestamp"></param>
        /// <param name="value"></param>
        public TimestampedValue(float timestamp, TValue value)
        {
            this.Timestamp = timestamp;
            this.Value = value;
        }
    }
}