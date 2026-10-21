using System.Net.Mail;

namespace Donysh.Auth.Configuration;

public sealed class EmailOptions
{
    public const string SectionName = "Email";

    public string Host { get; set; } = string.Empty;
    public int Port { get; set; } = 587;
    public bool UseSsl { get; set; } = true;
    public int TimeoutSeconds { get; set; } = 20;
    public string UserName { get; set; } = string.Empty;
    public string Password { get; set; } = string.Empty;
    public string FromAddress { get; set; } = string.Empty;
    public string FromName { get; set; } = "Donysh";

    public bool IsConfigured => !string.IsNullOrWhiteSpace(Host)
        && Port is > 0 and <= 65535
        && TimeoutSeconds is >= 5 and <= 120
        && !string.IsNullOrWhiteSpace(FromAddress)
        && MailAddress.TryCreate(FromAddress, out _)
        && (string.IsNullOrWhiteSpace(UserName) == string.IsNullOrWhiteSpace(Password));
}
