using Donysh.Life.Features.Reporting;
using Microsoft.AspNetCore.Mvc.RazorPages;

namespace Donysh.Life.Pages;

public sealed class ProgressModel(ProgressReportService reports) : PageModel
{
    public ProgressReport Report { get; private set; } = new(
        DateOnly.MinValue, DateOnly.MinValue, 0, 0, 0, 0, 0, 0, 0, 0, 0, [], []);

    public async Task OnGetAsync(DateOnly? week, CancellationToken cancellationToken) =>
        Report = await reports.GetWeekAsync(week, cancellationToken);
}
