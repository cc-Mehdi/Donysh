using System.ComponentModel.DataAnnotations;
using Donysh.Auth.Configuration;
using Donysh.Auth.Domain;
using Donysh.Auth.Security;
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
    IOptions<AuthFeatureOptions> features) : PageModel
{
    [BindProperty] public InputModel Input { get; set; } = new();
    [BindProperty(SupportsGet = true)] public string? ReturnUrl { get; set; }

    public bool RegistrationAvailable => features.Value.CanRegister;
    public bool PasswordRecoveryAvailable => features.Value.CanRecoverPassword;

    public sealed class InputModel
    {
        [Required(ErrorMessage = "ایمیل را وارد کنید.")]
        [EmailAddress(ErrorMessage = "ایمیل معتبر نیست.")]
        [StringLength(254, ErrorMessage = "ایمیل بیش از حد طولانی است.")]
        [Display(Name = "ایمیل")]
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

        var email = Input.Email.Trim().ToLowerInvariant();
        var result = await signInManager.PasswordSignInAsync(email, Input.Password, Input.RememberMe, lockoutOnFailure: true);
        if (result.Succeeded) return LocalRedirect(ReturnUrl);
        ModelState.AddModelError(string.Empty, result.IsLockedOut
            ? "به دلیل تلاش‌های ناموفق، ورود این حساب موقتاً متوقف شده است."
            : result.IsNotAllowed
                ? "این حساب هنوز برای ورود آماده نیست."
                : "ایمیل یا رمز عبور صحیح نیست.");
        return Page();
    }
}
