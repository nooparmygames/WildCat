using System;

namespace WildCat.Protocol
{
    /// <summary>
    /// Thrown by <see cref="WireCodec"/> when bytes are not a valid frame or payload: cut short,
    /// followed by stray bytes, a negative or impossible length, or values the payload type cannot
    /// hold. Decoding never throws anything else for bad input.
    /// </summary>
    public sealed class WireFormatException : FormatException
    {
        public WireFormatException(string message) : base(message)
        {
        }

        public WireFormatException(string message, Exception innerException) : base(message, innerException)
        {
        }
    }
}
