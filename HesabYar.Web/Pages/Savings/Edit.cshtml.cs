using System.ComponentModel.DataAnnotations;
using HesabYar.Web.Data;
using HesabYar.Web.Services;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using Microsoft.EntityFrameworkCore;

namespace HesabYar.Web.Pages.Savings;

public sealed class EditModel(
    ApplicationDbContext db,
    IWorkspaceContext workspaceContext) : PageModel
{

    [BindProperty]
    public GoalInput Goal { get; set; } = new();


    public async Task<IActionResult> OnGetAsync(
        Guid id,
        CancellationToken cancellationToken)
    {
        var workspace =
            await workspaceContext.RequireCurrentAsync(cancellationToken);


        var goal = await db.SavingsGoals
            .AsNoTracking()
            .FirstOrDefaultAsync(
                x => x.Id == id &&
                     x.WorkspaceId == workspace.Id,
                cancellationToken);


        if (goal is null)
        {
            return NotFound();
        }


        Goal = new GoalInput
        {
            Id = goal.Id,
            Name = goal.Name,
            TargetAmount = goal.TargetAmount,
            MonthlyTargetAmount = goal.MonthlyTargetAmount,
            TargetDate = goal.TargetDate
        };


        return Page();
    }



    public async Task<IActionResult> OnPostAsync(
        CancellationToken cancellationToken)
    {
        var workspace =
            await workspaceContext.RequireCurrentAsync(cancellationToken);


        if (!ModelState.IsValid)
        {
            return Page();
        }


        var goal = await db.SavingsGoals
            .FirstOrDefaultAsync(
                x => x.Id == Goal.Id &&
                     x.WorkspaceId == workspace.Id,
                cancellationToken);


        if (goal is null)
        {
            return NotFound();
        }


        var duplicateName = await db.SavingsGoals
            .AnyAsync(
                x => x.WorkspaceId == workspace.Id &&
                     x.Id != Goal.Id &&
                     x.Name == Goal.Name.Trim(),
                cancellationToken);


        if (duplicateName)
        {
            ModelState.AddModelError(
                "Goal.Name",
                "هدف دیگری با این عنوان وجود دارد.");

            return Page();
        }


        goal.Name = Goal.Name.Trim();
        goal.TargetAmount = Goal.TargetAmount;
        goal.MonthlyTargetAmount = Goal.MonthlyTargetAmount;
        goal.TargetDate = Goal.TargetDate;


        await db.SaveChangesAsync(cancellationToken);


        TempData["Success"] = "هدف پس‌انداز ویرایش شد.";

        return RedirectToPage("./Index");
    }



    public sealed class GoalInput
    {
        public Guid Id { get; set; }


        [Required(ErrorMessage = "نام هدف را وارد کنید.")]
        [StringLength(120, MinimumLength = 2)]
        public string Name { get; set; } = string.Empty;


        [Range(1, 999_999_999_999,
            ErrorMessage = "مبلغ هدف باید بیشتر از صفر باشد.")]
        public decimal TargetAmount { get; set; }


        [Range(0, 999_999_999_999,
            ErrorMessage = "هدف ماهانه معتبر نیست.")]
        public decimal MonthlyTargetAmount { get; set; }


        public DateOnly? TargetDate { get; set; }
    }
}