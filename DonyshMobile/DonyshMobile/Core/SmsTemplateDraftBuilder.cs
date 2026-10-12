using System.Text.RegularExpressions;

namespace DonyshMobile.Core;

public sealed record SmsFieldCandidate(string Value, int Start, int Length, string Display);

public sealed record SmsDraftAnalysis(
    IReadOnlyList<SmsFieldCandidate> Amounts,
    SmsFieldCandidate? Date,
    SmsFieldCandidate? Time,
    string? SuggestedTypePhrase,
    TransactionKind SuggestedKind);

public static partial class SmsTemplateDraftBuilder
{
    private static readonly (string Phrase, TransactionKind Kind)[] KnownPhrases =
    [
        ("برداشت پول", TransactionKind.Withdrawal),
        ("از حساب شما پرید", TransactionKind.Withdrawal),
        ("از حسابت پرید", TransactionKind.Withdrawal),
        ("از حساب شما برداشت شد", TransactionKind.Withdrawal),
        ("از حسابت برداشت شد", TransactionKind.Withdrawal),
        ("برداشت", TransactionKind.Withdrawal),
        ("خرید", TransactionKind.Withdrawal),
        ("پرداخت", TransactionKind.Withdrawal),
        ("کسر شد", TransactionKind.Withdrawal),
        ("واریز به حساب", TransactionKind.Deposit),
        ("به حسابت واریز شد", TransactionKind.Deposit),
        ("واریز پول", TransactionKind.Deposit),
        ("واریز", TransactionKind.Deposit)
    ];

    public static SmsDraftAnalysis Analyze(string body)
    {
        body ??= string.Empty;
        var occupied = new List<(int Start, int End)>();
        var date = ToCandidate(DateRegex().Match(body), "تاریخ");
        var time = ToCandidate(TimeRegex().Match(body), "ساعت");
        if (date is not null) occupied.Add((date.Start, date.Start + date.Length));
        if (time is not null) occupied.Add((time.Start, time.Start + time.Length));

        var amounts = NumberRegex().Matches(body).Cast<Match>()
            .Where(match => !occupied.Any(range => match.Index < range.End && match.Index + match.Length > range.Start))
            .Select(match => (Match: match, Context: GetContext(body, match), Score: ScoreAmount(body, match)))
            .OrderByDescending(item => item.Score)
            .ThenBy(item => item.Match.Index)
            .Select(item => new SmsFieldCandidate(
                item.Match.Value, item.Match.Index, item.Match.Length,
                $"{ToLatinDigits(item.Match.Value).Trim()}  ·  {item.Context}"))
            .ToList();

        var normalized = NormalizeLetters(body);
        var suggestion = KnownPhrases.FirstOrDefault(item =>
            normalized.Contains(NormalizeLetters(item.Phrase), StringComparison.OrdinalIgnoreCase));
        return new SmsDraftAnalysis(amounts, date, time,
            suggestion.Phrase, suggestion == default ? TransactionKind.Withdrawal : suggestion.Kind);
    }

    public static SmsTemplate Build(
        Guid id, Guid senderId, string name, string body, SmsFieldCandidate amount,
        MoneyUnit unit, TransactionKind kind, string? typePhrase)
    {
        var analysis = Analyze(body);
        var replacements = new List<(int Start, int Length, string Value)>
        {
            (amount.Start, amount.Length, "{amount}")
        };
        if (analysis.Date is not null) replacements.Add((analysis.Date.Start, analysis.Date.Length, "{date}"));
        if (analysis.Time is not null) replacements.Add((analysis.Time.Start, analysis.Time.Length, "{time}"));

        var phrase = typePhrase?.Trim() ?? string.Empty;
        if (phrase.Length > 0)
        {
            var phraseIndex = NormalizeLetters(body).IndexOf(NormalizeLetters(phrase), StringComparison.OrdinalIgnoreCase);
            if (phraseIndex >= 0) replacements.Add((phraseIndex, phrase.Length, "{type}"));
        }

        var pattern = ApplyReplacements(body, replacements);
        pattern = NumberRegex().Replace(pattern, "{ignore}");
        return new SmsTemplate
        {
            Id = id,
            SenderId = senderId,
            Name = name.Trim(),
            Pattern = pattern.Trim(),
            Unit = unit,
            DefaultKind = kind,
            Phrases = phrase.Length == 0 ? [] : [new TransactionPhrase { Text = phrase, Kind = kind }]
        };
    }

