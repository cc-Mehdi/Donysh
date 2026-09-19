using HesabYar.Web.Domain;

namespace HesabYar.Web.Pages.Expenses;

public static class ExpenseListFilter
{
    public static IQueryable<Expense> Apply(
        IQueryable<Expense> query, string? q, Guid? categoryId, bool needsReview,
        DateOnly? from, DateOnly? to)
    {
        if (!string.IsNullOrWhiteSpace(q))
        {
            var search = q.Trim();
            query = query.Where(x => x.Reason.Contains(search));
        }

        if (categoryId.HasValue)
            query = query.Where(x => x.CategoryId == categoryId.Value);

        if (needsReview)
            query = query.Where(x => x.NeedsReview);

        if (from.HasValue)
            query = query.Where(x => x.ExpenseDate >= from.Value);

        if (to.HasValue)
            query = query.Where(x => x.ExpenseDate <= to.Value);

        return query;
    }
}
