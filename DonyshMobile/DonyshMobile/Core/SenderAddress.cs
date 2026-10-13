using System.Text;

namespace DonyshMobile.Core;

public static class SenderAddress
{
    public static string Normalize(string? value)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return string.Empty;
        }

        var builder = new StringBuilder(value.Length);
        foreach (var character in value.Trim())
        {
            var normalized = character switch
            {
                >= '\u06F0' and <= '\u06F9' => (char)('0' + character - '\u06F0'),
                >= '\u0660' and <= '\u0669' => (char)('0' + character - '\u0660'),
                _ => character
            };

            if (char.IsLetterOrDigit(normalized))
            {
                builder.Append(char.ToUpperInvariant(normalized));
            }
        }

        var result = builder.ToString();
        if (result.All(char.IsDigit))
        {
            // Mobile numbers may be shown with +98, 0098, 098, 09, or no prefix.
            // Restrict the rewrite to a ten-digit mobile subscriber number.
            if (result.StartsWith("00989", StringComparison.Ordinal) && result.Length == 14)
            {
                result = result[4..];
            }
            else if (result.StartsWith("0989", StringComparison.Ordinal) && result.Length == 13)
            {
                result = result[1..];
            }

            if (result.StartsWith("989", StringComparison.Ordinal) && result.Length == 12)
            {
                result = result[2..];
            }
            else if (result.StartsWith("09", StringComparison.Ordinal) && result.Length == 11)
            {
                result = result[1..];
            }
        }

        return result;
    }
}
