using System.ComponentModel.DataAnnotations;

namespace Donysh.Life.Features.Activities;

public sealed class ActivityForm
{
    [Required(ErrorMessage = "عنوان فعالیت را بنویسید.")]
    [StringLength(80, MinimumLength = 2, ErrorMessage = "عنوان باید بین ۲ تا ۸۰ نویسه باشد.")]
    public string Title { get; set; } = string.Empty;

    [Required(ErrorMessage = "یک رنگ انتخاب کنید.")]
    [RegularExpression("^#[0-9A-Fa-f]{6}$", ErrorMessage = "رنگ انتخاب‌شده معتبر نیست.")]
    public string ColorHex { get; set; } = "#0D9488";

    [Required(ErrorMessage = "یک نماد انتخاب کنید.")]
    public string IconKey { get; set; } = "circle";

    [StringLength(60, ErrorMessage = "دسته‌بندی نمی‌تواند بیشتر از ۶۰ نویسه باشد.")]
    public string? Category { get; set; }

    [Range(typeof(decimal), "0.5", "168", ErrorMessage = "هدف هفتگی باید بین نیم ساعت تا ۱۶۸ ساعت باشد.")]
    public decimal WeeklyHours { get; set; } = 5;
}

public sealed class SetupActivityInput
{
    public string TemplateKey { get; set; } = string.Empty;
    public bool IsSelected { get; set; }

    [Range(typeof(decimal), "0.5", "168", ErrorMessage = "هدف هفتگی باید بین نیم ساعت تا ۱۶۸ ساعت باشد.")]
    public decimal WeeklyHours { get; set; }
}

public sealed record ActivityListItem(
    Guid Id,
    string Title,
    string ColorHex,
    string IconKey,
    string? Category,
    bool IsActive,
    int TargetMinutes)
{
    public decimal TargetHours => TargetMinutes / 60m;
    public string IconGlyph => ActivityCatalog.IconGlyph(IconKey);
}

public sealed record LifeOverview(int ActivityCount, int ActiveActivityCount, int WeeklyTargetMinutes, int PlannedWeeklyMinutes)
{
    public decimal WeeklyTargetHours => WeeklyTargetMinutes / 60m;
    public decimal PlannedWeeklyHours => PlannedWeeklyMinutes / 60m;
}

public sealed record ActivityCommandResult(bool Succeeded, string Message)
{
    public static ActivityCommandResult Success(string message) => new(true, message);
    public static ActivityCommandResult Failure(string message) => new(false, message);
}
