using System.Globalization;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using WildCat.Admin.Resources;
using WildCat.Interfaces;

namespace WildCat.Admin;

/// <summary>
/// Builds the activation and password reset emails from EmailTexts.resx and sends them through the
/// host's <see cref="IEmailSender"/>. The sender is resolved in a fresh scope per email, so any
/// lifetime works; without a registered sender the email is logged instead.
/// </summary>
internal sealed class AccountEmails(
    IServiceScopeFactory scopeFactory,
    IOptionsMonitor<WildCatAdminOptions> options,
    ILogger<AccountEmails> logger)
{
    public Task SendActivationAsync(string email, string? firstName, string code)
    {
        string body = Compose(firstName, EmailTexts.ActivationBody, code, PanelLink("activate", email, code));
        return SendAsync(email, EmailTexts.ActivationSubject, body);
    }

    public Task SendPasswordResetAsync(string email, string? firstName, string code)
    {
        string body = Compose(firstName, EmailTexts.PasswordResetBody, code, PanelLink("reset-password", email, code));
        return SendAsync(email, EmailTexts.PasswordResetSubject, body);
    }

    /// <summary>
    /// Runs <paramref name="send"/> without making the request wait for it. The endpoints that
    /// must answer the same way whether or not an account exists use this, so the time an SMTP
    /// server takes does not tell a caller which emails are registered. Failures are logged.
    /// </summary>
    public void SendInBackground(Func<AccountEmails, Task> send)
    {
        _ = Task.Run(async () =>
        {
            try
            {
                await send(this);
            }
            catch (Exception ex)
            {
                logger.LogError(ex, "Sending an account email failed");
            }
        });
    }

    private async Task SendAsync(string to, string subject, string body)
    {
        await using var scope = scopeFactory.CreateAsyncScope();
        var sender = scope.ServiceProvider.GetService<IEmailSender>();
        if (sender == null)
        {
            logger.LogWarning(
                "No IEmailSender is registered, so this email to {To} was not sent. Subject: {Subject}. Body: {Body}",
                to, subject, body);
            return;
        }
        await sender.SendAsync(to, subject, body);
    }

    private static string Compose(string? firstName, string bodyFormat, string code, string? link)
    {
        string greeting = string.IsNullOrWhiteSpace(firstName)
            ? EmailTexts.GreetingWithoutName
            : string.Format(CultureInfo.CurrentCulture, EmailTexts.GreetingWithName, firstName.Trim());
        string body = greeting + "\n\n" + string.Format(CultureInfo.CurrentCulture, bodyFormat, code);
        if (link != null)
            body += "\n\n" + string.Format(CultureInfo.CurrentCulture, EmailTexts.LinkLine, link);
        return body + "\n";
    }

    private string? PanelLink(string page, string email, string code)
    {
        string? baseUrl = options.CurrentValue.PanelBaseUrl;
        if (string.IsNullOrWhiteSpace(baseUrl))
            return null;
        return $"{baseUrl.Trim().TrimEnd('/')}{PanelHosting.RequestPath}/{page}?email={Uri.EscapeDataString(email)}&code={Uri.EscapeDataString(code)}";
    }
}
