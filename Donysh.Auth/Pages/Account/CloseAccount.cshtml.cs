using Donysh.Auth.Domain;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using Microsoft.AspNetCore.RateLimiting;

namespace Donysh.Auth.Pages.Account;

[Authorize]
[EnableRateLimiting("account")]
public sealed class CloseAccountModel(UserManager<AuthUser> users, SignInManager<AuthUser> signInManager,
    IConfiguration configuration) : PageModel
{
    public async Task<IActionResult> OnGetAsync()
    {
        var user = await users.GetUserAsync(User);
        if (user is null || user.IsDeactivated)
        {
            await signInManager.SignOutAsync();
            return Challenge();
        }
        return Page();
    }

    public async Task<IActionResult> OnPostDeactivateAsync(string? password, bool confirmDeactivate)
    {
        var user = await users.GetUserAsync(User);
        if (user is null || user.IsDeactivated)
        {
            await signInManager.SignOutAsync();
            return Challenge();
        }
        if (!confirmDeactivate || string.IsNullOrWhiteSpace(password)
            || !(await signInManager.CheckPasswordSignInAsync(user, password, lockoutOnFailure: true)).Succeeded)
        {
            ModelState.AddModelError(string.Empty, "برای غیرفعال‌سازی، تأیید و رمز عبور صحیح لازم است.");
            return Page();
        }
        user.IsDeactivated = true;
        var result = await users.UpdateAsync(user);
        if (!result.Succeeded)
        {
            ModelState.AddModelError(string.Empty, "غیرفعال‌سازی انجام نشد؛ دوباره تلاش کنید.");
            return Page();
        }
        await users.UpdateSecurityStampAsync(user);
        await signInManager.SignOutAsync();
        Response.Headers.CacheControl = "no-store, no-cache";
        return Redirect(FederatedSignOutUrl.Create(configuration));
    }

    public async Task<IActionResult> OnPostDeleteAsync(string? password, bool confirmDelete)
    {
        var user = await users.GetUserAsync(User);
        if (user is null || user.IsDeactivated)
        {
            await signInManager.SignOutAsync();
            return Challenge();
        }
        if (!confirmDelete || string.IsNullOrWhiteSpace(password)
            || !(await signInManager.CheckPasswordSignInAsync(user, password, lockoutOnFailure: true)).Succeeded)
        {
            ModelState.AddModelError(string.Empty, "برای حذف، تأیید و رمز عبور صحیح لازم است.");
            return Page();
        }
        var result = await users.DeleteAsync(user);
        if (!result.Succeeded)
        {
            ModelState.AddModelError(string.Empty, "حذف حساب انجام نشد؛ دوباره تلاش کنید.");
            return Page();
        }
        await signInManager.SignOutAsync();
        Response.Headers.CacheControl = "no-store, no-cache";
        return Redirect(FederatedSignOutUrl.Create(configuration));
    }
}
