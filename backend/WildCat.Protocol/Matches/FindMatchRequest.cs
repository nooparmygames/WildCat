namespace WildCat.Protocol
{
    /// <summary>FindMatch (15): the game mode, the player's skill and how long to wait in seconds.</summary>
    public class FindMatchRequest : MessageBase
    {
        public string matchType = "";
        public uint skill;
        public uint waitTime;

        public FindMatchRequest()
        {
        }

        public FindMatchRequest(string matchType, uint skill, uint timeout)
        {
            this.matchType = matchType;
            this.skill = skill;
            this.waitTime = timeout;
        }

        public override void Serialize(WireWriter writer)
        {
            writer.WriteString(matchType);
            writer.WriteUInt32(skill);
            writer.WriteUInt32(waitTime);
        }

        public override void Deserialize(WireReader reader)
        {
            matchType = reader.ReadString();
            skill = reader.ReadUInt32();
            waitTime = reader.ReadUInt32();
        }
    }
}
