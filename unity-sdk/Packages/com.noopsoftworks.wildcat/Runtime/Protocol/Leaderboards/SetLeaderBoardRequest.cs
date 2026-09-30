namespace WildCat.Protocol
{
    /// <summary>SetLeaderboard (28). The gateway refuses it from clients; scores come from server script events.</summary>
    public class SetLeaderBoardRequest : MessageBase
    {
        public string leaderBoardName = "";
        public int score;

        public override void Serialize(WireWriter writer)
        {
            writer.WriteString(leaderBoardName);
            writer.WriteInt32(score);
        }

        public override void Deserialize(WireReader reader)
        {
            leaderBoardName = reader.ReadString();
            score = reader.ReadInt32();
        }
    }
}
