using HesabYar.Web.Domain;

namespace HesabYar.Web.Helpers;

public static class RecurringObligationHelper
{
    public static int MonthsBetween(PersianYearMonth start, PersianYearMonth period)
        => (period.Year - start.Year) * 12 + (period.Month - start.Month);

    public static bool IsScheduledForPeriod(RecurringObligation obligation, PersianYearMonth period, bool requireActive = true)
    {
        if (requireActive && !obligation.IsActive)
        {
            return false;
        }

        var offset = MonthsBetween(new PersianYearMonth(obligation.StartYear, obligation.StartMonth), period);
        if (offset < 0)
        {
            return false;
        }

        return !obligation.DurationMonths.HasValue || offset < obligation.DurationMonths.Value;
    }

    public static DateOnly GetDueDate(RecurringObligation obligation, PersianYearMonth period)
    {
        var start = PersianCalendarHelper.StartOfMonth(period);
        var end = PersianCalendarHelper.EndOfMonthExclusive(period);
        var daysInMonth = end.DayNumber - start.DayNumber;
        var dueDay = Math.Clamp(obligation.DueDay, 1, daysInMonth);
        return start.AddDays(dueDay - 1);
    }

    public static int? GetInstallmentNumber(RecurringObligation obligation, PersianYearMonth period)
    {
        if (obligation.Type != RecurringObligationType.Installment)
        {
            return null;
        }

        var offset = MonthsBetween(new PersianYearMonth(obligation.StartYear, obligation.StartMonth), period);
        return offset >= 0 ? offset + 1 : null;
    }

    public static int? GetRemainingInstallments(RecurringObligation obligation, PersianYearMonth period)
    {
        if (obligation.Type != RecurringObligationType.Installment || !obligation.DurationMonths.HasValue)
        {
            return null;
        }

        var offset = MonthsBetween(new PersianYearMonth(obligation.StartYear, obligation.StartMonth), period);
        if (offset < 0)
        {
            return obligation.DurationMonths.Value;
        }

        return Math.Max(0, obligation.DurationMonths.Value - offset - 1);
    }
}
