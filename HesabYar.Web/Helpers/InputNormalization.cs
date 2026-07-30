using System.Globalization;
using System.Text;

namespace HesabYar.Web.Helpers;

public static class InputNormalization
{
    public static string ToLatinDigits(string? value)
    {
        if (string.IsNullOrEmpty(value))
        {
            return string.Empty;
        }

        var result = new StringBuilder(value.Length);
        foreach (var ch in value)
        {
            result.Append(ch switch
            {
                >= '\u06F0' and <= '\u06F9' => (char)('0' + ch - '\u06F0'),
                >= '\u0660' and <= '\u0669' => (char)('0' + ch - '\u0660'),
                _ => ch
            });
        }

        return result.ToString();
    }

    public static bool TryParseMoney(string? value, out decimal amount)
    {
        amount = 0;
        if (string.IsNullOrWhiteSpace(value))
        {
            return false;
        }

        var normalized = ToLatinDigits(value)
            .Replace(",", string.Empty, StringComparison.Ordinal)
            .Replace("٬", string.Empty, StringComparison.Ordinal)
            .Replace(" ", string.Empty, StringComparison.Ordinal)
            .Replace("\u00A0", string.Empty, StringComparison.Ordinal)
            .Replace("\u200C", string.Empty, StringComparison.Ordinal)
            .Replace("٫", ".", StringComparison.Ordinal)
            .Trim();

        return decimal.TryParse(
            normalized,
            NumberStyles.AllowLeadingSign | NumberStyles.AllowDecimalPoint,
            CultureInfo.InvariantCulture,
            out amount);
    }
}
