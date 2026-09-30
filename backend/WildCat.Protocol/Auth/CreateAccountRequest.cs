using System.Collections.Generic;

namespace WildCat.Protocol
{
    /// <summary>
    /// CreateNewAccount (78): creates an account without logging in. The session ticket travels in
    /// the payload. The answer (79) is an OptionalResponse&lt;StringMessage&gt; with the account id,
    /// or error code 2 when the device, user name or email is taken.
    /// </summary>
    public class CreateAccountRequest : MessageBase
    {
        /// <summary>The current session's ticket.</summary>
        public string ticket = "";

        /// <summary>The kind of account to create.</summary>
        public LoginType LoginType;

        /// <summary>The device id, user name or email address, depending on <see cref="LoginType"/>.</summary>
        public string Id = "";

        /// <summary>
        /// The password in plain text if the login type has one (the field keeps its old name for
        /// now); send it only over wss://.
        /// </summary>
        public string PasswordHash = "";

        /// <summary>Extra data for custom account types; the server does not use it.</summary>
        public Dictionary<string, string> data = new Dictionary<string, string>();

        public override void Serialize(WireWriter writer)
        {
            writer.WriteString(ticket);
            writer.WriteByte((byte)LoginType);
            writer.WriteString(Id);
            writer.WriteString(PasswordHash);
            writer.WriteStringMap(data);
        }

        public override void Deserialize(WireReader reader)
        {
            ticket = reader.ReadString();
            LoginType = (LoginType)reader.ReadByte();
            Id = reader.ReadString();
            PasswordHash = reader.ReadString();
            data = reader.ReadStringMap();
        }
    }
}
