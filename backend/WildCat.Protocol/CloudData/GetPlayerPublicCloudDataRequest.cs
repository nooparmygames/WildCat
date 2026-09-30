namespace WildCat.Protocol
{
    /// <summary>GetPlayerPublicCloudData (70): another player's public value for a key.</summary>
    public class GetPlayerPublicCloudDataRequest : MessageBase
    {
        public string player = "";
        public string key = "";

        public override void Serialize(WireWriter writer)
        {
            writer.WriteString(player);
            writer.WriteString(key);
        }

        public override void Deserialize(WireReader reader)
        {
            player = reader.ReadString();
            key = reader.ReadString();
        }
    }
}
