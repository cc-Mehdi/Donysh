using System.Globalization;

namespace HesabYar.Web.Helpers;

public static class Formatters
{
    private static readonly CultureInfo Fa = CultureInfo.GetCultureInfo("fa-IR");

    public static string Money(decimal amount) => $"{amount:N0} تومان";

    public static string PersianDate(DateOnly date)
    {
        var period = PersianCalendarHelper.GetYearMonth(date);
        var day = PersianCalendarHelper.GetDayOfMonth(date);
        return $"{period.Year:0000}/{period.Month:00}/{day:00}";
    }

    public static string PersianMonthTitle(DateOnly date)
        => PersianCalendarHelper.Title(PersianCalendarHelper.GetYearMonth(date));

    public static string Percent(decimal value) => value.ToString("0.#", Fa) + "٪";
}
