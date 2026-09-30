using System.Collections.Generic;

namespace WildCat.Protocol
{
    /// <summary>
    /// The answer to GetLeaderboard (27). A Me query fills only <see cref="playerRank"/>, an
    /// Around query <see cref="playerRank"/> and <see cref="entries"/>, a Top query
    /// <see cref="count"/> and <see cref="entries"/>; unused numbers are -1.
    /// </summary>
    public class GetLeaderBoardResponse : MessageBase
    {
        public List<LeaderBoardEntry> entries = new List<LeaderBoardEntry>();
        public int playerRank = -1;
        public int count = -1;

        public bool IsPlayerRank { get { return entries.Count == 0 && playerRank != -1; } }
        public bool IsAroundMeResult { get { return entries.Count > 0 && playerRank != -1; } }
        public bool IsTopResult { get { return entries.Count > 0 && playerRank == -1; } }

        public override void Serialize(WireWriter writer)
        {
            writer.WriteInt32(playerRank);
            writer.WriteInt32(count);
            writer.WriteList(entries);
        }

        public override void Deserialize(WireReader reader)
        {
            playerRank = reader.ReadInt32();
            count = reader.ReadInt32();
            entries = reader.ReadList<LeaderBoardEntry>();
        }
    }
}
