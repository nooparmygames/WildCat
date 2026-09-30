namespace WildCat.Protocol
{
    /// <summary>
    /// EmailActivation (80): activates an email login with the code sent to it. The session ticket
    /// travels in the payload. The answer (81) is an OptionalResponse&lt;EmptyMessage&gt;.
    /// </summary>
    public class EmailActivationRequest : MessageBase
    {
        /// <summary>The current session's ticket.</summary>
        public string ticket = "";

        /// <summary>The email address to activate.</summary>
        public string email = "";

        /// <summary>The activation code from the email.</summary>
        public string code = "";

        public override void Serialize(WireWriter writer)
        {
            writer.WriteString(ticket);
            writer.WriteString(email);
            writer.WriteString(code);
        }

        public override void Deserialize(WireReader reader)
        {
            ticket = reader.ReadString();
            email = reader.ReadString();
            code = reader.ReadString();
        }
    }
}
