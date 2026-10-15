using System.ComponentModel.DataAnnotations;
using System.Security.Cryptography;
using HesabYar.Web.Data;
using HesabYar.Web.Domain;
using HesabYar.Web.Services;
using HesabYar.Web.Sms;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using Microsoft.AspNetCore.RateLimiting;
using Microsoft.EntityFrameworkCore;

namespace HesabYar.Web.Pages.Mobile;

[EnableRateLimiting("account")]
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
        if (DeviceName?.Any(char.IsControl) == true) ModelState.AddModelError(nameof(DeviceName), "نام اتصال نباید شامل نویسه‌های کنترلی باشد.");
        if (!ModelState.IsValid) return Page();
        var code = Convert.ToHexString(RandomNumberGenerator.GetBytes(16));
        var device = new MobileDevice {
            UserId = workspaceContext.UserId!, WorkspaceId = Workspace.Id, CategoryId = CategoryId,
            Name = DeviceName!.Trim(), PairCodeHash = MobileApi.Hash(code), PairExpiresAtUtc = DateTime.UtcNow.AddMinutes(10)
        };
        var created = await db.Database.CreateExecutionStrategy().ExecuteAsync(async () => {
            db.ChangeTracker.Clear();
            await using var tx = await db.Database.BeginTransactionAsync(ct);
            // The same per-user lock as imports serializes concurrent pairing submissions.
            await db.Database.ExecuteSqlInterpolatedAsync($"SELECT pg_advisory_xact_lock(hashtextextended({device.UserId}, 0))", ct);
            if (!await db.WorkspaceMembers.AnyAsync(x => x.WorkspaceId == device.WorkspaceId && x.UserId == device.UserId, ct)
                || !await db.ExpenseCategories.AnyAsync(x => x.Id == device.CategoryId && x.WorkspaceId == device.WorkspaceId && !x.IsArchived, ct))
            {
                ModelState.AddModelError("", "دسترسی به فضای مالی یا دسته تغییر کرده است؛ صفحه را تازه کنید.");
                return false;
            }
            // A commit whose acknowledgement was lost must not create another connection on retry.
            if (await db.MobileDevices.AnyAsync(x => x.Id == device.Id, ct)) return true;
            var now = DateTime.UtcNow;
            if (await db.MobileDevices.CountAsync(x => x.UserId == device.UserId && !x.Revoked && (x.TokenHash != null || x.PairExpiresAtUtc > now), ct) >= 3)
            {
                ModelState.AddModelError("", "حداکثر ۳ اتصال فعال؛ ابتدا یکی را لغو کنید.");
                return false;
            }
            db.MobileDevices.Add(device);
            await db.SaveChangesAsync(ct);
            await tx.CommitAsync(ct);
            return true;
        });
        if (created) PairCode = code;
        await LoadAsync(ct);
        return Page();
    }

    public async Task<IActionResult> OnPostRevokeAsync(Guid id, CancellationToken ct)
    {
        await db.MobileDevices.Where(x => x.Id == id && x.UserId == workspaceContext.UserId)
            .ExecuteUpdateAsync(s => s.SetProperty(x => x.Revoked, true).SetProperty(x => x.PairCodeHash, (string?)null)
                .SetProperty(x => x.TokenHash, (string?)null), ct);
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
