using Donysh.Life.Features.Activities;
using Donysh.Life.Features.Scheduling;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;

namespace Donysh.Life.Pages;

public sealed class WeekModel(WeeklyScheduleService schedules, OccurrenceTrackingService tracking) : PageModel
{
    [BindProperty] public SchedulePatternForm Input { get; set; } = new();
    [BindProperty] public Guid? PatternId { get; set; }
    [BindProperty] public bool ConfirmDelete { get; set; }
    [BindProperty] public DayOfWeek SelectedDay { get; set; } = DayOfWeek.Saturday;
    [BindProperty] public DateOnly WeekStart { get; set; }
    [BindProperty] public ScheduleInteractionForm Interaction { get; set; } = new();
    [BindProperty] public DateOnly OriginalDate { get; set; }
    [BindProperty] public OccurrenceRecordForm Occurrence { get; set; } = new();
    [BindProperty(SupportsGet = true, Name = "view")] public string DisplayMode { get; set; } = "week";

    public WeekScheduleView View { get; private set; } = new(
        DateOnly.MinValue, [], [], [], 0);
    public string? CommandError { get; private set; }

    public async Task OnGetAsync(DayOfWeek? day, DateOnly? week, CancellationToken cancellationToken)
    {
        DisplayMode = DisplayMode == "day" ? "day" : "week";
        SelectedDay = day is not null && WeekCalendar.Days.Contains(day.Value) ? day.Value : DayOfWeek.Saturday;
        Input.DayOfWeek = SelectedDay;
        await LoadAsync(SelectedDay, week, cancellationToken);
    }

    public async Task<IActionResult> OnPostSaveAsync(CancellationToken cancellationToken)
    {
        SelectedDay = Input.DayOfWeek;
        if (!ModelState.IsValid) { await LoadAsync(SelectedDay, WeekStart == default ? null : WeekStart, cancellationToken); return Page(); }
        var result = await schedules.SaveAsync(PatternId, Input, cancellationToken);
        if (!result.Succeeded) { CommandError = result.Message; await LoadAsync(SelectedDay, WeekStart == default ? null : WeekStart, cancellationToken); return Page(); }
        TempData["ScheduleMessage"] = result.Message;
        return RedirectToCalendar(Input.DayOfWeek);
    }

    public async Task<IActionResult> OnPostToggleAsync(CancellationToken cancellationToken)
    {
        if (PatternId is null) return BadRequest();
        var result = await schedules.ToggleAsync(PatternId.Value, cancellationToken);
        TempData[result.Succeeded ? "ScheduleMessage" : "ScheduleError"] = result.Message;
        return RedirectToCalendar(SelectedDay);
    }

    public async Task<IActionResult> OnPostDeleteAsync(CancellationToken cancellationToken)
    {
        if (PatternId is null || !ConfirmDelete) return BadRequest();
        var result = await schedules.DeleteAsync(PatternId.Value, cancellationToken);
        TempData[result.Succeeded ? "ScheduleMessage" : "ScheduleError"] = result.Message;
        return RedirectToCalendar(SelectedDay);
    }

    public async Task<IActionResult> OnPostInteractAsync(CancellationToken cancellationToken)
    {
        var result = ModelState.IsValid
            ? await schedules.ApplyInteractionAsync(Interaction, cancellationToken)
            : ActivityCommandResult.Failure("اطلاعات جابه‌جایی معتبر نیست.");
        TempData[result.Succeeded ? "ScheduleMessage" : "ScheduleError"] = result.Message;
        return RedirectToCalendar(Interaction.TargetDate.DayOfWeek);
    }

    public async Task<IActionResult> OnPostCancelOccurrenceAsync(CancellationToken cancellationToken)
    {
        if (PatternId is null || OriginalDate == default) return BadRequest();
        var result = await schedules.CancelOccurrenceAsync(PatternId.Value, OriginalDate, cancellationToken);
        TempData[result.Succeeded ? "ScheduleMessage" : "ScheduleError"] = result.Message;
        return RedirectToCalendar(SelectedDay);
    }

    public async Task<IActionResult> OnPostRecordOccurrenceAsync(CancellationToken cancellationToken)
    {
        var result = ModelState.IsValid
            ? await tracking.RecordAsync(Occurrence, cancellationToken)
            : ActivityCommandResult.Failure("اطلاعات عملکرد معتبر نیست.");
        TempData[result.Succeeded ? "ScheduleMessage" : "ScheduleError"] = result.Message;
        return RedirectToCalendar(SelectedDay);
    }

    public async Task<IActionResult> OnPostClearOccurrenceAsync(CancellationToken cancellationToken)
    {
        if (PatternId is null || OriginalDate == default) return BadRequest();
        var result = await tracking.ClearAsync(PatternId.Value, OriginalDate, cancellationToken);
        TempData[result.Succeeded ? "ScheduleMessage" : "ScheduleError"] = result.Message;
        return RedirectToCalendar(SelectedDay);
    }

    private async Task LoadAsync(DayOfWeek day, DateOnly? week, CancellationToken cancellationToken)
    {
        View = await schedules.GetWeekAsync(day, week, cancellationToken);
        WeekStart = View.WeekStart;
    }

    private RedirectToPageResult RedirectToCalendar(DayOfWeek day) => RedirectToPage(new
    {
        day,
        week = WeekStart == default ? null : WeekStart.ToString("yyyy-MM-dd"),
        view = DisplayMode == "day" ? "day" : "week"
    });
}
