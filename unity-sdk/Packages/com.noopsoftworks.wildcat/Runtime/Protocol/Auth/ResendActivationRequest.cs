namespace WildCat.Protocol
{
    /// <summary>
    /// ResendActivation (86): sends a new activation code to an email login that is not activated
    /// yet; the new code replaces the old one. Needs an initialized session (ticket in the frame
    /// header). The answer (87) is an OptionalResponse&lt;EmptyMessage&gt; that succeeds whether or
    /// not a code was sent, so it does not tell which addresses are registered or activated.
    /// </summary>
    public class ResendActivationRequest : MessageBase
    {
        public string email = "";

        public override void Serialize(WireWriter writer)
        {
            writer.WriteString(email);
        }

        public override void Deserialize(WireReader reader)
        {
            email = reader.ReadString();
        }
    }
}