    private static string ApplyReplacements(string source, IEnumerable<(int Start, int Length, string Value)> replacements)
    {
        foreach (var replacement in replacements.OrderByDescending(x => x.Start))
            source = source.Remove(replacement.Start, replacement.Length).Insert(replacement.Start, replacement.Value);
        return source;
    }

    private static SmsFieldCandidate? ToCandidate(Match match, string label) =>
        match.Success ? new SmsFieldCandidate(match.Value, match.Index, match.Length, $"{label}: {ToLatinDigits(match.Value)}") : null;

    private static string ToLatinDigits(string value) => string.Concat(value.Select(character => character switch
    {
        >= '۰' and <= '۹' => (char)('0' + character - '۰'),
        >= '٠' and <= '٩' => (char)('0' + character - '٠'),
        _ => character
    }));

    private static string NormalizeLetters(string value) => value.Replace('ي', 'ی').Replace('ك', 'ک').Replace('\u200c', ' ');

    private static int ScoreAmount(string body, Match match)
    {
        var before = NormalizeLetters(body[Math.Max(0, match.Index - 24)..match.Index]);
        var afterEnd = Math.Min(body.Length, match.Index + match.Length + 24);
        var after = NormalizeLetters(body[(match.Index + match.Length)..afterEnd]);
        var context = before + after;
        var score = 0;
        if (context.Contains("برداشت") || context.Contains("واریز") || context.Contains("پرید") || context.Contains("پرداخت") || context.Contains("خرید")) score += 30;
        if (after.Contains("ریال") || after.Contains("تومان")) score += 15;
        if (match.Value.Contains(',') || match.Value.Contains('٬')) score += 5;
        if (before.Contains("مانده") || before.Contains("موجودی")) score -= 40;
        if (before.TrimEnd().EndsWith("حساب", StringComparison.OrdinalIgnoreCase) && !match.Value.Contains(',') && !match.Value.Contains('٬')) score -= 50;
        return score;
    }

    private static string GetContext(string body, Match match)
    {
        var lineStart = body.LastIndexOf('\n', Math.Max(0, match.Index - 1));
        var lineEnd = body.IndexOf('\n', match.Index + match.Length);
        lineStart = lineStart < 0 ? 0 : lineStart + 1;
        lineEnd = lineEnd < 0 ? body.Length : lineEnd;
        var line = body[lineStart..lineEnd].Trim();
        if (line.Length > 40) line = line[..40] + "…";
        return line;
    }

    [GeneratedRegex(@"(?<![0-9۰-۹٠-٩])[0-9۰-۹٠-٩][0-9۰-۹٠-٩,٬ ]{1,}(?![0-9۰-۹٠-٩])", RegexOptions.CultureInvariant, 200)]
    private static partial Regex NumberRegex();

    [GeneratedRegex(@"(?<![0-9۰-۹٠-٩])(?:[0-9۰-۹٠-٩]{2,4}[./-][0-9۰-۹٠-٩]{1,2}[./-][0-9۰-۹٠-٩]{1,4}|[0-9۰-۹٠-٩]{6,8})(?![0-9۰-۹٠-٩])", RegexOptions.CultureInvariant, 200)]
    private static partial Regex DateRegex();

    [GeneratedRegex(@"(?<![0-9۰-۹٠-٩])[0-9۰-۹٠-٩]{1,2}[:.][0-9۰-۹٠-٩]{2}(?::[0-9۰-۹٠-٩]{2})?(?![0-9۰-۹٠-٩])", RegexOptions.CultureInvariant, 200)]
    private static partial Regex TimeRegex();
}
