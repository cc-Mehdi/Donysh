namespace DonyshMobile.Core;

public enum TransactionKind { Withdrawal, Deposit }
public enum MoneyUnit { Rial, Toman }
public enum TemplateFieldKey { BankName, TransactionType, Amount, Unit, Time, Date }

public sealed class SmsTemplate
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public Guid SenderId { get; set; }
    public string Name { get; set; } = string.Empty;
    public string Pattern { get; set; } = string.Empty;
    public MoneyUnit Unit { get; set; } = MoneyUnit.Rial;
    public TransactionKind DefaultKind { get; set; } = TransactionKind.Withdrawal;
    public List<TransactionPhrase> Phrases { get; set; } = [];
    public string SampleMessage { get; set; } = string.Empty;
    public List<SmsTemplateField> Fields { get; set; } = [];
}

public sealed class SmsTemplateField
{
    public TemplateFieldKey Key { get; set; }
    public string Value { get; set; } = string.Empty;
}

public sealed class TransactionPhrase
{
    public string Text { get; set; } = string.Empty;
    public TransactionKind Kind { get; set; }
}

public sealed record ParsedSms(
    Guid TemplateId,
    string TemplateName,
    long AmountRial,
    TransactionKind Kind,
    string? DateText,
    string? TimeText);
