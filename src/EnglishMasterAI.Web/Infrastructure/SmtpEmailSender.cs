using System.Net;
using System.Net.Mail;
using EnglishMasterAI.Web.Configuration;
using EnglishMasterAI.Web.Data;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Identity.UI.Services;
using Microsoft.Extensions.Options;

namespace EnglishMasterAI.Web.Infrastructure;

public sealed class SmtpEmailSender(IOptions<EmailOptions> options) : IEmailSender<ApplicationUser>
{
    private readonly EmailOptions _options = options.Value;

    public Task SendConfirmationLinkAsync(ApplicationUser user, string email, string confirmationLink) =>
        SendAsync(
            email,
            "Confirm your EnglishMaster AI account",
            $"Confirm your account by opening <a href=\"{WebUtility.HtmlEncode(confirmationLink)}\">this secure link</a>.");

    public Task SendPasswordResetLinkAsync(ApplicationUser user, string email, string resetLink) =>
        SendAsync(
            email,
            "Reset your EnglishMaster AI password",
            $"Reset your password by opening <a href=\"{WebUtility.HtmlEncode(resetLink)}\">this secure link</a>.");

    public Task SendPasswordResetCodeAsync(ApplicationUser user, string email, string resetCode) =>
        SendAsync(
            email,
            "Your EnglishMaster AI password reset code",
            $"Your password reset code is <strong>{WebUtility.HtmlEncode(resetCode)}</strong>.");

    private async Task SendAsync(string recipient, string subject, string htmlBody)
    {
        if (!_options.IsConfigured)
        {
            throw new InvalidOperationException("SMTP email delivery is not configured.");
        }

        using var message = new MailMessage
        {
            From = new MailAddress(_options.FromAddress, _options.FromName),
            Subject = subject,
            Body = htmlBody,
            IsBodyHtml = true
        };
        message.To.Add(new MailAddress(recipient));

        using var client = new SmtpClient(_options.Host, _options.Port)
        {
            EnableSsl = _options.UseSsl
        };
        if (!string.IsNullOrWhiteSpace(_options.Username))
        {
            client.Credentials = new NetworkCredential(_options.Username, _options.Password);
        }

        await client.SendMailAsync(message);
    }
}
