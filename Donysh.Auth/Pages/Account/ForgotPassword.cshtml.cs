using System.ComponentModel.DataAnnotations;
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
public sealed class ForgotPasswordModel(
    UserManager<AuthUser> users,
    IAuthEmailSender sender,
    IOptions<AuthFeatureOptions> features,
    ILogger<ForgotPasswordModel> logger) : PageModel
{
    [BindProperty]
    [Required(ErrorMessage = "ایمیل را وارد کنید.")]
    [EmailAddress(ErrorMessage = "ایمیل معتبر نیست.")]
    [StringLength(254)]
    [Display(Name = "ایمیل")]
    public string Email { get; set; } = string.Empty;

    public bool Available => features.Value.CanRecoverPassword && sender.IsConfigured;

    public IActionResult OnGet() => Page();

    public async Task<IActionResult> OnPostAsync(CancellationToken cancellationToken)
    {
        if (!Available) return NotFound();
        if (!ModelState.IsValid) return Page();

        var user = await users.FindByEmailAsync(Email.Trim());
        if (user is not null && user.EmailConfirmed && user.Email is not null)
        {
            try
            {
                var token = await users.GeneratePasswordResetTokenAsync(user);
                var code = WebEncoders.Base64UrlEncode(Encoding.UTF8.GetBytes(token));
                var callback = Url.Page("/Account/ResetPassword", null,
                    new { email = user.Email, code }, Request.Scheme);
                if (callback is not null)
                    await sender.SendAsync(user.Email, "بازیابی رمز عبور دُنیش",
                        AuthEmailTemplates.ResetPassword(callback),
                        cancellationToken);
            }
            catch (Exception exception)
            {
                logger.LogWarning("Password recovery delivery failed.");
                logger.LogDebug(exception, "Password recovery delivery details.");
            }
        }
        return RedirectToPage("/Account/RecoverySent");
    }
}
