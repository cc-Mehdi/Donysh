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
            if (result.StartsWith("0098", StringComparison.Ordinal) && result.Length > 4)
            {
                result = result[4..];
            }
            else if (result.StartsWith("98", StringComparison.Ordinal) && result.Length > 10)
            {
                result = result[2..];
            }

            if (result.StartsWith('0') && result.Length > 1)
            {
                result = result[1..];
            }
        }

        return result;
    }
}
