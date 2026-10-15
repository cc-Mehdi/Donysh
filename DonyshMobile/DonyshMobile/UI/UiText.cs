using System.Globalization;

namespace DonyshMobile.UI;

public static class UiText
{
    public static string Digits(object value) => string.Concat(Convert.ToString(value, CultureInfo.InvariantCulture)!
        .Select(x => x is >= '0' and <= '9' ? (char)('۰' + x - '0') : x));

    public static string Date(DateTimeOffset value)
    {
        var local = value.ToLocalTime();
        var calendar = new PersianCalendar();
        return Digits($"{calendar.GetYear(local.DateTime):0000}/{calendar.GetMonth(local.DateTime):00}/{calendar.GetDayOfMonth(local.DateTime):00} · {local:HH:mm}");
    }
}
