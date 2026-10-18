using System.Text.RegularExpressions;

namespace DonyshMobile.Core;

public static partial class SmsPrivacy
{
    [GeneratedRegex(@"(?im)(م\s*و\s*ج\s*و\s*د\s*[یي]|م\s*ا\s*ن\s*د\s*ه|balance)[^\r\n]*", RegexOptions.CultureInvariant, 100)]
    private static partial Regex BalanceLine();

    public static string RemoveBalance(string? body)
    {
        if (string.IsNullOrWhiteSpace(body)) return "متن حساس پیامک روی گوشی نگهداری شد.";
        var clean = body.Replace("\u200e", "").Replace("\u200f", "");
        try { clean = BalanceLine().Replace(clean, string.Empty); }
        catch (RegexMatchTimeoutException) { return "متن حساس پیامک روی گوشی نگهداری شد."; }
        clean = clean.Trim();
        return string.IsNullOrWhiteSpace(clean) ? "متن حساس پیامک روی گوشی نگهداری شد." : clean;
    }
}
