namespace WildCat.Protocol
{
    /// <summary>
    /// RecoverPassword (84): asks for a password reset code to be sent to an email login. Needs an
    /// initialized session (ticket in the frame header), not a logged in player. The answer (85)
    /// is an OptionalResponse&lt;EmptyMessage&gt; that succeeds whether or not the address belongs to
    /// an account, so it cannot be used to find out which addresses are registered. Send the code
    /// back with ResetPasswordWithCode (163).
    /// </summary>
    public class RecoverPasswordRequest : MessageBase
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
