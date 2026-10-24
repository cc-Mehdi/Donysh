using Donysh.Life.Domain;

namespace Donysh.Life.Features.Scheduling;

public static class ScheduleRecurrence
{
    public static int DayMask(DayOfWeek day) => 1 << (int)day;

    public static bool OccursOn(RecurringSchedulePattern pattern, DateOnly date)
    {
        if (pattern.StartsOn is not DateOnly start)
            return pattern.RepeatFrequency == "weekly" && (EffectiveMask(pattern) & DayMask(date.DayOfWeek)) != 0;
        if (date < start || (pattern.RepeatUntil is DateOnly until && date > until)) return false;
        if (!MatchesFrequency(pattern, start, date)) return false;
        return pattern.RepeatCount is not int limit || OccurrenceNumber(pattern, start, date) <= limit;
    }

    public static int ShiftDays(int mask, DayOfWeek from, DayOfWeek to)
    {
        var shift = ((int)to - (int)from + 7) % 7;
        var result = 0;
        for (var day = 0; day < 7; day++)
            if ((mask & (1 << day)) != 0) result |= 1 << ((day + shift) % 7);
        return result;
    }

    private static bool MatchesFrequency(RecurringSchedulePattern pattern, DateOnly start, DateOnly date)
    {
        var interval = Math.Max(1, pattern.RepeatInterval);
        return pattern.RepeatFrequency switch
        {
            "none" => date == start,
            "daily" => date.DayNumber - start.DayNumber >= 0 && (date.DayNumber - start.DayNumber) % interval == 0,
            "weekly" => (EffectiveMask(pattern) & DayMask(date.DayOfWeek)) != 0
                && ((SaturdayOf(date).DayNumber - SaturdayOf(start).DayNumber) / 7) % interval == 0,
            "monthly" => date.Day == start.Day && MonthsBetween(start, date) % interval == 0,
            "yearly" => date.Month == start.Month && date.Day == start.Day && (date.Year - start.Year) % interval == 0,
            _ => false
        };
    }

    private static int OccurrenceNumber(RecurringSchedulePattern pattern, DateOnly start, DateOnly date)
    {
        var interval = Math.Max(1, pattern.RepeatInterval);
        if (pattern.RepeatFrequency == "none") return 1;
        if (pattern.RepeatFrequency == "daily") return (date.DayNumber - start.DayNumber) / interval + 1;
        if (pattern.RepeatFrequency == "weekly")
        {
            var mask = EffectiveMask(pattern);
            var first = SaturdayOf(start);
            var weekNumber = (SaturdayOf(date).DayNumber - first.DayNumber) / 7 / interval;
            var selectedPerWeek = Enumerable.Range(0, 7).Count(offset => (mask & DayMask(first.AddDays(offset).DayOfWeek)) != 0);
            var firstWeek = Enumerable.Range(start.DayNumber - first.DayNumber, 7 - (start.DayNumber - first.DayNumber))
                .Count(offset => (mask & DayMask(first.AddDays(offset).DayOfWeek)) != 0);
            var currentWeek = SaturdayOf(date);
            var throughDate = Enumerable.Range(0, date.DayNumber - currentWeek.DayNumber + 1)
                .Count(offset => (mask & DayMask(currentWeek.AddDays(offset).DayOfWeek)) != 0);
            return weekNumber == 0 ? throughDate - (selectedPerWeek - firstWeek) : firstWeek + (weekNumber - 1) * selectedPerWeek + throughDate;
        }
        var count = 0;
        if (pattern.RepeatFrequency == "monthly")
        {
            for (var month = 0; month <= MonthsBetween(start, date); month += interval)
            {
                var target = start.AddMonths(month);
                if (target.Day == start.Day && target <= date) count++;
            }
        }
        else if (pattern.RepeatFrequency == "yearly")
        {
            for (var year = 0; year <= date.Year - start.Year; year += interval)
            {
                var target = start.AddYears(year);
                if (target.Month == start.Month && target.Day == start.Day && target <= date) count++;
            }
        }
        return count;
    }

    private static int EffectiveMask(RecurringSchedulePattern pattern) => pattern.RepeatDaysMask == 0
        ? DayMask(pattern.DayOfWeek) : pattern.RepeatDaysMask;
    private static int MonthsBetween(DateOnly start, DateOnly date) => (date.Year - start.Year) * 12 + date.Month - start.Month;
    private static DateOnly SaturdayOf(DateOnly date) => date.AddDays(-(((int)date.DayOfWeek - (int)DayOfWeek.Saturday + 7) % 7));
}
