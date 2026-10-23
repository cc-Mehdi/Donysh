using System.ComponentModel.DataAnnotations;
using System.Text;
using Donysh.Auth.Configuration;
using Donysh.Auth.Domain;
using Donysh.Auth.Services;
using Donysh.Auth.Security;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using Microsoft.AspNetCore.RateLimiting;
using Microsoft.AspNetCore.WebUtilities;
using Microsoft.Extensions.Options;

namespace Donysh.Auth.Pages.Account;

[AllowAnonymous]
[EnableRateLimiting("registration")]
public sealed class RegisterModel(
    UserManager<AuthUser> users,
    IAuthEmailSender emailSender,
    IOptions<AuthFeatureOptions> features,
    ILogger<RegisterModel> logger) : PageModel
{
    [BindProperty] public InputModel Input { get; set; } = new();
    [BindProperty(SupportsGet = true)] public string? ReturnUrl { get; set; }
    public bool Available => features.Value.CanRegister && emailSender.IsConfigured;

    public sealed class InputModel
    {
        [Required(ErrorMessage = "نام را وارد کنید.")]
        [StringLength(100, MinimumLength = 2, ErrorMessage = "نام باید بین ۲ تا ۱۰۰ نویسه باشد.")]
        [Display(Name = "نام")]
        public string FirstName { get; set; } = string.Empty;

        [Required(ErrorMessage = "نام خانوادگی را وارد کنید.")]
        [StringLength(100, MinimumLength = 2, ErrorMessage = "نام خانوادگی باید بین ۲ تا ۱۰۰ نویسه باشد.")]
        [Display(Name = "نام خانوادگی")]
        public string LastName { get; set; } = string.Empty;

        [Required(ErrorMessage = "شماره تماس را وارد کنید.")]
        [RegularExpression(@"^(?:\+98|0)9[0-9]{9}$", ErrorMessage = "شماره همراه را به صورت 09xxxxxxxxx یا +989xxxxxxxxx وارد کنید.")]
        [Display(Name = "شماره تماس")]
        public string PhoneNumber { get; set; } = string.Empty;

        [Required(ErrorMessage = "ایمیل را وارد کنید.")]
        [EmailAddress(ErrorMessage = "ایمیل معتبر نیست.")]
        [StringLength(254)]
        [Display(Name = "ایمیل")]
        public string Email { get; set; } = string.Empty;

        [Required(ErrorMessage = "نام کاربری را وارد کنید.")]
        [RegularExpression(@"^[A-Za-z][A-Za-z0-9._-]{2,29}$", ErrorMessage = "نام کاربری باید ۳ تا ۳۰ نویسه و با حرف لاتین آغاز شود.")]
        [Display(Name = "نام کاربری")]
        public string UserName { get; set; } = string.Empty;

        [Required(ErrorMessage = "رمز عبور را وارد کنید.")]
        [StringLength(256, MinimumLength = 8, ErrorMessage = "رمز عبور باید حداقل ۸ نویسه باشد.")]
        [DataType(DataType.Password)]
        [Display(Name = "رمز عبور")]
        public string Password { get; set; } = string.Empty;

        [Required(ErrorMessage = "تکرار رمز عبور را وارد کنید.")]
        [DataType(DataType.Password)]
        [Compare(nameof(Password), ErrorMessage = "تکرار رمز عبور یکسان نیست.")]
        [Display(Name = "تکرار رمز عبور")]
        public string ConfirmPassword { get; set; } = string.Empty;
    }

    public IActionResult OnGet()
    {
        ReturnUrl = SafeReturnUrl.Normalize(ReturnUrl);
        return User.Identity?.IsAuthenticated == true
            ? LocalRedirect(ReturnUrl)
            : Page();
    }

    public async Task<IActionResult> OnPostAsync(CancellationToken cancellationToken)
    {
        ReturnUrl = SafeReturnUrl.Normalize(ReturnUrl);
        if (!Available) return NotFound();
        if (!ModelState.IsValid) return Page();

        var normalizedEmail = Input.Email.Trim().ToLowerInvariant();
        var user = new AuthUser
        {
            UserName = Input.UserName.Trim(),
            Email = normalizedEmail,
            FirstName = Input.FirstName.Trim(),
            LastName = Input.LastName.Trim(),
            DisplayName = $"{Input.FirstName.Trim()} {Input.LastName.Trim()}",
            PhoneNumber = Input.PhoneNumber.Trim(),
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
                new { userId = user.Id, code, returnUrl = ReturnUrl }, Request.Scheme)
                ?? throw new InvalidOperationException("Confirmation URL could not be created.");
            await emailSender.SendAsync(normalizedEmail, "تأیید حساب دُنیش",
                AuthEmailTemplates.ConfirmAccount(user.DisplayName, callback),
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

        return RedirectToPage("/Account/RegistrationSent", new { returnUrl = ReturnUrl });
    }

    private static string Translate(string code) => code switch
    {
        "DuplicateEmail" => "این ایمیل پیش‌تر ثبت شده است.",
        "DuplicateUserName" => "این نام کاربری پیش‌تر ثبت شده است.",
        "PasswordRequiresDigit" => "رمز عبور باید دست‌کم یک عدد داشته باشد.",
        "PasswordRequiresLower" => "رمز عبور باید دست‌کم یک حرف لاتین کوچک داشته باشد.",
        "PasswordTooShort" => "رمز عبور کوتاه است.",
        _ => "امکان ساخت حساب وجود نداشت؛ اطلاعات را بررسی کنید."
    };
}
