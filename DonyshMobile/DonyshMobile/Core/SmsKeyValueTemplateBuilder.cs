using System.Text;
using System.Text.RegularExpressions;

namespace DonyshMobile.Core;

public sealed record TemplateBuildResult(SmsTemplate? Template, string? Error);

public static partial class SmsKeyValueTemplateBuilder
{
    public static TemplateBuildResult Build(
        Guid id, Guid senderId, string name, string message, IReadOnlyList<SmsTemplateField> fields)
    {
        message = message?.Trim() ?? string.Empty;
        if (message.Length is 0 or > 2048)
            return Failed("متن کامل پیام را وارد کنید؛ حداکثر ۲۰۴۸ نویسه مجاز است.");
        if (fields.Count is 0 or > 12)
            return Failed("حداقل یک ردیف کلید و مقدار لازم است.");
        if (fields.Any(x => x is null || !Enum.IsDefined(x.Key) || string.IsNullOrWhiteSpace(x.Value) || x.Value.Length > 200))
            return Failed("مقدار همه ردیف‌ها را وارد کنید.");
        if (fields.GroupBy(x => x.Key).Any(group => group.Count() > 1))
            return Failed("هر کلید فقط یک بار قابل استفاده است.");

        var amount = fields.SingleOrDefault(x => x.Key == TemplateFieldKey.Amount);
        if (amount is null) return Failed("یک ردیف با کلید «مبلغ» اضافه کنید.");

        var unitText = fields.SingleOrDefault(x => x.Key == TemplateFieldKey.Unit)?.Value.Trim() ?? "ریال";
        if (unitText is not ("ریال" or "تومان"))
            return Failed("مقدار واحد باید «ریال» یا «تومان» باشد.");
        var unit = unitText == "تومان" ? MoneyUnit.Toman : MoneyUnit.Rial;

        var typeField = fields.SingleOrDefault(x => x.Key == TemplateFieldKey.TransactionType);
        var kind = InferKind(typeField?.Value);
        var replacements = new List<(int Start, int Length, string Token)>();
        foreach (var field in fields.Where(x => x.Key is TemplateFieldKey.Amount or TemplateFieldKey.Date or TemplateFieldKey.Time or TemplateFieldKey.TransactionType))
        {
            var index = FindNormalized(message, field.Value.Trim());
            if (index < 0)
                return Failed($"مقدار «{field.Value.Trim()}» برای کلید «{GetLabel(field.Key)}» داخل متن پیام پیدا نشد.");
            var token = field.Key switch
            {
                TemplateFieldKey.Amount => "{amount}",
                TemplateFieldKey.Date => "{date}",
                TemplateFieldKey.Time => "{time}",
                _ => "{type}"
            };
            replacements.Add((index, field.Value.Trim().Length, token));
        }
        if (replacements.OrderBy(x => x.Start).Zip(replacements.OrderBy(x => x.Start).Skip(1),
                (left, right) => left.Start + left.Length > right.Start).Any(overlaps => overlaps))
            return Failed("مقدار دو کلید روی یک بخش از پیام قرار گرفته است.");

        var pattern = message;
        foreach (var replacement in replacements.OrderByDescending(x => x.Start))
            pattern = pattern.Remove(replacement.Start, replacement.Length).Insert(replacement.Start, replacement.Token);
        pattern = RemainingNumberRegex().Replace(pattern, "{ignore}");

        var bank = fields.SingleOrDefault(x => x.Key == TemplateFieldKey.BankName)?.Value.Trim();
        var generatedName = string.IsNullOrWhiteSpace(name)
            ? string.Join(" - ", new[] { bank, kind == TransactionKind.Deposit ? "واریز" : "برداشت" }.Where(x => !string.IsNullOrWhiteSpace(x)))
            : name.Trim();
        var template = new SmsTemplate
        {
            Id = id,
            SenderId = senderId,
            Name = generatedName,
            Pattern = pattern,
            Unit = unit,
            DefaultKind = kind,
            Phrases = typeField is null ? [] : [new TransactionPhrase { Text = typeField.Value.Trim(), Kind = kind }],
            SampleMessage = message,
            Fields = fields.Select(x => new SmsTemplateField { Key = x.Key, Value = x.Value.Trim() }).ToList()
        };
        var validationError = SmsTemplateParser.Validate(template);
        return validationError is null ? new TemplateBuildResult(template, null) : Failed(validationError);
    }

    public static string GetLabel(TemplateFieldKey key) => key switch
    {
        TemplateFieldKey.BankName => "نام بانک",
        TemplateFieldKey.TransactionType => "نوع تراکنش",
        TemplateFieldKey.Amount => "مبلغ",
        TemplateFieldKey.Unit => "واحد",
        TemplateFieldKey.Time => "ساعت",
        TemplateFieldKey.Date => "تاریخ",
        _ => string.Empty
    };

    public static TemplateFieldKey? FromLabel(string? label) => label switch
    {
        "نام بانک" => TemplateFieldKey.BankName,
        "نوع تراکنش" => TemplateFieldKey.TransactionType,
        "مبلغ" => TemplateFieldKey.Amount,
        "واحد" => TemplateFieldKey.Unit,
        "ساعت" => TemplateFieldKey.Time,
        "تاریخ" => TemplateFieldKey.Date,
        _ => null
    };

    private static TransactionKind InferKind(string? value)
    {
        var normalized = Normalize(value ?? string.Empty);
        return normalized.Contains("واریز") || normalized.Contains("دریافت") || normalized.Contains("افزوده")
            ? TransactionKind.Deposit
            : TransactionKind.Withdrawal;
    }

    private static int FindNormalized(string message, string value) =>
        Normalize(message).IndexOf(Normalize(value), StringComparison.OrdinalIgnoreCase);

    private static string Normalize(string value)
    {
        var builder = new StringBuilder(value.Length);
        foreach (var character in value)
            builder.Append(character switch
            {
                >= '۰' and <= '۹' => (char)('0' + character - '۰'),
                >= '٠' and <= '٩' => (char)('0' + character - '٠'),
                'ي' => 'ی',
                'ك' => 'ک',
                '\u200c' => ' ',
                _ => character
            });
        return builder.ToString();
    }

    private static TemplateBuildResult Failed(string error) => new(null, error);

    [GeneratedRegex(@"(?<![0-9۰-۹٠-٩])[0-9۰-۹٠-٩][0-9۰-۹٠-٩,٬ ]{1,}(?![0-9۰-۹٠-٩])", RegexOptions.CultureInvariant, 200)]
    private static partial Regex RemainingNumberRegex();
}
