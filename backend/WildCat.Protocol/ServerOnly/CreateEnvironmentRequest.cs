namespace WildCat.Protocol
{
    /// <summary>
    /// CreateEnvironment (44): the old unauthenticated developer sign-up. No handler serves it
    /// since step 4 (the host seeds developers and games from configuration); the payload stays for
    /// server tools.
    /// </summary>
    public class CreateEnvironmentRequest : MessageBase
    {
        public string FirstName = "";
        public string LastName = "";
        public string PasswordHash = "";
        public string Email = "";
        public string PhoneNumber = "";
        public string Address = "";
        public string Description = "";
        public string GameName = "";

        public override void Serialize(WireWriter writer)
        {
            writer.WriteString(FirstName);
            writer.WriteString(LastName);
            writer.WriteString(PasswordHash);
            writer.WriteString(Email);
            writer.WriteString(PhoneNumber);
            writer.WriteString(Address);
            writer.WriteString(Description);
            writer.WriteString(GameName);
        }

        public override void Deserialize(WireReader reader)
        {
            FirstName = reader.ReadString();
            LastName = reader.ReadString();
            PasswordHash = reader.ReadString();
            Email = reader.ReadString();
            PhoneNumber = reader.ReadString();
            Address = reader.ReadString();
            Description = reader.ReadString();
            GameName = reader.ReadString();
        }
    }
}
