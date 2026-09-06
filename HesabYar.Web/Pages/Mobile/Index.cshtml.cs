using System.ComponentModel.DataAnnotations;
using System.Security.Cryptography;
using HesabYar.Web.Data;
using HesabYar.Web.Domain;
using HesabYar.Web.Services;
using HesabYar.Web.Sms;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using Microsoft.EntityFrameworkCore;

namespace HesabYar.Web.Pages.Mobile;

public sealed class IndexModel(ApplicationDbContext db, IWorkspaceContext workspaceContext) : PageModel
{
    [BindProperty, Required, StringLength(80)] public string DeviceName { get; set; } = "گوشی من";
    [BindProperty] public Guid CategoryId { get; set; }
    public string? PairCode { get; private set; }
    public Workspace Workspace { get; private set; } = null!;
    public List<ExpenseCategory> Categories { get; private set; } = [];
    public List<MobileDevice> Devices { get; private set; } = [];
    public async Task OnGetAsync(CancellationToken ct) => await LoadAsync(ct);

    public async Task<IActionResult> OnPostAsync(CancellationToken ct)
    {
        await LoadAsync(ct);
        if (!Categories.Any(x => x.Id == CategoryId)) ModelState.AddModelError(nameof(CategoryId), "دسته معتبر انتخاب کنید.");
        var now = DateTime.UtcNow;
        if (await db.MobileDevices.CountAsync(x => x.UserId == workspaceContext.UserId && !x.Revoked && (x.TokenHash != null || x.PairExpiresAtUtc > now), ct) >= 3)
            ModelState.AddModelError("", "حداکثر ۳ اتصال فعال؛ ابتدا یکی را لغو کنید.");
        if (!ModelState.IsValid) return Page();
        PairCode = Convert.ToHexString(RandomNumberGenerator.GetBytes(16));
        db.MobileDevices.Add(new MobileDevice {
            UserId = workspaceContext.UserId!, WorkspaceId = Workspace.Id, CategoryId = CategoryId,
            Name = DeviceName.Trim(), PairCodeHash = MobileApi.Hash(PairCode), PairExpiresAtUtc = now.AddMinutes(10)
        });
        await db.SaveChangesAsync(ct);
        await LoadAsync(ct);
        return Page();
    }

    public async Task<IActionResult> OnPostRevokeAsync(Guid id, CancellationToken ct)
    {
        await db.MobileDevices.Where(x => x.Id == id && x.UserId == workspaceContext.UserId)
            .ExecuteUpdateAsync(s => s.SetProperty(x => x.Revoked, true).SetProperty(x => x.PairCodeHash, (string?)null), ct);
        TempData["Success"] = "دسترسی اتصال لغو شد. پیامک‌های صف این اتصال دیگر ثبت نمی‌شوند.";
        return RedirectToPage();
    }

    private async Task LoadAsync(CancellationToken ct)
    {
        Response.Headers.CacheControl = "no-store";
        Workspace = await workspaceContext.RequireCurrentAsync(ct);
        Categories = await db.ExpenseCategories.AsNoTracking().Where(x => x.WorkspaceId == Workspace.Id && !x.IsArchived).OrderBy(x => x.Name).ToListAsync(ct);
        Devices = await db.MobileDevices.AsNoTracking().Include(x => x.Workspace).Include(x => x.Category)
            .Where(x => x.UserId == workspaceContext.UserId).OrderByDescending(x => x.CreatedAtUtc).Take(100).ToListAsync(ct);
    }
}
