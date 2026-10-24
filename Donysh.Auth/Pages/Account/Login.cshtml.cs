using System.ComponentModel.DataAnnotations;
using Donysh.Auth.Configuration;
using Donysh.Auth.Domain;
using Donysh.Auth.Security;
using Donysh.Auth.Services;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using Microsoft.AspNetCore.RateLimiting;
using Microsoft.Extensions.Options;

namespace Donysh.Auth.Pages.Account;

[AllowAnonymous]
[EnableRateLimiting("account")]
public sealed class LoginModel(
    SignInManager<AuthUser> signInManager,
    UserManager<AuthUser> users,
    IOptions<AuthFeatureOptions> features,
    IAuthEmailSender emailSender) : PageModel
{
    [BindProperty] public InputModel Input { get; set; } = new();
    [BindProperty(SupportsGet = true)] public string? ReturnUrl { get; set; }

    public bool PasswordRecoveryAvailable => features.Value.CanRecoverPassword && emailSender.IsConfigured;
    public bool MigrationCompleted => features.Value.LegacyIdentityImportCompleted;

    public sealed class InputModel
    {
        [Required(ErrorMessage = "ایمیل یا نام کاربری را وارد کنید.")]
        [StringLength(254, ErrorMessage = "شناسهٔ ورود بیش از حد طولانی است.")]
        [Display(Name = "ایمیل یا نام کاربری")]
        public string Email { get; set; } = string.Empty;

        [Required(ErrorMessage = "رمز عبور را وارد کنید.")]
        [DataType(DataType.Password)]
        [StringLength(256, ErrorMessage = "رمز عبور بیش از حد طولانی است.")]
        [Display(Name = "رمز عبور")]
        public string Password { get; set; } = string.Empty;

        public bool RememberMe { get; set; }
    }

    public IActionResult OnGet()
    {
        ReturnUrl = SafeReturnUrl.Normalize(ReturnUrl);
        if (User.Identity?.IsAuthenticated == true) return LocalRedirect(ReturnUrl);
        return Page();
    }

    public async Task<IActionResult> OnPostAsync()
    {
        ReturnUrl = SafeReturnUrl.Normalize(ReturnUrl);
        if (!ModelState.IsValid) return Page();

        var identifier = Input.Email.Trim();
        var user = identifier.Contains('@')
            ? await users.FindByEmailAsync(identifier)
            : await users.FindByNameAsync(identifier);
        if (user is null)
        {
            ModelState.AddModelError(string.Empty, "ایمیل، نام کاربری یا رمز عبور صحیح نیست.");
            return Page();
        }
        if (user.IsDeactivated)
        {
            var reactivation = await signInManager.CheckPasswordSignInAsync(user, Input.Password, lockoutOnFailure: true);
            if (reactivation.Succeeded)
            {
                user.IsDeactivated = false;
                var updated = await users.UpdateAsync(user);
                if (updated.Succeeded)
                {
                    await signInManager.SignInAsync(user, Input.RememberMe);
                    return LocalRedirect(ReturnUrl);
                }
            }
            ModelState.AddModelError(string.Empty, reactivation.IsLockedOut
                ? "به دلیل تلاش‌های ناموفق، ورود این حساب موقتاً متوقف شده است."
                : "ایمیل، نام کاربری یا رمز عبور صحیح نیست.");
            return Page();
        }
        var result = await signInManager.PasswordSignInAsync(user, Input.Password, Input.RememberMe, lockoutOnFailure: true);
        if (result.Succeeded) return LocalRedirect(ReturnUrl);
        ModelState.AddModelError(string.Empty, result.IsLockedOut
            ? "به دلیل تلاش‌های ناموفق، ورود این حساب موقتاً متوقف شده است."
            : result.IsNotAllowed
                ? "این حساب هنوز برای ورود آماده نیست."
                : "ایمیل، نام کاربری یا رمز عبور صحیح نیست.");
        return Page();
    }
}
