using System;

namespace WildCat.Protocol
{
    /// <summary>
    /// The header flags byte of a frame. Each set flag adds its field to the frame header (see
    /// <see cref="WireCodec"/>). A response from the server sets only <see cref="HasRequestID"/>.
    /// </summary>
    [Flags]
    public enum WildCatMsgHeader : byte
    {
        normal = 0,
        HasGameTarget = 1,
        HasPlayerTarget = 2,
        HasAuthData = 4,
        // 8 is reserved.
        HasRequestID = 16,
        // The last three are defined but not used by either side.
        stored = 32,
        buffered = 64,
        hasTime = 128,
    }
}
