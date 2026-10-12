using System.Globalization;
using System.Text;
using System.Text.RegularExpressions;

namespace DonyshMobile.Core;

public static class SmsTemplateParser
{
    private static readonly Regex Placeholder = new(@"\{(amount|date|time|type|ignore)\}", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant, TimeSpan.FromMilliseconds(200));
    private static readonly Regex AnyPlaceholder = new(@"\{[^{}]*\}", RegexOptions.CultureInvariant, TimeSpan.FromMilliseconds(200));
    private static readonly TimeSpan MatchTimeout = TimeSpan.FromMilliseconds(250);

    public static string? Validate(SmsTemplate template)
    {
        if (string.IsNullOrWhiteSpace(template.Name) || template.Name.Length > 80)
            return "نام قالب باید بین ۱ تا ۸۰ نویسه باشد.";
        if (string.IsNullOrWhiteSpace(template.Pattern) || template.Pattern.Length > 2000)
            return "متن قالب باید بین ۱ تا ۲۰۰۰ نویسه باشد.";
        if (!Enum.IsDefined(template.Unit) || !Enum.IsDefined(template.DefaultKind))
            return "واحد پول یا نوع تراکنش معتبر نیست.";

        var matches = Placeholder.Matches(template.Pattern);
        if (matches.Count == 0 || matches.Cast<Match>().Count(m => m.Groups[1].Value.Equals("amount", StringComparison.OrdinalIgnoreCase)) != 1)
            return "هر قالب باید دقیقاً یک {amount} داشته باشد.";
        foreach (var field in new[] { "date", "time", "type" })
            if (matches.Cast<Match>().Count(m => m.Groups[1].Value.Equals(field, StringComparison.OrdinalIgnoreCase)) > 1)
                return $"{{{field}}} فقط یک بار مجاز است.";
        var remainder = Placeholder.Replace(template.Pattern, string.Empty);
        if (AnyPlaceholder.IsMatch(remainder) || remainder.Contains('{') || remainder.Contains('}'))
            return "جای‌نگهدار ناشناخته در قالب وجود دارد.";
        if (remainder.Trim().Length < 2)
            return "برای جلوگیری از تشخیص اشتباه، بخشی از متن ثابت پیام را هم در قالب بگذارید.";

        if (template.Phrases.Count > 30 || template.Phrases.Any(p =>
                string.IsNullOrWhiteSpace(p.Text) || p.Text.Length > 100 || !Enum.IsDefined(p.Kind)))
            return "نگاشت نوع تراکنش نامعتبر است (حداکثر ۳۰ عبارت، هرکدام حداکثر ۱۰۰ نویسه).";
        var normalizedPhrases = template.Phrases.Select(p => NormalizeText(p.Text)).ToList();
        if (normalizedPhrases.Distinct(StringComparer.OrdinalIgnoreCase).Count() != normalizedPhrases.Count)
            return "عبارت تکراری در نگاشت نوع تراکنش وجود دارد.";
        if (matches.Cast<Match>().Any(m => m.Groups[1].Value.Equals("type", StringComparison.OrdinalIgnoreCase)) && template.Phrases.Count == 0)
            return "برای {type} حداقل یک نگاشت عبارت به برداشت یا واریز تعریف کنید.";
        return null;
    }

    public static ParsedSms? TryParse(SmsTemplate template, string body) => CreateMatcher(template)?.Invoke(body);

    public static Func<string, ParsedSms?>? CreateMatcher(SmsTemplate template)
    {
        if (Validate(template) is not null) return null;
        var regex = BuildRegex(template.Pattern);
        return body => Parse(template, regex, body);
    }

    private static ParsedSms? Parse(SmsTemplate template, Regex regex, string body)
    {
        if (body.Length > 10_000) return null;
        try
        {
            var match = regex.Match(NormalizeText(body));
            if (!match.Success) return null;

            var amountText = match.Groups["amount"].Value.Replace(",", string.Empty).Replace("٬", string.Empty).Replace(" ", string.Empty);
            if (!long.TryParse(amountText, NumberStyles.None, CultureInfo.InvariantCulture, out var amount) || amount <= 0)
                return null;
            if (template.Unit == MoneyUnit.Toman)
            {
                if (amount > long.MaxValue / 10) return null;
                amount *= 10;
            }

            var kind = template.DefaultKind;
            if (match.Groups["type"].Success)
            {
                var typeText = NormalizeText(match.Groups["type"].Value).Trim();
                var phrase = template.Phrases
                    .OrderByDescending(p => p.Text.Length)
                    .FirstOrDefault(p => typeText.Contains(NormalizeText(p.Text).Trim(), StringComparison.OrdinalIgnoreCase));
                if (phrase is null) return null;
                kind = phrase.Kind;
            }
            return new ParsedSms(template.Id, template.Name, amount, kind,
                match.Groups["date"].Success ? match.Groups["date"].Value.Trim() : null,
                match.Groups["time"].Success ? match.Groups["time"].Value.Trim() : null);
        }
        catch (RegexMatchTimeoutException)
        {
            return null;
        }
    }

    private static Regex BuildRegex(string pattern)
    {
        var builder = new StringBuilder(@"\A\s*");
        var last = 0;
        foreach (Match placeholder in Placeholder.Matches(pattern))
        {
            AppendLiteral(builder, pattern[last..placeholder.Index]);
            builder.Append(placeholder.Groups[1].Value.ToLowerInvariant() switch
            {
                "amount" => @"(?<amount>[0-9][0-9,٬ ]*)",
                "date" => @"(?<date>(?:[0-9]{2,4}[./-][0-9]{1,2}[./-][0-9]{1,4}|[0-9]{6,8}))",
                "time" => @"(?<time>[0-9]{1,2}[:.][0-9]{2}(?:[:.][0-9]{2})?)",
                "type" => @"(?<type>.+?)",
                _ => @"(?:.*?)"
            });
            last = placeholder.Index + placeholder.Length;
        }
        AppendLiteral(builder, pattern[last..]);
        builder.Append(@"\s*\z");
        return new Regex(builder.ToString(), RegexOptions.Singleline | RegexOptions.IgnoreCase | RegexOptions.CultureInvariant, MatchTimeout);
    }

    private static void AppendLiteral(StringBuilder builder, string literal)
    {
        var normalized = NormalizeText(literal);
        var escaped = Regex.Escape(normalized);
        builder.Append(Regex.Replace(escaped, @"(?:\\ |\s)+", @"\s+"));
    }

    private static string NormalizeText(string text)
    {
        var builder = new StringBuilder(text.Length);
        foreach (var c in text)
            builder.Append(c switch
            {
                >= '۰' and <= '۹' => (char)('0' + c - '۰'),
                >= '٠' and <= '٩' => (char)('0' + c - '٠'),
                'ي' => 'ی',
                'ك' => 'ک',
                '\u200c' => ' ',
                _ => c
            });
        return builder.ToString();
    }
}
