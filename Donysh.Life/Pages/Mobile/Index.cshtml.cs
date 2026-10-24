using System.ComponentModel.DataAnnotations;
using System.Security.Claims;
using Donysh.Life.Data;
using Donysh.Life.Domain;
using Donysh.Life.Features.Mobile;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using Microsoft.EntityFrameworkCore;

namespace Donysh.Life.Pages.Mobile;

public sealed class IndexModel(LifeDbContext db) : PageModel
{
    [BindProperty, Required, StringLength(80)] public string DeviceName { get; set; } = string.Empty;
    public string? PairCode { get; private set; }
    public IReadOnlyList<LifeMobileDevice> ActiveDevices { get; private set; } = [];
    public IReadOnlyList<LifeMobileDevice> PendingDevices { get; private set; } = [];
    public IReadOnlyList<LifeMobileDevice> HistoryDevices { get; private set; } = [];
    private string Subject => User.FindFirstValue("sub") ?? throw new InvalidOperationException("شناسهٔ حساب پیدا نشد.");
    public static string TehranDate(DateTimeOffset utc) =>
        TimeZoneInfo.ConvertTime(utc, TimeZoneInfo.FindSystemTimeZoneById("Asia/Tehran")).ToString("yyyy/MM/dd HH:mm");

    public async Task OnGetAsync(CancellationToken ct) => await LoadAsync(ct);

    public async Task<IActionResult> OnPostAsync(CancellationToken ct)
    {
        if (DeviceName?.Any(char.IsControl) == true) ModelState.AddModelError(nameof(DeviceName), "نام گوشی معتبر نیست.");
        if (!ModelState.IsValid) { await LoadAsync(ct); return Page(); }
        var subject = Subject;
        var now = DateTimeOffset.UtcNow;
        if (await db.MobileDevices.CountAsync(item => item.UserSubject == subject && !item.Revoked
            && (item.TokenHash != null || item.PairCodeHash != null && item.PairExpiresAtUtc > now), ct) >= 3)
        {
            ModelState.AddModelError("", "حداکثر سه گوشی یا کد اتصال فعال مجاز است.");
            await LoadAsync(ct);
            return Page();
        }
        PairCode = LifeMobileApi.CreatePairCode();
        db.MobileDevices.Add(new LifeMobileDevice
        {
            UserSubject = subject, Name = DeviceName!.Trim(), PairCodeHash = LifeMobileApi.HashPairCode(PairCode),
            PairExpiresAtUtc = now.AddMinutes(10)
        });
        await db.SaveChangesAsync(ct);
        await LoadAsync(ct);
        return Page();
    }

    public async Task<IActionResult> OnPostRevokeAsync(Guid id, CancellationToken ct)
    {
        var subject = Subject;
        await db.MobileDevices.Where(item => item.Id == id && item.UserSubject == subject)
            .ExecuteUpdateAsync(setters => setters.SetProperty(item => item.Revoked, true)
                .SetProperty(item => item.PairCodeHash, (string?)null)
                .SetProperty(item => item.TokenHash, (string?)null), ct);
        TempData["MobileMessage"] = "دسترسی گوشی لغو شد.";
        return RedirectToPage();
    }

    private async Task LoadAsync(CancellationToken ct)
    {
        Response.Headers.CacheControl = "no-store";
        var subject = Subject;
        var now = DateTimeOffset.UtcNow;
        var devices = db.MobileDevices.AsNoTracking().Where(item => item.UserSubject == subject);
        ActiveDevices = await devices.Where(item => !item.Revoked && item.TokenHash != null)
            .OrderByDescending(item => item.CreatedAtUtc).ToListAsync(ct);
        PendingDevices = await devices.Where(item => !item.Revoked && item.PairCodeHash != null && item.PairExpiresAtUtc > now)
            .OrderByDescending(item => item.CreatedAtUtc).ToListAsync(ct);
        HistoryDevices = await devices.Where(item => item.Revoked || item.TokenHash == null && item.PairExpiresAtUtc <= now)
            .OrderByDescending(item => item.CreatedAtUtc).Take(20).ToListAsync(ct);
    }
}
