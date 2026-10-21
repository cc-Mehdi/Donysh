using System.ComponentModel.DataAnnotations;
using System.Text;
using Donysh.Auth.Configuration;
using Donysh.Auth.Domain;
using Donysh.Auth.Security;
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
[EnableRateLimiting("registration")]
public sealed class ResendConfirmationModel(
    UserManager<AuthUser> users,
    IAuthEmailSender emailSender,
    IOptions<AuthFeatureOptions> features,
    ILogger<ResendConfirmationModel> logger) : PageModel
{
    [BindProperty]
    [Required(ErrorMessage = "ایمیل را وارد کنید.")]
    [EmailAddress(ErrorMessage = "ایمیل معتبر نیست.")]
    [StringLength(254, ErrorMessage = "ایمیل بیش از حد طولانی است.")]
    [Display(Name = "ایمیل")]
    public string Email { get; set; } = string.Empty;

    [BindProperty(SupportsGet = true)] public string? ReturnUrl { get; set; }
    public bool Available => features.Value.CanRegister && emailSender.IsConfigured;

    public IActionResult OnGet()
    {
        ReturnUrl = SafeReturnUrl.Normalize(ReturnUrl);
        return User.Identity?.IsAuthenticated == true ? LocalRedirect(ReturnUrl) : Page();
    }

    public async Task<IActionResult> OnPostAsync(CancellationToken cancellationToken)
    {
        ReturnUrl = SafeReturnUrl.Normalize(ReturnUrl);
        if (!Available) return NotFound();
        if (!ModelState.IsValid) return Page();

        var user = await users.FindByEmailAsync(Email.Trim());
        if (user is not null && !user.EmailConfirmed && user.Email is not null)
        {
            try
            {
                var token = await users.GenerateEmailConfirmationTokenAsync(user);
                var code = WebEncoders.Base64UrlEncode(Encoding.UTF8.GetBytes(token));
                var callback = Url.Page("/Account/ConfirmEmail", null,
                    new { userId = user.Id, code, returnUrl = ReturnUrl }, Request.Scheme);
                if (callback is not null)
                    await emailSender.SendAsync(user.Email, "تأیید حساب دُنیش",
                        AuthEmailTemplates.ConfirmAccount(user.DisplayName, callback), cancellationToken);
            }
            catch (Exception exception)
            {
                logger.LogWarning("Account confirmation redelivery failed.");
                logger.LogDebug(exception, "Account confirmation redelivery details.");
            }
        }

        return RedirectToPage("/Account/RegistrationSent", new { returnUrl = ReturnUrl });
    }
}
