using Donysh.Life.Features.Activities;
using Donysh.Life.Features.Scheduling;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;

namespace Donysh.Life.Pages;

public sealed class WeekModel(WeeklyScheduleService schedules, OccurrenceTrackingService tracking, ReminderScheduleService reminders) : PageModel
{
    [BindProperty] public SchedulePatternForm Input { get; set; } = new();
    [BindProperty] public Guid? PatternId { get; set; }
    [BindProperty] public bool ConfirmDelete { get; set; }
    [BindProperty] public DayOfWeek SelectedDay { get; set; } = WeekCalendar.Today().DayOfWeek;
    [BindProperty] public DateOnly WeekStart { get; set; }
    [BindProperty] public ScheduleInteractionForm Interaction { get; set; } = new();
    [BindProperty] public DateOnly OriginalDate { get; set; }
    [BindProperty] public OccurrenceRecordForm Occurrence { get; set; } = new();
    [BindProperty(SupportsGet = true, Name = "view")] public string? DisplayMode { get; set; }

    public WeekScheduleView View { get; private set; } = new(
        DateOnly.MinValue, [], [], [], 0);
    public string? CommandError { get; private set; }

    public async Task OnGetAsync(DayOfWeek? day, DateOnly? week, CancellationToken cancellationToken)
    {
        DisplayMode = DisplayMode == "day" ? "day" : "week";
        SelectedDay = day is not null && WeekCalendar.Days.Contains(day.Value) ? day.Value : WeekCalendar.Today().DayOfWeek;
        Input.DayOfWeek = SelectedDay;
        await LoadAsync(SelectedDay, week, cancellationToken);
    }

    public async Task<IActionResult> OnGetDueRemindersAsync(CancellationToken cancellationToken)
    {
        Response.Headers.CacheControl = "no-store";
        return new JsonResult(await reminders.UpcomingAsync(cancellationToken));
    }

    public async Task<IActionResult> OnGetCalendarReminderAsync(Guid patternId, DateOnly scheduledDate, CancellationToken cancellationToken)
    {
        var week = await schedules.GetWeekAsync(scheduledDate.DayOfWeek, scheduledDate, cancellationToken);
        var block = week.Days.SelectMany(day => day.Blocks).SingleOrDefault(item => item.Id == patternId
            && item.ScheduledDate == scheduledDate && item.IsActive && item.ReminderMinutesBefore.HasValue);
        if (block is null) return NotFound();
        var calendar = ScheduleCalendarExport.Build(block);
        return File(System.Text.Encoding.UTF8.GetBytes(calendar), "text/calendar; charset=utf-8", $"donysh-life-{scheduledDate:yyyy-MM-dd}.ics");
    }

    public async Task<IActionResult> OnPostSaveAsync(CancellationToken cancellationToken)
    {
        KeepValidationFor(nameof(Input));
        DisplayMode = NormalizeDisplayMode(DisplayMode);
        SelectedDay = Input.DayOfWeek;
        if (!ModelState.IsValid) { await LoadAsync(SelectedDay, WeekStart == default ? null : WeekStart, cancellationToken); return Page(); }
        var result = await schedules.SaveAsync(PatternId, Input, cancellationToken);
        if (!result.Succeeded) { CommandError = result.Message; await LoadAsync(SelectedDay, WeekStart == default ? null : WeekStart, cancellationToken); return Page(); }
        TempData["ScheduleMessage"] = result.Message;
        return RedirectToCalendar(Input.DayOfWeek);
    }

    public async Task<IActionResult> OnPostToggleAsync(CancellationToken cancellationToken)
    {
        DisplayMode = NormalizeDisplayMode(DisplayMode);
        if (PatternId is null) return BadRequest();
        var result = await schedules.ToggleAsync(PatternId.Value, cancellationToken);
        TempData[result.Succeeded ? "ScheduleMessage" : "ScheduleError"] = result.Message;
        return RedirectToCalendar(SelectedDay);
    }

    public async Task<IActionResult> OnPostDeleteAsync(CancellationToken cancellationToken)
    {
        DisplayMode = NormalizeDisplayMode(DisplayMode);
        if (PatternId is null || !ConfirmDelete) return BadRequest();
        var result = await schedules.DeleteAsync(PatternId.Value, cancellationToken);
        TempData[result.Succeeded ? "ScheduleMessage" : "ScheduleError"] = result.Message;
        return RedirectToCalendar(SelectedDay);
    }

    public async Task<IActionResult> OnPostInteractAsync(CancellationToken cancellationToken)
    {
        KeepValidationFor(nameof(Interaction));
        DisplayMode = NormalizeDisplayMode(DisplayMode);
        var result = ModelState.IsValid
            ? await schedules.ApplyInteractionAsync(Interaction, cancellationToken)
            : ActivityCommandResult.Failure("اطلاعات جابه‌جایی معتبر نیست.");
        TempData[result.Succeeded ? "ScheduleMessage" : "ScheduleError"] = result.Message;
        return RedirectToCalendar(Interaction.TargetDate.DayOfWeek);
    }

    public async Task<IActionResult> OnPostCancelOccurrenceAsync(CancellationToken cancellationToken)
    {
        DisplayMode = NormalizeDisplayMode(DisplayMode);
        if (PatternId is null || OriginalDate == default) return BadRequest();
        var result = await schedules.CancelOccurrenceAsync(PatternId.Value, OriginalDate, cancellationToken);
        TempData[result.Succeeded ? "ScheduleMessage" : "ScheduleError"] = result.Message;
        return RedirectToCalendar(SelectedDay);
    }

    public async Task<IActionResult> OnPostRecordOccurrenceAsync(CancellationToken cancellationToken)
    {
        KeepValidationFor(nameof(Occurrence));
        DisplayMode = NormalizeDisplayMode(DisplayMode);
        var result = ModelState.IsValid
            ? await tracking.RecordAsync(Occurrence, cancellationToken)
            : ActivityCommandResult.Failure("اطلاعات عملکرد معتبر نیست.");
        TempData[result.Succeeded ? "ScheduleMessage" : "ScheduleError"] = result.Message;
        return RedirectToCalendar(SelectedDay);
    }

    public async Task<IActionResult> OnPostClearOccurrenceAsync(CancellationToken cancellationToken)
    {
        DisplayMode = NormalizeDisplayMode(DisplayMode);
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
        view = NormalizeDisplayMode(DisplayMode)
    });

    private void KeepValidationFor(string prefix)
    {
        var acceptedPrefix = prefix + ".";
        foreach (var key in ModelState.Keys.Where(key =>
                     !key.Equals(prefix, StringComparison.OrdinalIgnoreCase)
                     && !key.StartsWith(acceptedPrefix, StringComparison.OrdinalIgnoreCase)).ToList())
            ModelState.Remove(key);
    }

    private static string NormalizeDisplayMode(string? mode) => mode == "day" ? "day" : "week";
}
