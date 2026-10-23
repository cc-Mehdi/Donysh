using Donysh.Life.Features.Activities;
using Donysh.Life.Features.Reporting;
using Donysh.Life.Features.Scheduling;
using Donysh.Life.Features.Tracking;
using System.Security.Claims;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;

namespace Donysh.Life.Pages;

public sealed class IndexModel(
    LifeActivityService activities,
    ActualActivityService actualActivities,
    WeeklyScheduleService schedules,
    ProgressReportService reports) : PageModel
{
    [BindProperty] public ActualActivityForm ManualEntry { get; set; } = new();
    [BindProperty] public Guid? EntryId { get; set; }
    [BindProperty] public bool ConfirmDelete { get; set; }

    public LifeOverview Overview { get; private set; } = new(0, 0, 0, 0);
    public IReadOnlyList<ActivityListItem> Activities { get; private set; } = [];
    public IReadOnlyList<ActualActivityItem> RecentEntries { get; private set; } = [];
    public WeekScheduleView? Week { get; private set; }
    public ProgressReport? Progress { get; private set; }
    public IReadOnlyList<ScheduleBlockItem> TodayBlocks { get; private set; } = [];
    public DateOnly TodayDate { get; private set; } = WeekCalendar.Today();
    public string DisplayName => User.FindFirstValue("name")?.Split(' ', StringSplitOptions.RemoveEmptyEntries).FirstOrDefault()
        ?? User.Identity?.Name?.Split(' ', StringSplitOptions.RemoveEmptyEntries).FirstOrDefault()
        ?? "دوست من";
    public string Greeting
    {
        get
        {
            var hour = TehranNow().Hour;
            return hour < 12 ? "صبح بخیر" : hour < 18 ? "عصر بخیر" : "شب بخیر";
        }
    }
    public int TodayPlannedMinutes => Progress?.Days.FirstOrDefault(day => day.Date == TodayDate)?.PlannedMinutes ?? 0;
    public int TodayActualMinutes => Progress?.Days.FirstOrDefault(day => day.Date == TodayDate)?.ActualMinutes ?? 0;
    public string? CommandError { get; private set; }

    public async Task OnGetAsync(CancellationToken cancellationToken)
    {
        ManualEntry.OccurredDate = WeekCalendar.Today();
        await LoadAsync(cancellationToken);
    }

    public async Task<IActionResult> OnPostSaveActualAsync(CancellationToken cancellationToken)
    {
        if (!ModelState.IsValid)
        {
            await LoadAsync(cancellationToken);
            return Page();
        }
        var result = await actualActivities.SaveAsync(EntryId, ManualEntry, cancellationToken);
        if (!result.Succeeded)
        {
            CommandError = result.Message;
            await LoadAsync(cancellationToken);
            return Page();
        }
        TempData["ActualMessage"] = result.Message;
        return RedirectToPage();
    }

    public async Task<IActionResult> OnPostDeleteActualAsync(CancellationToken cancellationToken)
    {
        if (EntryId is null || !ConfirmDelete) return BadRequest();
        var result = await actualActivities.DeleteAsync(EntryId.Value, cancellationToken);
        TempData[result.Succeeded ? "ActualMessage" : "ActualError"] = result.Message;
        return RedirectToPage();
    }

    private async Task LoadAsync(CancellationToken cancellationToken)
    {
        Overview = await activities.GetOverviewAsync(cancellationToken);
        Activities = await activities.ListAsync(cancellationToken);
        RecentEntries = await actualActivities.ListRecentAsync(cancellationToken: cancellationToken);
        TodayDate = WeekCalendar.Today();
        Week = await schedules.GetWeekAsync(TodayDate.DayOfWeek, cancellationToken: cancellationToken);
        Progress = await reports.GetWeekAsync(cancellationToken: cancellationToken);
        TodayBlocks = Week.Days.FirstOrDefault(day => day.Date == TodayDate)?.Blocks
            .Where(block => block.IsActive).OrderBy(block => block.StartMinute).ToList() ?? [];
    }

    private static DateTime TehranNow()
    {
        try { return TimeZoneInfo.ConvertTime(DateTimeOffset.UtcNow, TimeZoneInfo.FindSystemTimeZoneById("Asia/Tehran")).DateTime; }
        catch (TimeZoneNotFoundException) { return DateTimeOffset.UtcNow.AddHours(3.5).DateTime; }
    }
}
