using System;

namespace WildCat.Protocol
{
    /// <summary>The answer to GetPlayerPendingMatches (132): matches the player can rejoin.</summary>
    public class PendingMatchesCollectionData : MessageBase
    {
        public class PendingMatchInfo : MessageBase
        {
            public Guid matchId;
            public DateTime startTime;
            public string[] players = new string[0];

            public override void Serialize(WireWriter writer)
            {
                writer.WriteGuid(matchId);
                writer.WriteDateTime(startTime);
                writer.WriteStringArray(players);
            }

            public override void Deserialize(WireReader reader)
            {
                matchId = reader.ReadGuid();
                startTime = reader.ReadDateTime();
                players = reader.ReadStringArray();
            }
        }

        public PendingMatchInfo[] matches = new PendingMatchInfo[0];

        public override void Serialize(WireWriter writer)
        {
            writer.WriteList(matches);
        }

        public override void Deserialize(WireReader reader)
        {
            matches = reader.ReadList<PendingMatchInfo>().ToArray();
        }
    }
}
