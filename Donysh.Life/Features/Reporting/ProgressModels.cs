using Donysh.Life.Features.Activities;
using Donysh.Life.Features.Scheduling;

namespace Donysh.Life.Features.Reporting;

public sealed record ActivityProgressItem(
    Guid ActivityId,
    string Title,
    string ColorHex,
    string IconKey,
    bool IsActive,
    int TargetMinutes,
    int PlannedMinutes,
    int ScheduledActualMinutes,
    int ManualActualMinutes,
    int CompletedCount,
    int PartialCount,
    int MissedCount,
    int MovedCount)
{
    public int ActualMinutes => ScheduledActualMinutes + ManualActualMinutes;
    public int RemainingMinutes => Math.Max(0, TargetMinutes - ActualMinutes);
    public int GoalPercent => TargetMinutes == 0 ? 0 : (int)Math.Round(ActualMinutes * 100m / TargetMinutes);
    public int GoalProgressValue => Math.Clamp(GoalPercent, 0, 100);
    public string ColorClass => ActivityCatalog.ColorClass(ColorHex);
    public string IconGlyph => ActivityCatalog.IconGlyph(IconKey);
    public string TargetLabel => TargetMinutes == 0 ? "بدون هدف" : WeekCalendar.DurationName(TargetMinutes);
    public string PlannedLabel => WeekCalendar.DurationName(PlannedMinutes);
    public string ActualLabel => WeekCalendar.DurationName(ActualMinutes);
}

public sealed record DailyProgressItem(
    DateOnly Date,
    DayOfWeek DayOfWeek,
    int PlannedMinutes,
    int ScheduledActualMinutes,
    int ManualActualMinutes)
{
    public int ActualMinutes => ScheduledActualMinutes + ManualActualMinutes;
    public string DayName => WeekCalendar.DayName(DayOfWeek);
    public string PersianDate => WeekCalendar.PersianDate(Date);
    public string PlannedLabel => WeekCalendar.DurationName(PlannedMinutes);
    public string ActualLabel => WeekCalendar.DurationName(ActualMinutes);
}

public sealed record ProgressReport(
    DateOnly WeekStart,
    DateOnly WeekEnd,
    int TargetMinutes,
    int PlannedMinutes,
    int ScheduledActualMinutes,
    int ManualActualMinutes,
    int CompletedCount,
    int PartialCount,
    int MissedCount,
    int MovedCount,
    int UnrecordedCount,
    IReadOnlyList<ActivityProgressItem> Activities,
    IReadOnlyList<DailyProgressItem> Days)
{
    public int ActualMinutes => ScheduledActualMinutes + ManualActualMinutes;
    public int GoalPercent => TargetMinutes == 0 ? 0 : (int)Math.Round(ActualMinutes * 100m / TargetMinutes);
    public int PlanPercent => PlannedMinutes == 0 ? 0 : (int)Math.Round(ActualMinutes * 100m / PlannedMinutes);
    public int GoalProgressValue => Math.Clamp(GoalPercent, 0, 100);
    public int PlanProgressValue => Math.Clamp(PlanPercent, 0, 100);
    public bool HasData => TargetMinutes > 0 || PlannedMinutes > 0 || ActualMinutes > 0;
    public bool IsCurrentWeek => WeekStart == WeekCalendar.CurrentSaturday();
    public string PersianRange => $"{WeekCalendar.PersianDate(WeekStart)} تا {WeekCalendar.PersianDate(WeekEnd)}";
    public string TargetLabel => WeekCalendar.DurationName(TargetMinutes);
    public string PlannedLabel => WeekCalendar.DurationName(PlannedMinutes);
    public string ActualLabel => WeekCalendar.DurationName(ActualMinutes);
    public string Insight => ActualMinutes == 0
        ? "هنوز زمان واقعی برای این هفته ثبت نشده است؛ ثبت نتیجهٔ برنامه‌ها یا یک فعالیت دستی، گزارش را کامل می‌کند."
        : TargetMinutes > 0 && ActualMinutes >= TargetMinutes
            ? "زمان واقعی این هفته به هدف تعیین‌شده رسیده است. حالا پایداری این روند از افزایش عدد مهم‌تر است."
            : PlannedMinutes > 0 && ActualMinutes >= PlannedMinutes
                ? "زمان واقعی به اندازهٔ کل برنامهٔ این هفته بوده است؛ بخشی از آن می‌تواند خارج از برنامه انجام شده باشد."
                : "فاصلهٔ برنامه و عملکرد هنوز وجود دارد؛ جزئیات هر فعالیت نشان می‌دهد بهتر است کدام برنامه واقع‌بینانه‌تر شود.";
}
