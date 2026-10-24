using System.ComponentModel.DataAnnotations;
using System.Text;
using Donysh.Auth.Configuration;
using Donysh.Auth.Domain;
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
public sealed class ResetPasswordModel(UserManager<AuthUser> users, IOptions<AuthFeatureOptions> features) : PageModel
{
    [BindProperty] public InputModel Input { get; set; } = new();

    public sealed class InputModel
    {
        [Required(ErrorMessage = "نشانی ایمیل در پیوند بازیابی یافت نشد.")] public string Email { get; set; } = string.Empty;
        [Required(ErrorMessage = "کد بازیابی در پیوند یافت نشد.")] public string Code { get; set; } = string.Empty;
        [Required(ErrorMessage = "رمز عبور را وارد کنید.")]
        [StringLength(256, MinimumLength = 8, ErrorMessage = "رمز عبور باید حداقل ۸ نویسه باشد.")]
        [DataType(DataType.Password)]
        [Display(Name = "رمز عبور جدید")]
        public string Password { get; set; } = string.Empty;
        [Required(ErrorMessage = "تکرار رمز عبور را وارد کنید.")]
        [DataType(DataType.Password)]
        [Compare(nameof(Password), ErrorMessage = "تکرار رمز عبور یکسان نیست.")]
        [Display(Name = "تکرار رمز عبور")]
        public string ConfirmPassword { get; set; } = string.Empty;
    }

    public IActionResult OnGet(string? email, string? code)
    {
        if (!features.Value.CanRecoverPassword || string.IsNullOrWhiteSpace(email) || string.IsNullOrWhiteSpace(code))
            return NotFound();
        Input.Email = email;
        Input.Code = code;
        return Page();
    }

    public async Task<IActionResult> OnPostAsync()
    {
        if (!features.Value.CanRecoverPassword) return NotFound();
        if (!ModelState.IsValid) return Page();
        var user = await users.FindByEmailAsync(Input.Email.Trim());
        if (user is null) return RedirectToPage("/Account/ResetPasswordDone");
        try
        {
            var token = Encoding.UTF8.GetString(WebEncoders.Base64UrlDecode(Input.Code));
            var result = await users.ResetPasswordAsync(user, token, Input.Password);
            if (result.Succeeded) return RedirectToPage("/Account/ResetPasswordDone");
        }
        catch (FormatException) { }
        ModelState.AddModelError(string.Empty, "پیوند بازیابی معتبر نیست یا منقضی شده است.");
        return Page();
    }
}
