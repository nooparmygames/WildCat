using System;
using System.Collections.Generic;

namespace WildCat.Protocol
{
    /// <summary>
    /// MatchRejoinSyncEvent (127): every match property and every player's properties, sent to a
    /// player who rejoins; not wrapped in an OptionalResponse. Every player bag is written, empty
    /// ones included. (Empty bags used to be skipped while the count still included them, so the
    /// reader ran past the end.)
    /// </summary>
    public class MatchSyncEventData : MessageBase
    {
        public Guid Game;
        public Dictionary<string, byte[]> gameProperties = new Dictionary<string, byte[]>();
        public Dictionary<string, Dictionary<string, byte[]>> playerProperties = new Dictionary<string, Dictionary<string, byte[]>>();

        public override void Serialize(WireWriter writer)
        {
            writer.WriteGuid(Game);
            writer.WriteBytesMap(gameProperties);
            writer.WriteCount(playerProperties == null ? 0 : playerProperties.Count);
            if (playerProperties == null)
                return;
            foreach (var bag in playerProperties)
            {
                writer.WriteString(bag.Key);
                writer.WriteBytesMap(bag.Value);
            }
        }

        public override void Deserialize(WireReader reader)
        {
            Game = reader.ReadGuid();
            gameProperties = reader.ReadBytesMap();
            int playerCount = reader.ReadCount();
            playerProperties = new Dictionary<string, Dictionary<string, byte[]>>(playerCount);
            for (int i = 0; i < playerCount; ++i)
            {
                string player = reader.ReadString();
                playerProperties[player] = reader.ReadBytesMap();
            }
        }
    }
}
