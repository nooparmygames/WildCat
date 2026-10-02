using System;
using WildCat.Protocol;

namespace WildCat
{
    /// <summary>What a script event returned.</summary>
    public sealed class ScriptResult
    {
        public ScriptResult(bool hasValue, ParamType type, object value)
        {
            HasValue = hasValue;
            Type = type;
            Value = hasValue ? value : null;
        }

        /// <summary>False when the script returned nothing (or null).</summary>
        public bool HasValue { get; }

        /// <summary>The value's wire type; meaningless without a value.</summary>
        public ParamType Type { get; }

        /// <summary>
        /// The value: int, short, long, bool, byte, byte[], float, double or string, or null.
        /// </summary>
        public object Value { get; }

        /// <summary>The value as <typeparamref name="T"/>; throws InvalidCastException when it is another type or missing.</summary>
        public T GetValue<T>()
        {
            if (!HasValue)
                throw new InvalidCastException("The script event returned no value.");
            if (!(Value is T))
                throw new InvalidCastException("The script event returned a " + Value.GetType().Name + ", not a " + typeof(T).Name + ".");
            return (T)Value;
        }
    }
}
