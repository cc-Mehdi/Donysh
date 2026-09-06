using System.Globalization;
using System.Text.RegularExpressions;

namespace Donysh.Sms;

public sealed record BankWithdrawal(string Bank, string? Account, decimal AmountToman, DateTime LocalDateTime);

public static class BankSmsParser
{
    private const string Money = @"(?<amount>(?:[0-9]{1,3}(?:,[0-9]{3})+|[0-9]+))";
    private static MatchCollection Matches(string text, string pattern) => Regex.Matches(text, pattern,
        RegexOptions.CultureInvariant | RegexOptions.Multiline, TimeSpan.FromMilliseconds(100));

    public static string Normalize(string text)
    {
        return string.Concat(text.Select(c => c is >= '۰' and <= '۹' ? (char)('0' + c - '۰')
            : c is >= '٠' and <= '٩' ? (char)('0' + c - '٠') : c == '٬' ? ',' : c))
            .Replace("\r", "").Replace("\u200e", "").Replace("\u200f", "").Trim();
    }

    public static BankWithdrawal? Parse(string? text)
    {
        if (string.IsNullOrWhiteSpace(text) || text.Length > 2048) return null;
        text = Normalize(text);
        if (text.Contains("رمز", StringComparison.Ordinal) || text.Contains("واریز", StringComparison.Ordinal)) return null;
        try
        {
            var mellat = Matches(text, @"^حساب\s*(?<account>[0-9]{4,24})\s*$");
            var isBlu = Matches(text, @"^بلو\s*$").Count == 1 && Matches(text, @"^برداشت پول\s*$").Count == 1;
            if ((mellat.Count == 1) == isBlu) return null;
            var amounts = Matches(text, isBlu
                ? @"^[^\n]*?عزیز،\s*" + Money + @"\s+ریال از حساب شما پرید\.\s*$"
                : @"^برداشت\s*" + Money + @"\s*$");
            if (amounts.Count != 1) return null;
            if (!decimal.TryParse(amounts[0].Groups["amount"].Value.Replace(",", ""), NumberStyles.None,
                    CultureInfo.InvariantCulture, out var rial) || rial < 10 || rial > 9_999_999_999_990m || rial % 10 != 0) return null;
            var dates = Matches(text, isBlu
                ? @"^(?<hour>[0-9]{2}):(?<minute>[0-9]{2})\s*\n(?<year>[0-9]{4})\.(?<month>[0-9]{2})\.(?<day>[0-9]{2})\s*$"
                : @"^(?<year>[0-9]{2}|[0-9]{4})/(?<month>[0-9]{2})/(?<day>[0-9]{2})-(?<hour>[0-9]{2}):(?<minute>[0-9]{2})\s*$");
            if (dates.Count != 1) return null;
            var date = dates[0];
            int Part(string name) => int.Parse(date.Groups[name].Value, CultureInfo.InvariantCulture);
            var year = Part("year");
            if (date.Groups["year"].Length == 2) year += 1400;
            if (year is < 1400 or > 1499) return null;
            var local = new PersianCalendar().ToDateTime(year, Part("month"), Part("day"), Part("hour"), Part("minute"), 0, 0);
            return new(isBlu ? "blu" : "mellat", isBlu ? null : mellat[0].Groups["account"].Value, rial / 10, local);
        }
        catch (ArgumentException) { return null; }
        catch (RegexMatchTimeoutException) { return null; }
    }
}
