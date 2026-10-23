using System.ComponentModel.DataAnnotations;
using Donysh.Auth.Domain;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using Microsoft.AspNetCore.RateLimiting;

namespace Donysh.Auth.Pages.Account;

[Authorize]
[EnableRateLimiting("account")]
public sealed class ChangePasswordModel(UserManager<AuthUser> users, SignInManager<AuthUser> signInManager) : PageModel
{
    [BindProperty] public InputModel Input { get; set; } = new();

    public sealed class InputModel
    {
        [Required(ErrorMessage = "رمز فعلی را وارد کنید."), DataType(DataType.Password)]
        [Display(Name = "رمز فعلی")]
        public string CurrentPassword { get; set; } = string.Empty;

        [Required(ErrorMessage = "رمز تازه را وارد کنید."), StringLength(256, MinimumLength = 8), DataType(DataType.Password)]
        [Display(Name = "رمز تازه")]
        public string NewPassword { get; set; } = string.Empty;

        [Required(ErrorMessage = "تکرار رمز را وارد کنید."), Compare(nameof(NewPassword), ErrorMessage = "تکرار رمز یکسان نیست."), DataType(DataType.Password)]
        [Display(Name = "تکرار رمز تازه")]
        public string ConfirmPassword { get; set; } = string.Empty;
    }

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

    public async Task<IActionResult> OnPostAsync()
    {
        var user = await users.GetUserAsync(User);
        if (user is null || user.IsDeactivated)
        {
            await signInManager.SignOutAsync();
            return Challenge();
        }
        if (!ModelState.IsValid) return Page();
        var result = await users.ChangePasswordAsync(user, Input.CurrentPassword, Input.NewPassword);
        if (!result.Succeeded)
        {
            ModelState.AddModelError(string.Empty, result.Errors.Any(error => error.Code == "PasswordMismatch")
                ? "رمز فعلی صحیح نیست."
                : "رمز تازه باید دست‌کم ۸ نویسه، یک حرف لاتین کوچک و یک عدد داشته باشد.");
            return Page();
        }
        await signInManager.RefreshSignInAsync(user);
        return RedirectToPage("/Account/Manage", new { notice = "password" });
    }
}
