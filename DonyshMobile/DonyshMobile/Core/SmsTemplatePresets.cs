namespace DonyshMobile.Core;

public sealed record SmsTemplatePreset(string Id, string BankName, string Description, string SampleMessage, IReadOnlyList<SmsTemplateField> Fields);

public static class SmsTemplatePresets
{
    public static IReadOnlyList<SmsTemplatePreset> All { get; } =
    [
        new(
            "blu-withdrawal",
            "بلو بانک — برداشت",
            "پیام برداشت پول بلو با مبلغ ریالی، موجودی، ساعت و تاریخ شمسی",
            "بلو\nبرداشت پول\nمهدی عزیز، 1,400,000ریال از حساب شما پرید.\nموجودی: 7,758,083 ریال\n۱۷:۵۵\n۱۴۰۵.۰۷.۱۹",
            [
                Field(TemplateFieldKey.BankName, "بلو"),
                Field(TemplateFieldKey.TransactionType, "برداشت"),
                Field(TemplateFieldKey.Amount, "1,400,000"),
                Field(TemplateFieldKey.Unit, "ریال"),
                Field(TemplateFieldKey.Time, "17:55"),
                Field(TemplateFieldKey.Date, "1405.07.19")
            ]),
        new(
            "mellat-withdrawal",
            "بانک ملت — برداشت",
            "پیام برداشت بانک ملت شامل حساب، مبلغ، مانده، تاریخ و ساعت",
            "حساب5555555555\nبرداشت50,000,000\nمانده111,111,111\n05/06/15-12:14",
            [
                Field(TemplateFieldKey.BankName, "بانک ملت"),
                Field(TemplateFieldKey.TransactionType, "برداشت"),
                Field(TemplateFieldKey.Amount, "50,000,000"),
                Field(TemplateFieldKey.Unit, "ریال"),
                Field(TemplateFieldKey.Time, "12:14"),
                Field(TemplateFieldKey.Date, "05/06/15")
            ])
    ];

    private static SmsTemplateField Field(TemplateFieldKey key, string value) => new() { Key = key, Value = value };
}
