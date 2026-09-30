namespace WildCat.Protocol
{
    /// <summary>
    /// ResetPasswordWithCode (163): sets a new password for an email login with the code that
    /// RecoverPassword (84) sent. A reset also activates the address. Needs an initialized session
    /// (ticket in the frame header). The answer (164) is an OptionalResponse&lt;EmptyMessage&gt;;
    /// a wrong or expired code and an unknown address get the same error (code 1).
    /// </summary>
    public class ResetPasswordWithCodeRequest : MessageBase
    {
        public string email = "";
        public string code = "";

        /// <summary>The new password in plain text; send it only over wss://.</summary>
        public string newPassword = "";

        public override void Serialize(WireWriter writer)
        {
            writer.WriteString(email);
            writer.WriteString(code);
            writer.WriteString(newPassword);
        }

        public override void Deserialize(WireReader reader)
        {
            email = reader.ReadString();
            code = reader.ReadString();
            newPassword = reader.ReadString();
        }
    }
}
