using Donysh.Life.Features.Activities;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;

namespace Donysh.Life.Pages;

public sealed class SetupModel(LifeActivityService activities) : PageModel
{
    [BindProperty] public List<SetupActivityInput> Suggestions { get; set; } = [];

    public int Step { get; private set; } = 1;
    public bool HasActivities { get; private set; }
    public string? CommandError { get; private set; }

    public async Task OnGetAsync(int step = 1, CancellationToken cancellationToken = default)
    {
        Step = step == 2 ? 2 : 1;
        HasActivities = (await activities.GetOverviewAsync(cancellationToken)).ActivityCount > 0;
        if (Step == 2) PopulateSuggestions();
    }

    public IActionResult OnPostBegin() => RedirectToPage(new { step = 2 });

    public async Task<IActionResult> OnPostCompleteAsync(CancellationToken cancellationToken)
    {
        Step = 2;
        if (!ModelState.IsValid) return Page();
        var result = await activities.CompleteSetupAsync(Suggestions, cancellationToken);
        if (!result.Succeeded) { CommandError = result.Message; return Page(); }
        TempData["ActivityMessage"] = result.Message;
        return RedirectToPage("/Activities");
    }

    private void PopulateSuggestions() => Suggestions = ActivityCatalog.Templates.Select(item => new SetupActivityInput
    {
        TemplateKey = item.Key,
        IsSelected = item.Key is "work" or "exercise" or "study",
        WeeklyHours = item.SuggestedHours
    }).ToList();
}
