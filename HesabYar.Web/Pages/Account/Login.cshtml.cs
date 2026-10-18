using System.ComponentModel.DataAnnotations;
using HesabYar.Web.Configuration;
using HesabYar.Web.Domain;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using Microsoft.AspNetCore.RateLimiting;
using Microsoft.Extensions.Options;

namespace HesabYar.Web.Pages.Account;

[AllowAnonymous]
[EnableRateLimiting("account")]
public sealed class LoginModel(
    SignInManager<ApplicationUser> signInManager,
    IOptions<CentralAuthOptions> centralAuthOptions) : PageModel
{
    private readonly CentralAuthOptions centralAuth = centralAuthOptions.Value;

    [BindProperty]
    public InputModel Input { get; set; } = new();

    [BindProperty(SupportsGet = true)]
    public string? ReturnUrl { get; set; }

    [BindProperty(SupportsGet = true)]
    public bool AuthError { get; set; }

    public bool UsesCentralAuth => centralAuth.Enabled;

    public sealed class InputModel
    {
        [Required(ErrorMessage = "ایمیل را وارد کنید.")]
        [EmailAddress(ErrorMessage = "ایمیل معتبر نیست.")]
        [Display(Name = "ایمیل")]
        public string Email { get; set; } = string.Empty;

        [Required(ErrorMessage = "رمز عبور را وارد کنید.")]
        [DataType(DataType.Password)]
        [Display(Name = "رمز عبور")]
        public string Password { get; set; } = string.Empty;

        public bool RememberMe { get; set; }
    }

    public IActionResult OnGet()
    {
        if (User.Identity?.IsAuthenticated == true)
        {
            return RedirectToPage("/Index");
        }

        if (centralAuth.Enabled && !AuthError)
        {
            return ChallengeCentralAuth();
        }

        return Page();
    }

    public async Task<IActionResult> OnPostAsync()
    {
        if (centralAuth.Enabled)
        {
            return ChallengeCentralAuth();
        }

        ReturnUrl ??= Url.Content("~/");
        if (!ModelState.IsValid)
        {
            return Page();
        }

        var email = Input.Email.Trim().ToLowerInvariant();
        var result = await signInManager.PasswordSignInAsync(email, Input.Password, Input.RememberMe, lockoutOnFailure: true);

        if (result.Succeeded)
        {
            return LocalRedirect(ReturnUrl);
        }

        if (result.IsLockedOut)
        {
            ModelState.AddModelError(string.Empty, "به دلیل تلاش‌های ناموفق، حساب شما موقتاً قفل شده است.");
            return Page();
        }

        ModelState.AddModelError(string.Empty, "ایمیل یا رمز عبور صحیح نیست.");
        return Page();
    }

    public IActionResult OnPostCentralAsync() => ChallengeCentralAuth();

    private ChallengeResult ChallengeCentralAuth()
    {
        var destination = !string.IsNullOrWhiteSpace(ReturnUrl) && Url.IsLocalUrl(ReturnUrl)
            ? ReturnUrl
            : Url.Content("~/");
        return Challenge(
            new AuthenticationProperties { RedirectUri = destination },
            CentralAuthOptions.Scheme);
    }
}
