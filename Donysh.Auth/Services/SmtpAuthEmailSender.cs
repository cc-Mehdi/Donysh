using System.Net;
using System.Net.Mail;
using Donysh.Auth.Configuration;
using Microsoft.Extensions.Options;

namespace Donysh.Auth.Services;

public sealed class SmtpAuthEmailSender(IOptions<EmailOptions> options) : IAuthEmailSender
{
    private readonly EmailOptions _options = options.Value;

    public bool IsConfigured => _options.IsConfigured;

    public async Task SendAsync(string recipient, string subject, string htmlBody, CancellationToken cancellationToken)
    {
        if (!IsConfigured) throw new InvalidOperationException("Email delivery is not configured.");

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
            EnableSsl = _options.UseSsl,
            Timeout = checked(_options.TimeoutSeconds * 1000),
            DeliveryMethod = SmtpDeliveryMethod.Network,
            UseDefaultCredentials = false,
            Credentials = string.IsNullOrWhiteSpace(_options.UserName)
                ? null
                : new NetworkCredential(_options.UserName, _options.Password)
        };
        cancellationToken.ThrowIfCancellationRequested();
        await client.SendMailAsync(message, cancellationToken);
    }
}
