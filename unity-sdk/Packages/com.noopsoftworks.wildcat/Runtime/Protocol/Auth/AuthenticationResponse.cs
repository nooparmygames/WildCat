namespace WildCat.Protocol
{
    /// <summary>The answer to Authenticate (77) when the login worked.</summary>
    public class AuthenticationResponse : MessageBase
    {
        public string name = "";
        public string userId = "";
        public string ticket = "";
        public bool isNewlyCreated;

        public override void Serialize(WireWriter writer)
        {
            writer.WriteString(userId);
            writer.WriteString(name);
            writer.WriteString(ticket);
            writer.WriteBool(isNewlyCreated);
        }

        public override void Deserialize(WireReader reader)
        {
            userId = reader.ReadString();
            name = reader.ReadString();
            ticket = reader.ReadString();
            isNewlyCreated = reader.ReadBool();
        }
    }
}
