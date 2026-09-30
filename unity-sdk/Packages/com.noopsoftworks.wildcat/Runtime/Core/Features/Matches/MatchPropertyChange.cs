using System;
using WildCat.Protocol;

namespace WildCat
{
    /// <summary>A match property changed.</summary>
    public sealed class MatchPropertyChange
    {
        public MatchPropertyChange(Guid matchId, string playerId, string key, byte[] value)
        {
            MatchId = matchId;
            PlayerId = playerId;
            Key = key;
            Value = value ?? new byte[0];
        }

        public Guid MatchId { get; }

        /// <summary>The player whose property changed, or null for a global (match) property.</summary>
        public string PlayerId { get; }

        public bool IsGlobal
        {
            get { return string.IsNullOrEmpty(PlayerId); }
        }

        public string Key { get; }

        public byte[] Value { get; }

        /// <summary>Reads <see cref="Value"/> as a payload that was set with the MessageBase overloads.</summary>
        public T GetValue<T>() where T : MessageBase, new()
        {
            return WireCodec.DecodePayload<T>(Value);
        }
    }
}
