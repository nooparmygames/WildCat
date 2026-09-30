using System.Collections.Generic;

namespace WildCat.Protocol
{
    /// <summary>
    /// Authenticate (76): logs a player in on a session. The session ticket travels in the
    /// payload, not the frame header. The answer (77) is an OptionalResponse&lt;AuthenticationResponse&gt;.
    /// </summary>
    public class AuthenticationRequest : MessageBase
    {
        /// <summary>The current session's ticket.</summary>
        public string ticket = "";

        /// <summary>The type of login.</summary>
        public LoginType LoginType;

        /// <summary>The device id, user name or email address, depending on <see cref="LoginType"/>.</summary>
        public string Id = "";

        /// <summary>
        /// The password in plain text (the field keeps its old name for now); send it only over
        /// wss://. The server stores a salted hash.
        /// </summary>
        public string PasswordHash = "";

        /// <summary>
        /// Create the account when it does not exist yet; only for device and user name logins.
        /// </summary>
        public bool AutoCreate;

        /// <summary>Extra data for custom login types; the server does not use it.</summary>
        public Dictionary<string, string> data = new Dictionary<string, string>();

        public override void Serialize(WireWriter writer)
        {
            writer.WriteString(ticket);
            writer.WriteByte((byte)LoginType);
            writer.WriteString(Id);
            writer.WriteString(PasswordHash);
            writer.WriteBool(AutoCreate);
            writer.WriteStringMap(data);
        }

        public override void Deserialize(WireReader reader)
        {
            ticket = reader.ReadString();
            LoginType = (LoginType)reader.ReadByte();
            Id = reader.ReadString();
            PasswordHash = reader.ReadString();
            AutoCreate = reader.ReadBool();
            data = reader.ReadStringMap();
        }
    }
}
