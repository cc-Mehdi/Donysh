using System.Globalization;
using System.Text;
using System.Text.RegularExpressions;

namespace DonyshMobile.Core;

public static partial class SmsTimestampPolicy
{
    // Android's inbox `date` is arrival time; SMS broadcasts use the service-centre
    // timestamp, which is exposed by the inbox as `date_sent`.
    public static DateTimeOffset InboxTimestamp(long receivedAtUnixMs, long sentAtUnixMs)
    {
        var received = DateTimeOffset.FromUnixTimeMilliseconds(receivedAtUnixMs);
        if (sentAtUnixMs <= 0) return received;
        try
        {
            var sent = DateTimeOffset.FromUnixTimeMilliseconds(sentAtUnixMs);
            return sent <= received.AddMinutes(5) && sent >= received.AddHours(-24)
                ? sent : received;
        }
        catch (ArgumentOutOfRangeException) { return received; }
    }

    // Only used to repair an older live/inbox pair that differed by more than the
    // normal five-minute window. The SMS itself must corroborate the live time.
    public static bool BodyConfirmsLocalTime(string body, DateTimeOffset liveTimestamp)
    {
        var normalized = new StringBuilder(body.Length);
        foreach (var value in body.Normalize(NormalizationForm.FormKC))
            normalized.Append(value switch
            {
                >= '۰' and <= '۹' => (char)('0' + value - '۰'),
                >= '٠' and <= '٩' => (char)('0' + value - '٠'),
                _ => value
            });
        var local = liveTimestamp.ToLocalTime();
        foreach (Match match in ClockPattern().Matches(normalized.ToString()))
        {
            if (!int.TryParse(match.Groups[1].Value, NumberStyles.None, CultureInfo.InvariantCulture, out var hour)
                || !int.TryParse(match.Groups[2].Value, NumberStyles.None, CultureInfo.InvariantCulture, out var minute)) continue;
            if (hour == local.Hour && minute == local.Minute) return true;
        }
        return false;
    }

    [GeneratedRegex(@"(?<!\d)([01]?\d|2[0-3]):([0-5]\d)(?!\d)", RegexOptions.CultureInvariant)]
    private static partial Regex ClockPattern();
}
