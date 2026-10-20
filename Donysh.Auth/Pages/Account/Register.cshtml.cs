using System.ComponentModel.DataAnnotations;
using System.Net;
using System.Text;
using Donysh.Auth.Configuration;
using Donysh.Auth.Domain;
using Donysh.Auth.Services;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using Microsoft.AspNetCore.RateLimiting;
using Microsoft.AspNetCore.WebUtilities;
using Microsoft.Extensions.Options;

namespace Donysh.Auth.Pages.Account;

[AllowAnonymous]
[EnableRateLimiting("account")]
public sealed class RegisterModel(
    UserManager<AuthUser> users,
    IAuthEmailSender emailSender,
    IOptions<AuthFeatureOptions> features,
    ILogger<RegisterModel> logger) : PageModel
{
    [BindProperty] public InputModel Input { get; set; } = new();
    public bool Available => features.Value.CanRegister && emailSender.IsConfigured;

    public sealed class InputModel
    {
        [Required(ErrorMessage = "نام را وارد کنید.")]
        [StringLength(100, MinimumLength = 2, ErrorMessage = "نام باید بین ۲ تا ۱۰۰ نویسه باشد.")]
        [Display(Name = "نام نمایشی")]
        public string DisplayName { get; set; } = string.Empty;

        [Required(ErrorMessage = "ایمیل را وارد کنید.")]
        [EmailAddress(ErrorMessage = "ایمیل معتبر نیست.")]
        [StringLength(254)]
        [Display(Name = "ایمیل")]
        public string Email { get; set; } = string.Empty;

        [Required(ErrorMessage = "رمز عبور را وارد کنید.")]
        [StringLength(256, MinimumLength = 8, ErrorMessage = "رمز عبور باید حداقل ۸ نویسه باشد.")]
        [DataType(DataType.Password)]
        [Display(Name = "رمز عبور")]
        public string Password { get; set; } = string.Empty;

        [DataType(DataType.Password)]
        [Compare(nameof(Password), ErrorMessage = "تکرار رمز عبور یکسان نیست.")]
        [Display(Name = "تکرار رمز عبور")]
        public string ConfirmPassword { get; set; } = string.Empty;
    }

    public IActionResult OnGet() => User.Identity?.IsAuthenticated == true
        ? RedirectToPage("/Index")
        : Page();

    public async Task<IActionResult> OnPostAsync(CancellationToken cancellationToken)
    {
        if (!Available) return NotFound();
        if (!ModelState.IsValid) return Page();

        var normalizedEmail = Input.Email.Trim().ToLowerInvariant();
        var user = new AuthUser
        {
            UserName = normalizedEmail,
            Email = normalizedEmail,
            DisplayName = Input.DisplayName.Trim(),
            EmailConfirmed = false
        };
        var result = await users.CreateAsync(user, Input.Password);
        if (!result.Succeeded)
        {
            foreach (var error in result.Errors)
                ModelState.AddModelError(string.Empty, Translate(error.Code));
            return Page();
        }

        try
        {
            var token = await users.GenerateEmailConfirmationTokenAsync(user);
            var code = WebEncoders.Base64UrlEncode(Encoding.UTF8.GetBytes(token));
            var callback = Url.Page("/Account/ConfirmEmail", null,
                new { userId = user.Id, code }, Request.Scheme)
                ?? throw new InvalidOperationException("Confirmation URL could not be created.");
            await emailSender.SendAsync(normalizedEmail, "تأیید حساب دُنیش",
                $"<p>برای تأیید حساب دُنیش، روی پیوند زیر بزنید:</p><p><a href=\"{WebUtility.HtmlEncode(callback)}\">تأیید حساب</a></p>",
                cancellationToken);
        }
        catch (Exception exception)
        {
            logger.LogWarning("Account confirmation delivery failed; the incomplete account is being removed.");
            logger.LogDebug(exception, "Account confirmation delivery details.");
            await users.DeleteAsync(user);
            ModelState.AddModelError(string.Empty, "ارسال پیام تأیید انجام نشد؛ کمی بعد دوباره تلاش کنید.");
            return Page();
        }

        return RedirectToPage("/Account/RegistrationSent");
    }

    private static string Translate(string code) => code switch
    {
        "DuplicateEmail" or "DuplicateUserName" => "امکان ساخت حساب با این مشخصات وجود ندارد.",
        "PasswordRequiresDigit" => "رمز عبور باید دست‌کم یک عدد داشته باشد.",
        "PasswordRequiresLower" => "رمز عبور باید دست‌کم یک حرف انگلیسی کوچک داشته باشد.",
        "PasswordTooShort" => "رمز عبور کوتاه است.",
        _ => "امکان ساخت حساب وجود نداشت؛ اطلاعات را بررسی کنید."
    };
}
