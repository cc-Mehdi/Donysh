using Donysh.Life.Features.Activities;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;

namespace Donysh.Life.Pages;

public sealed class ActivitiesModel(LifeActivityService activities) : PageModel
{
    [BindProperty] public ActivityForm Input { get; set; } = new();
    [BindProperty] public Guid? ActivityId { get; set; }
    [BindProperty] public bool ConfirmDelete { get; set; }

    public IReadOnlyList<ActivityListItem> Items { get; private set; } = [];
    public string? CommandError { get; private set; }
    public bool OpenCreateDialog { get; private set; }

    public async Task OnGetAsync(CancellationToken cancellationToken) => await LoadAsync(cancellationToken);

    public async Task<IActionResult> OnPostSaveAsync(CancellationToken cancellationToken)
    {
        if (!ModelState.IsValid)
        {
            OpenCreateDialog = ActivityId is null;
            await LoadAsync(cancellationToken);
            return Page();
        }

        var result = await activities.SaveAsync(ActivityId, Input, cancellationToken);
        if (!result.Succeeded)
        {
            CommandError = result.Message;
            OpenCreateDialog = ActivityId is null;
            await LoadAsync(cancellationToken);
            return Page();
        }

        TempData["ActivityMessage"] = result.Message;
        return RedirectToPage();
    }

    public async Task<IActionResult> OnPostToggleAsync(CancellationToken cancellationToken)
    {
        if (ActivityId is null) return BadRequest();
        var result = await activities.ToggleAsync(ActivityId.Value, cancellationToken);
        TempData["ActivityMessage"] = result.Message;
        return RedirectToPage();
    }

    public async Task<IActionResult> OnPostDeleteAsync(CancellationToken cancellationToken)
    {
        if (ActivityId is null || !ConfirmDelete) return BadRequest();
        var result = await activities.DeleteAsync(ActivityId.Value, cancellationToken);
        TempData["ActivityMessage"] = result.Message;
        return RedirectToPage();
    }

    private async Task LoadAsync(CancellationToken cancellationToken) =>
        Items = await activities.ListAsync(cancellationToken);
}
