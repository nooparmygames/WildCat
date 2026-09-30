using System;

namespace WildCat.Protocol
{
    /// <summary>One entry of a GetLeaderboard answer (27).</summary>
    public class LeaderBoardEntry : MessageBase
    {
        public string playerId = "";
        public string userName = "";
        public int score;
        public DateTime entryTime;

        /// <summary>Filled in by server scripts; not sent on the wire.</summary>
        [NonSerialized]
        public int Rank;

        public override void Serialize(WireWriter writer)
        {
            writer.WriteString(playerId);
            writer.WriteString(userName);
            writer.WriteInt32(score);
            writer.WriteDateTime(entryTime);
        }

        public override void Deserialize(WireReader reader)
        {
            playerId = reader.ReadString();
            userName = reader.ReadString();
            score = reader.ReadInt32();
            entryTime = reader.ReadDateTime();
        }
    }
}
