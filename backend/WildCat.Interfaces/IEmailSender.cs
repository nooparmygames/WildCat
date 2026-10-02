namespace WildCat.Interfaces
{
    /// <summary>
    /// Sends account emails such as activation and password reset codes. The host registers an
    /// SMTP implementation when SMTP is configured and a logging one otherwise.
    /// </summary>
    public interface IEmailSender
    {
        Task SendAsync(string to, string subject, string body);
    }
}
