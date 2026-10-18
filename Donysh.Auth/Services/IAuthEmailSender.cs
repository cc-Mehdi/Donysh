namespace Donysh.Auth.Services;

public interface IAuthEmailSender
{
    bool IsConfigured { get; }
    Task SendAsync(string recipient, string subject, string htmlBody, CancellationToken cancellationToken);
}
