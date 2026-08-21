using HesabYar.Web.Data;
using HesabYar.Web.Helpers;
using HesabYar.Web.Services;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace HesabYar.Web.ViewComponents;

public sealed class ObligationAlertViewComponent(ApplicationDbContext db, IWorkspaceContext workspaceContext) : ViewComponent
{
    public sealed record AlertModel(int Count, int OverdueCount);

    public async Task<IViewComponentResult> InvokeAsync()
    {
        var workspace = await workspaceContext.GetCurrentAsync();
        if (workspace is null)
        {
            return Content(string.Empty);
        }

        var today = DateOnly.FromDateTime(DateTime.Now);
        var period = PersianCalendarHelper.GetYearMonth(today);
        var obligations = await db.RecurringObligations
            .Where(x => x.WorkspaceId == workspace.Id && x.IsActive)
            .AsNoTracking()
            .ToListAsync();

        var ids = obligations.Select(x => x.Id).ToList();
        var paidIds = ids.Count == 0
     ? new HashSet<Guid>()
     : (await db.RecurringObligationPayments
         .AsNoTracking()
         .Where(x => ids.Contains(x.RecurringObligationId) &&
                     x.PeriodYear == period.Year &&
                     x.PeriodMonth == period.Month)
         .Select(x => x.RecurringObligationId)
         .ToListAsync()).ToHashSet();

        var count = 0;
        var overdue = 0;
        foreach (var item in obligations)
        {
            if (!RecurringObligationHelper.IsScheduledForPeriod(item, period) || paidIds.Contains(item.Id))
            {
                continue;
            }

            var due = RecurringObligationHelper.GetDueDate(item, period);
            var days = due.DayNumber - today.DayNumber;
            if (due < today)
            {
                count++;
                overdue++;
            }
            else if (days <= item.ReminderDaysBefore)
            {
                count++;
            }
        }

        return View(new AlertModel(count, overdue));
    }
}
