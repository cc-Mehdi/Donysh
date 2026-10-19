using Donysh.Life.Features.Activities;
using Donysh.Life.Features.Scheduling;
using Donysh.Life.Features.Tracking;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;

namespace Donysh.Life.Pages;

public sealed class IndexModel(LifeActivityService activities, ActualActivityService actualActivities) : PageModel
{
    [BindProperty] public ActualActivityForm ManualEntry { get; set; } = new();
    [BindProperty] public Guid? EntryId { get; set; }
    [BindProperty] public bool ConfirmDelete { get; set; }

    public LifeOverview Overview { get; private set; } = new(0, 0, 0, 0);
    public IReadOnlyList<ActivityListItem> Activities { get; private set; } = [];
    public IReadOnlyList<ActualActivityItem> RecentEntries { get; private set; } = [];
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
    }
}
