using System.ComponentModel.DataAnnotations;
using Donysh.Life.Features.Activities;
using Donysh.Life.Features.Scheduling;

namespace Donysh.Life.Features.Tracking;

public sealed class ActualActivityForm
{
    [Required(ErrorMessage = "یک فعالیت انتخاب کنید.")]
    public Guid ActivityId { get; set; }
    [Required(ErrorMessage = "تاریخ انجام فعالیت را وارد کنید.")]
    public DateOnly OccurredDate { get; set; }
    [RegularExpression("^(?:[01]\\d|2[0-3]):[0-5]\\d$", ErrorMessage = "ساعت شروع معتبر نیست.")]
    public string? StartTime { get; set; }
    [Range(1, 1440, ErrorMessage = "مدت واقعی باید بین ۱ تا ۱۴۴۰ دقیقه باشد.")]
    public int ActualMinutes { get; set; } = 30;
    [StringLength(160, ErrorMessage = "یادداشت نمی‌تواند بیشتر از ۱۶۰ نویسه باشد.")]
    public string? Note { get; set; }
}

public sealed record ActualActivityItem(Guid Id, Guid ActivityId, string ActivityTitle, string ColorHex, string IconKey,
    DateOnly OccurredDate, int? StartMinute, int ActualMinutes, string? Note)
{
    public string ColorClass => ActivityCatalog.ColorClass(ColorHex);
    public string IconGlyph => ActivityCatalog.IconGlyph(IconKey);
    public string PersianDate => WeekCalendar.PersianDate(OccurredDate);
    public string? StartLabel => StartMinute is null ? null : $"{StartMinute / 60:00}:{StartMinute % 60:00}";
    public string DurationLabel => WeekCalendar.DurationName(ActualMinutes);
}
