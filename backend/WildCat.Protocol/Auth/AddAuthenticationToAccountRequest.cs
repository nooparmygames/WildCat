using System.Collections.Generic;

namespace WildCat.Protocol
{
    /// <summary>
    /// AddAuthMechanismToAccount (82): adds a device, user name or email login to the account of
    /// the logged in player (session ticket in the frame header). The answer (83) is an
    /// OptionalResponse&lt;EmptyMessage&gt;, with error code 1 when the credential is taken.
    /// </summary>
    public class AddAuthenticationToAccountRequest : MessageBase
    {
        /// <summary>The kind of login to add.</summary>
        public LoginType LoginType;

        /// <summary>The device id, user name or email address, depending on <see cref="LoginType"/>.</summary>
        public string Id = "";

        /// <summary>
        /// The password in plain text if the login type has one (the field keeps its old name for
        /// now); send it only over wss://.
        /// </summary>
        public string PasswordHash = "";

        /// <summary>Extra data for custom login types; the server does not use it.</summary>
        public Dictionary<string, string> data = new Dictionary<string, string>();

        public override void Serialize(WireWriter writer)
        {
            writer.WriteByte((byte)LoginType);
            writer.WriteString(Id);
            writer.WriteString(PasswordHash);
            writer.WriteStringMap(data);
        }

        public override void Deserialize(WireReader reader)
        {
            LoginType = (LoginType)reader.ReadByte();
            Id = reader.ReadString();
            PasswordHash = reader.ReadString();
            data = reader.ReadStringMap(WireLimits.MaxLoginDataEntries);
        }
    }
}
