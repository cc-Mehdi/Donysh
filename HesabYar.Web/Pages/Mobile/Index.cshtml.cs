using HesabYar.Web.Data;
using HesabYar.Web.Domain;
using HesabYar.Web.Helpers;
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
    public List<MobileDevice> ActiveDevices { get; private set; } = [];
    public List<MobileDevice> HistoryDevices { get; private set; } = [];
    public int HistoryCount { get; private set; }
    public List<string> DuplicateNames { get; private set; } = [];
    public static string TehranDate(DateTime utc)
    {
        var local = TimeZoneInfo.ConvertTimeFromUtc(DateTime.SpecifyKind(utc, DateTimeKind.Utc),
            TimeZoneInfo.FindSystemTimeZoneById("Asia/Tehran"));
        return $"{Formatters.PersianDate(DateOnly.FromDateTime(local))}، {local:HH:mm}";
    }
    public async Task OnGetAsync(CancellationToken ct) => await LoadAsync(ct);

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
        await workspaceContext.RequireCurrentAsync(ct);
        var now = DateTime.UtcNow;
        var devices = db.MobileDevices.AsNoTracking().Include(x => x.Workspace).Include(x => x.Category)
            .Where(x => x.UserId == workspaceContext.UserId);
        ActiveDevices = await devices.Where(x => !x.Revoked && x.TokenHash != null)
            .OrderByDescending(x => x.CreatedAtUtc).ToListAsync(ct);
        var history = devices.Where(x => x.Revoked || x.TokenHash == null
            && (x.PairCodeHash == null || x.PairExpiresAtUtc <= now));
        HistoryCount = await history.CountAsync(ct);
        HistoryDevices = await history.OrderByDescending(x => x.CreatedAtUtc).Take(20).ToListAsync(ct);
        DuplicateNames = ActiveDevices.GroupBy(x => x.Name.Trim(), StringComparer.OrdinalIgnoreCase)
            .Where(x => x.Count() > 1).Select(x => x.Key).ToList();
    }
}
