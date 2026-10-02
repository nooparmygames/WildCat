using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using WildCat.Interfaces;

namespace WildCat.Grains
{
    public static class EmailServiceCollectionExtensions
    {
        /// <summary>
        /// Binds <see cref="EmailOptions"/> from <paramref name="section"/> (normally
        /// "WildCat:Email") and registers the <see cref="IEmailSender"/> the grains use:
        /// <see cref="SmtpEmailSender"/> when SmtpHost and FromAddress are set, otherwise
        /// <see cref="LoggingEmailSender"/>. The choice is made once, at startup.
        /// </summary>
        public static IServiceCollection AddWildCatEmail(this IServiceCollection services, IConfiguration section)
        {
            services.Configure<EmailOptions>(section);
            var options = section.Get<EmailOptions>() ?? new EmailOptions();
            if (options.IsSmtpConfigured)
                services.AddSingleton<IEmailSender, SmtpEmailSender>();
            else
                services.AddSingleton<IEmailSender, LoggingEmailSender>();
            return services;
        }
    }
}
