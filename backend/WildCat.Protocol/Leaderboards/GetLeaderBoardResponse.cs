using System.Collections.Generic;

namespace WildCat.Protocol
{
    /// <summary>
    /// The answer to GetLeaderboard (27). <see cref="type"/> repeats the query type. A Me query
    /// fills only <see cref="playerRank"/>, an Around query <see cref="playerRank"/> and
    /// <see cref="entries"/>, a Top query <see cref="count"/> (the number of entries on the board)
    /// and <see cref="entries"/>; unused numbers are -1, and a rank is -1 for a player who is not
    /// on the board. (Before step 6b the answer did not carry the type and the SDK guessed it from
    /// which fields were filled, which failed for an empty board or a player without a score.)
    /// </summary>
    public class GetLeaderBoardResponse : MessageBase
    {
        public GetLeaderBoardRequest.RequestType type;
        public List<LeaderBoardEntry> entries = new List<LeaderBoardEntry>();
        public int playerRank = -1;
        public int count = -1;

        public bool IsPlayerRank { get { return type == GetLeaderBoardRequest.RequestType.Me; } }
        public bool IsAroundMeResult { get { return type == GetLeaderBoardRequest.RequestType.Around; } }
        public bool IsTopResult { get { return type == GetLeaderBoardRequest.RequestType.Top; } }

        public override void Serialize(WireWriter writer)
        {
            writer.WriteByte((byte)type);
            writer.WriteInt32(playerRank);
            writer.WriteInt32(count);
            writer.WriteList(entries);
        }

        public override void Deserialize(WireReader reader)
        {
            type = (GetLeaderBoardRequest.RequestType)reader.ReadByte();
            playerRank = reader.ReadInt32();
            count = reader.ReadInt32();
            entries = reader.ReadList<LeaderBoardEntry>();
        }
    }
}
