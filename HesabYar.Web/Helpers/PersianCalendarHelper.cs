using System.Globalization;

namespace HesabYar.Web.Helpers;

public readonly record struct PersianYearMonth(int Year, int Month)
{
    public PersianYearMonth AddMonths(int value)
    {
        var index = Year * 12 + (Month - 1) + value;
        return new PersianYearMonth(index / 12, index % 12 + 1);
    }
}

public static class PersianCalendarHelper
{
    private static readonly PersianCalendar Calendar = new();

    private static readonly string[] MonthNames =
    [
        "فروردین", "اردیبهشت", "خرداد", "تیر", "مرداد", "شهریور",
        "مهر", "آبان", "آذر", "دی", "بهمن", "اسفند"
    ];

    public static PersianYearMonth GetYearMonth(DateOnly date)
    {
        var value = date.ToDateTime(TimeOnly.MinValue);
        return new PersianYearMonth(Calendar.GetYear(value), Calendar.GetMonth(value));
    }

    public static int GetDayOfMonth(DateOnly date)
        => Calendar.GetDayOfMonth(date.ToDateTime(TimeOnly.MinValue));

    public static DateOnly StartOfMonth(PersianYearMonth period)
        => FromPersian(period.Year, period.Month, 1);

    public static DateOnly EndOfMonthExclusive(PersianYearMonth period)
        => StartOfMonth(period.AddMonths(1));

    public static DateOnly StartOfYear(int year)
        => FromPersian(year, 1, 1);

    public static DateOnly FromPersian(int year, int month, int day)
        => DateOnly.FromDateTime(Calendar.ToDateTime(year, month, day, 0, 0, 0, 0));

    public static string ToInput(DateOnly date)
    {
        var period = GetYearMonth(date);
        var day = GetDayOfMonth(date);
        return $"{period.Year:0000}/{period.Month:00}/{day:00}";
    }

    public static string ToInput(DateOnly? date)
        => date.HasValue ? ToInput(date.Value) : string.Empty;

    public static bool TryParseInput(string? input, out DateOnly date)
    {
        date = default;
        if (string.IsNullOrWhiteSpace(input))
        {
            return false;
        }

        var normalized = InputNormalization.ToLatinDigits(input)
            .Trim()
            .Replace('-', '/')
            .Replace('.', '/');

        var parts = normalized.Split('/', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
        if (parts.Length != 3 ||
            !int.TryParse(parts[0], out var year) ||
            !int.TryParse(parts[1], out var month) ||
            !int.TryParse(parts[2], out var day))
        {
            return false;
        }

        try
        {
            // User-facing dates are Persian. Gregorian ISO remains accepted for
            // old links/bookmarks and internal navigation compatibility.
            if (year is >= 1200 and <= 1600)
            {
                date = FromPersian(year, month, day);
                return true;
            }

            if (year is >= 1900 and <= 2200 &&
                DateOnly.TryParseExact(
                    $"{year:0000}/{month:00}/{day:00}",
                    "yyyy/MM/dd",
                    CultureInfo.InvariantCulture,
                    DateTimeStyles.None,
                    out var gregorianDate))
            {
                date = gregorianDate;
                return true;
            }
        }
        catch (ArgumentOutOfRangeException)
        {
            return false;
        }

        return false;
    }

    public static string MonthName(int month)
        => month is >= 1 and <= 12 ? MonthNames[month - 1] : month.ToString(CultureInfo.InvariantCulture);

    public static string Title(PersianYearMonth period) => $"{MonthName(period.Month)} {period.Year}";
}
