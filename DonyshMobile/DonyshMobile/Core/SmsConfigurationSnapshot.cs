namespace DonyshMobile.Core;

/// <summary>Only user setup is backed up; message bodies, receipts and delivery state stay on the phone.</summary>
public sealed class SmsConfigurationSnapshot
{
    public List<MonitoredSender> Senders { get; set; } = [];
    public List<SmsTemplate> Templates { get; set; } = [];
}

public sealed record SmsConfigurationBackup(long Revision, SmsConfigurationSnapshot Snapshot);
