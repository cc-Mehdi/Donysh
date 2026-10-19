using System.Globalization;

namespace Donysh.Life.Features.Scheduling;

public static class WeekCalendar
{
    public static readonly IReadOnlyList<int> DurationOptions = [30, 60, 90, 120, 180, 240, 360, 480, 720, 1440];
    public static readonly IReadOnlyList<DayOfWeek> Days =
    [
        DayOfWeek.Saturday,
        DayOfWeek.Sunday,
        DayOfWeek.Monday,
        DayOfWeek.Tuesday,
        DayOfWeek.Wednesday,
        DayOfWeek.Thursday,
        DayOfWeek.Friday
    ];

    public static string DayName(DayOfWeek day) => day switch
    {
        DayOfWeek.Saturday => "شنبه",
        DayOfWeek.Sunday => "یکشنبه",
        DayOfWeek.Monday => "دوشنبه",
        DayOfWeek.Tuesday => "سه‌شنبه",
        DayOfWeek.Wednesday => "چهارشنبه",
        DayOfWeek.Thursday => "پنجشنبه",
        DayOfWeek.Friday => "جمعه",
        _ => "روز"
    };

    public static DateOnly CurrentSaturday()
    {
        var today = Today();
        var distance = ((int)today.DayOfWeek - (int)DayOfWeek.Saturday + 7) % 7;
        return today.AddDays(-distance);
    }

    public static DateOnly Today()
    {
        var utcNow = DateTimeOffset.UtcNow;
        DateTime localNow;
        try
        {
            localNow = TimeZoneInfo.ConvertTime(utcNow, TimeZoneInfo.FindSystemTimeZoneById("Asia/Tehran")).DateTime;
        }
        catch (TimeZoneNotFoundException)
        {
            localNow = utcNow.AddHours(3.5).DateTime;
        }

        return DateOnly.FromDateTime(localNow);
    }

    public static string PersianDate(DateOnly date)
    {
        var calendar = new PersianCalendar();
        var value = date.ToDateTime(TimeOnly.MinValue);
        return ToPersianDigits($"{calendar.GetYear(value):0000}/{calendar.GetMonth(value):00}/{calendar.GetDayOfMonth(value):00}");
    }

    public static int DayIndex(DayOfWeek day) => Days.Select((value, index) => new { value, index })
        .Where(item => item.value == day)
        .Select(item => item.index)
        .DefaultIfEmpty(0)
        .First();

    public static string DurationName(int minutes) => minutes < 60
        ? $"{minutes} دقیقه"
        : minutes % 60 == 0
            ? $"{minutes / 60} ساعت"
            : $"{minutes / 60} ساعت و {minutes % 60} دقیقه";

    private static string ToPersianDigits(string value) => value
        .Replace('0', '۰').Replace('1', '۱').Replace('2', '۲').Replace('3', '۳').Replace('4', '۴')
        .Replace('5', '۵').Replace('6', '۶').Replace('7', '۷').Replace('8', '۸').Replace('9', '۹');
}
