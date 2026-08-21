using System.ComponentModel.DataAnnotations;
using HesabYar.Web.Data;
using HesabYar.Web.Domain;
using HesabYar.Web.Services;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using Microsoft.EntityFrameworkCore;

namespace HesabYar.Web.Pages.Installments;

public sealed class IndexModel(ApplicationDbContext db, IWorkspaceContext workspaceContext) : PageModel
{
    [BindProperty]
    public InputModel Input { get; set; } = new();

    public IReadOnlyList<InstallmentPlan> Items { get; private set; } = [];

    public sealed class InputModel
    {
        public Guid? Id { get; set; }

        [Required(ErrorMessage = "عنوان قسط را وارد کنید.")]
        [StringLength(120, MinimumLength = 2, ErrorMessage = "عنوان باید بین ۲ تا ۱۲۰ کاراکتر باشد.")]
        public string Title { get; set; } = string.Empty;

        [StringLength(500, ErrorMessage = "توضیحات نمی‌تواند بیشتر از ۵۰۰ کاراکتر باشد.")]
        public string? Notes { get; set; }

        [Range(1, 999_999_999_999, ErrorMessage = "مبلغ کل باید بیشتر از صفر باشد.")]
        public decimal TotalAmount { get; set; }

        [Range(1, 999_999_999_999, ErrorMessage = "مبلغ هر قسط باید بیشتر از صفر باشد.")]
        public decimal InstallmentAmount { get; set; }

        [Range(1, 600, ErrorMessage = "تعداد اقساط باید بین ۱ تا ۶۰۰ باشد.")]
        public int InstallmentCount { get; set; }

        [Range(0, 600, ErrorMessage = "تعداد اقساط پرداخت‌شده معتبر نیست.")]
        public int PaidInstallments { get; set; }

        [Required(ErrorMessage = "تاریخ اولین سررسید را وارد کنید.")]
        public DateOnly FirstDueDate { get; set; }

        public bool IsCompleted { get; set; }
    }

    public async Task OnGetAsync(Guid? edit, CancellationToken cancellationToken)
    {
        var workspace = await workspaceContext.RequireCurrentAsync(cancellationToken);
        if (edit.HasValue)
        {
            var item = await db.InstallmentPlans.AsNoTracking().SingleOrDefaultAsync(
                x => x.Id == edit.Value && x.WorkspaceId == workspace.Id,
                cancellationToken);
            if (item is not null)
            {
                Input = new InputModel
                {
                    Id = item.Id,
                    Title = item.Title,
                    Notes = item.Notes,
                    TotalAmount = item.TotalAmount,
                    InstallmentAmount = item.InstallmentAmount,
                    InstallmentCount = item.InstallmentCount,
                    PaidInstallments = item.PaidInstallments,
                    FirstDueDate = item.FirstDueDate,
                    IsCompleted = item.IsCompleted
                };
            }
        }

        await LoadAsync(workspace.Id, cancellationToken);
    }

    public async Task<IActionResult> OnPostSaveAsync(CancellationToken cancellationToken)
    {
        var workspace = await workspaceContext.RequireCurrentAsync(cancellationToken);
        if (Input.PaidInstallments > Input.InstallmentCount)
        {
            ModelState.AddModelError("Input.PaidInstallments", "تعداد پرداخت‌شده نمی‌تواند از تعداد کل بیشتر باشد.");
        }

        if (!ModelState.IsValid)
        {
            await LoadAsync(workspace.Id, cancellationToken);
            return Page();
        }

        InstallmentPlan? item = null;
        if (Input.Id.HasValue)
        {
            item = await db.InstallmentPlans.SingleOrDefaultAsync(
                x => x.Id == Input.Id.Value && x.WorkspaceId == workspace.Id,
                cancellationToken);
            if (item is null)
            {
                return NotFound();
            }
        }

        item ??= new InstallmentPlan { WorkspaceId = workspace.Id };
        if (item.Id == default || db.Entry(item).State == EntityState.Detached)
        {
            db.InstallmentPlans.Add(item);
        }

        item.Title = Input.Title.Trim();
        item.Notes = string.IsNullOrWhiteSpace(Input.Notes) ? null : Input.Notes.Trim();
        item.TotalAmount = Input.TotalAmount;
        item.InstallmentAmount = Input.InstallmentAmount;
        item.InstallmentCount = Input.InstallmentCount;
        item.PaidInstallments = Input.PaidInstallments;
        item.FirstDueDate = Input.FirstDueDate;
        item.IsCompleted = Input.IsCompleted || Input.PaidInstallments == Input.InstallmentCount;
        item.UpdatedAtUtc = DateTime.UtcNow;

        await db.SaveChangesAsync(cancellationToken);
        TempData["Success"] = Input.Id.HasValue ? "قسط ویرایش شد." : "قسط افزوده شد.";
        return RedirectToPage();
    }

    public async Task<IActionResult> OnPostDeleteAsync(Guid id, CancellationToken cancellationToken)
    {
        var workspace = await workspaceContext.RequireCurrentAsync(cancellationToken);
        var item = await db.InstallmentPlans.SingleOrDefaultAsync(
            x => x.Id == id && x.WorkspaceId == workspace.Id,
            cancellationToken);
        if (item is null)
        {
            return NotFound();
        }

        db.InstallmentPlans.Remove(item);
        await db.SaveChangesAsync(cancellationToken);
        TempData["Success"] = "قسط حذف شد.";
        return RedirectToPage();
    }

    private async Task LoadAsync(Guid workspaceId, CancellationToken cancellationToken)
    {
        Items = await db.InstallmentPlans
            .Where(x => x.WorkspaceId == workspaceId)
            .OrderBy(x => x.IsCompleted)
            .ThenBy(x => x.FirstDueDate)
            .ThenBy(x => x.Title)
            .AsNoTracking()
            .ToListAsync(cancellationToken);
    }
}
