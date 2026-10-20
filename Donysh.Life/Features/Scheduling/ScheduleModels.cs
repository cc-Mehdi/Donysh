using System.ComponentModel.DataAnnotations;
using Donysh.Life.Features.Activities;

namespace Donysh.Life.Features.Scheduling;

public sealed class SchedulePatternForm
{
    [Required(ErrorMessage = "یک فعالیت انتخاب کنید.")]
    public Guid ActivityId { get; set; }

    [EnumDataType(typeof(DayOfWeek), ErrorMessage = "روز انتخاب‌شده معتبر نیست.")]
    public DayOfWeek DayOfWeek { get; set; } = DayOfWeek.Saturday;

    [Required(ErrorMessage = "ساعت شروع را وارد کنید.")]
    [RegularExpression("^(?:[01]\\d|2[0-3]):(?:00|30)$", ErrorMessage = "ساعت شروع باید روی نیم‌ساعت باشد.")]
    public string StartTime { get; set; } = "08:00";

    [Range(30, 1440, ErrorMessage = "مدت فعالیت باید بین ۳۰ دقیقه تا ۲۴ ساعت باشد.")]
    public int DurationMinutes { get; set; } = 60;

    [StringLength(160, ErrorMessage = "یادداشت نمی‌تواند بیشتر از ۱۶۰ نویسه باشد.")]
    public string? Note { get; set; }
}

public sealed record ScheduleActivityOption(Guid Id, string Title, string ColorHex, string IconKey)
{
    public string ColorClass => ActivityCatalog.ColorClass(ColorHex);
    public string IconGlyph => ActivityCatalog.IconGlyph(IconKey);
}

public sealed record ScheduleBlockItem(
    Guid Id,
    Guid ActivityId,
    string ActivityTitle,
    string ColorHex,
    string IconKey,
    DayOfWeek DayOfWeek,
    int StartMinute,
    int DurationMinutes,
    string? Note,
    bool IsActive,
    DateOnly OriginalDate,
    DateOnly ScheduledDate,
    bool IsOccurrenceOverride,
    Domain.OccurrenceOutcomeStatus? OutcomeStatus,
    int? ActualMinutes,
    DateOnly? MovedToDate,
    string? OutcomeNote)
{
    public int EndMinute => StartMinute + DurationMinutes;
    public string StartLabel => FormatMinute(StartMinute);
    public string EndLabel => FormatMinute(EndMinute);
    public string DurationLabel => DurationMinutes < 60
        ? $"{DurationMinutes} دقیقه"
        : DurationMinutes % 60 == 0
            ? $"{DurationMinutes / 60} ساعت"
            : $"{DurationMinutes / 60} ساعت و {DurationMinutes % 60} دقیقه";
    public string ColorClass => ActivityCatalog.ColorClass(ColorHex);
    public string IconGlyph => ActivityCatalog.IconGlyph(IconKey);
    public string? OutcomeLabel => OutcomeStatus switch
    {
        Domain.OccurrenceOutcomeStatus.Completed => "انجام‌شده",
        Domain.OccurrenceOutcomeStatus.Missed => "انجام‌نشده",
        Domain.OccurrenceOutcomeStatus.Partial => "نیمه‌تمام",
        Domain.OccurrenceOutcomeStatus.Moved => "انتقال‌یافته",
        _ => null
    };
    public string OutcomeClass => OutcomeStatus switch
    {
        Domain.OccurrenceOutcomeStatus.Completed => "outcome-completed",
        Domain.OccurrenceOutcomeStatus.Missed => "outcome-missed",
        Domain.OccurrenceOutcomeStatus.Partial => "outcome-partial",
        Domain.OccurrenceOutcomeStatus.Moved => "outcome-moved",
        _ => string.Empty
    };

    private static string FormatMinute(int minute) => $"{minute / 60:00}:{minute % 60:00}";
}

public sealed class ScheduleInteractionForm
{
    [Required] public Guid PatternId { get; set; }
    [Required] public DateOnly OriginalDate { get; set; }
    [Required] public DateOnly TargetDate { get; set; }
    [Range(0, 1410)] public int StartMinute { get; set; }
    [Range(30, 1440)] public int DurationMinutes { get; set; }
    [RegularExpression("^(occurrence|series)$")] public string Scope { get; set; } = "occurrence";
}

public sealed class OccurrenceRecordForm
{
    [Required] public Guid PatternId { get; set; }
    [Required] public DateOnly OriginalDate { get; set; }
    public Domain.OccurrenceOutcomeStatus Status { get; set; }
    [Range(0, 1440, ErrorMessage = "زمان واقعی باید بین صفر تا ۱۴۴۰ دقیقه باشد.")]
    public int? ActualMinutes { get; set; }
    public DateOnly? MovedToDate { get; set; }
    [StringLength(160, ErrorMessage = "یادداشت نمی‌تواند بیشتر از ۱۶۰ نویسه باشد.")]
    public string? Note { get; set; }
}

public sealed record ActivityPlanSummary(Guid ActivityId, string Title, string ColorHex, int GoalMinutes, int PlannedMinutes)
{
    public string ColorClass => ActivityCatalog.ColorClass(ColorHex);
    public decimal GoalHours => GoalMinutes / 60m;
    public decimal PlannedHours => PlannedMinutes / 60m;
    public int CoveragePercent => GoalMinutes == 0 ? 0 : Math.Min(100, (int)Math.Round(PlannedMinutes * 100m / GoalMinutes));
}

public sealed record WeekDayView(DateOnly Date, DayOfWeek DayOfWeek, string Name, string PersianDate, bool IsSelected, IReadOnlyList<ScheduleBlockItem> Blocks);

public sealed record ScheduleBlockCardModel(
    ScheduleBlockItem Block,
    IReadOnlyList<ScheduleActivityOption> Activities,
    DateOnly WeekStart,
    string DisplayMode);

public sealed record WeekScheduleView(
    DateOnly WeekStart,
    IReadOnlyList<ScheduleActivityOption> Activities,
    IReadOnlyList<WeekDayView> Days,
    IReadOnlyList<ActivityPlanSummary> Summaries,
    int PlannedMinutes)
{
    public decimal PlannedHours => PlannedMinutes / 60m;
    public DateOnly WeekEnd => WeekStart.AddDays(6);
}
