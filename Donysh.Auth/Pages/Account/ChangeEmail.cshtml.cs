using System.ComponentModel.DataAnnotations;
using System.Text;
using Donysh.Auth.Domain;
using Donysh.Auth.Services;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using Microsoft.AspNetCore.RateLimiting;
using Microsoft.AspNetCore.WebUtilities;

namespace Donysh.Auth.Pages.Account;

[Authorize]
[EnableRateLimiting("account")]
public sealed class ChangeEmailModel(UserManager<AuthUser> users, SignInManager<AuthUser> signInManager,
    IAuthEmailSender emailSender, ILogger<ChangeEmailModel> logger) : PageModel
{
    [BindProperty, Required(ErrorMessage = "ایمیل تازه را وارد کنید."), EmailAddress(ErrorMessage = "ایمیل معتبر نیست."), StringLength(254)]
    [Display(Name = "ایمیل تازه")]
    public string NewEmail { get; set; } = string.Empty;
    public string CurrentEmail { get; private set; } = string.Empty;
    public bool Sent { get; private set; }

    public async Task<IActionResult> OnGetAsync()
    {
        var user = await users.GetUserAsync(User);
        if (user is null || user.IsDeactivated)
        {
            await signInManager.SignOutAsync();
            return Challenge();
        }
        CurrentEmail = user.Email ?? string.Empty;
        return Page();
    }

    public async Task<IActionResult> OnPostAsync(CancellationToken cancellationToken)
    {
        var user = await users.GetUserAsync(User);
        if (user is null || user.IsDeactivated)
        {
            await signInManager.SignOutAsync();
            return Challenge();
        }
        CurrentEmail = user.Email ?? string.Empty;
        if (!ModelState.IsValid) return Page();
        if (!emailSender.IsConfigured)
        {
            ModelState.AddModelError(string.Empty, "تغییر ایمیل تا آماده‌شدن سرویس ارسال پیام در دسترس نیست.");
            return Page();
        }
        var email = NewEmail.Trim().ToLowerInvariant();
        if (string.Equals(email, user.Email, StringComparison.OrdinalIgnoreCase))
        {
            ModelState.AddModelError(nameof(NewEmail), "این نشانی هم‌اکنون ایمیل حساب شماست.");
            return Page();
        }
        if (await users.FindByEmailAsync(email) is not null)
        {
            ModelState.AddModelError(nameof(NewEmail), "این ایمیل پیش‌تر ثبت شده است.");
            return Page();
        }
        var token = await users.GenerateChangeEmailTokenAsync(user, email);
        var code = WebEncoders.Base64UrlEncode(Encoding.UTF8.GetBytes(token));
        var callback = Url.Page("/Account/ChangeEmail", "Confirm", new { userId = user.Id, email, code }, Request.Scheme)
            ?? throw new InvalidOperationException("Email change URL could not be created.");
        try
        {
            await emailSender.SendAsync(email, "تأیید ایمیل تازهٔ دُنیش", AuthEmailTemplates.ChangeEmail(callback), cancellationToken);
            Sent = true;
        }
        catch (Exception exception)
        {
            logger.LogWarning("Email change confirmation delivery failed.");
            logger.LogDebug(exception, "Email change delivery details.");
            ModelState.AddModelError(string.Empty, "ارسال پیوند تأیید انجام نشد؛ کمی بعد دوباره تلاش کنید.");
        }
        return Page();
    }

    public async Task<IActionResult> OnGetConfirmAsync(string? userId, string? email, string? code)
    {
        var user = await users.GetUserAsync(User);
        if (user is null || user.IsDeactivated)
        {
            await signInManager.SignOutAsync();
            return Challenge();
        }
        CurrentEmail = user.Email ?? string.Empty;
        if (userId != user.Id || string.IsNullOrWhiteSpace(email) || string.IsNullOrWhiteSpace(code))
            return BadRequest();
        try
        {
            var token = Encoding.UTF8.GetString(WebEncoders.Base64UrlDecode(code));
            var legacyEmailUserName = string.Equals(user.UserName, user.Email, StringComparison.OrdinalIgnoreCase);
            if (legacyEmailUserName && await users.FindByNameAsync(email) is { } other && other.Id != user.Id)
            {
                ModelState.AddModelError(string.Empty, "این نشانی برای ورود حساب دیگری استفاده شده است.");
                return Page();
            }
            var result = await users.ChangeEmailAsync(user, email, token);
            if (result.Succeeded)
            {
                if (legacyEmailUserName)
                {
                    var renamed = await users.SetUserNameAsync(user, email);
                    if (!renamed.Succeeded)
                    {
                        ModelState.AddModelError(string.Empty, "ایمیل تغییر کرد، اما نام کاربری قبلی همچنان باقی است؛ آن را در مدیریت حساب ویرایش کنید.");
                        await signInManager.RefreshSignInAsync(user);
                        return Page();
                    }
                }
                await signInManager.RefreshSignInAsync(user);
                return RedirectToPage("/Account/Manage", new { notice = "email" });
            }
        }
        catch (FormatException) { }
        ModelState.AddModelError(string.Empty, "پیوند تأیید معتبر نیست یا منقضی شده است.");
        return Page();
    }
}
