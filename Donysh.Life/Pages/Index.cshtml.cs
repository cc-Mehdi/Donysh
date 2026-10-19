using Donysh.Life.Features.Activities;
using Microsoft.AspNetCore.Mvc.RazorPages;

namespace Donysh.Life.Pages;

public sealed class IndexModel(LifeActivityService activities) : PageModel
{
    public LifeOverview Overview { get; private set; } = new(0, 0, 0);

    public async Task OnGetAsync(CancellationToken cancellationToken) =>
        Overview = await activities.GetOverviewAsync(cancellationToken);
}
