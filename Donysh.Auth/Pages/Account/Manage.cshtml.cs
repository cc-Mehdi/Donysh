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
public sealed class ManageModel(UserManager<AuthUser> users, SignInManager<AuthUser> signInManager) : PageModel
{
    [BindProperty] public ProfileInput Input { get; set; } = new();
    public string Email { get; private set; } = string.Empty;
    public DateTime CreatedAtUtc { get; private set; }
    public string? Notice { get; private set; }

    public sealed class ProfileInput
    {
        [Required(ErrorMessage = "نام را وارد کنید.")]
        [StringLength(100, MinimumLength = 2)]
        [Display(Name = "نام")]
        public string FirstName { get; set; } = string.Empty;

        [Required(ErrorMessage = "نام خانوادگی را وارد کنید.")]
        [StringLength(100, MinimumLength = 2)]
        [Display(Name = "نام خانوادگی")]
        public string LastName { get; set; } = string.Empty;

        [Required(ErrorMessage = "شماره تماس را وارد کنید.")]
        [RegularExpression(@"^(?:\+98|0)9[0-9]{9}$", ErrorMessage = "شماره همراه معتبر نیست.")]
        [Display(Name = "شماره تماس")]
        public string PhoneNumber { get; set; } = string.Empty;

        [Required(ErrorMessage = "نام کاربری را وارد کنید.")]
        [RegularExpression(@"^[A-Za-z][A-Za-z0-9._-]{2,29}$", ErrorMessage = "نام کاربری باید ۳ تا ۳۰ نویسه و با حرف لاتین آغاز شود.")]
        [Display(Name = "نام کاربری")]
        public string UserName { get; set; } = string.Empty;

        public bool NotificationsEnabled { get; set; }
    }

    public async Task<IActionResult> OnGetAsync(string? notice)
    {
        var user = await users.GetUserAsync(User);
        if (user is null || user.IsDeactivated)
        {
            await signInManager.SignOutAsync();
            return Challenge();
        }
        Input = new ProfileInput
        {
            FirstName = user.FirstName,
            LastName = user.LastName,
            PhoneNumber = user.PhoneNumber ?? string.Empty,
            UserName = user.UserName ?? string.Empty,
            NotificationsEnabled = user.NotificationsEnabled
        };
        Populate(user);
        Notice = notice switch
        {
            "profile" => "اطلاعات حساب ذخیره شد.",
            "email" => "ایمیل تازه تأیید و ثبت شد.",
            "password" => "رمز عبور تغییر کرد.",
            _ => null
        };
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
        Populate(user);
        if (!ModelState.IsValid) return Page();

        user.FirstName = Input.FirstName.Trim();
        user.LastName = Input.LastName.Trim();
        user.DisplayName = $"{user.FirstName} {user.LastName}";
        user.PhoneNumber = Input.PhoneNumber.Trim();
        user.UserName = Input.UserName.Trim();
        user.NotificationsEnabled = Input.NotificationsEnabled;
        var result = await users.UpdateAsync(user);
        if (!result.Succeeded)
        {
            foreach (var error in result.Errors)
                ModelState.AddModelError(string.Empty, error.Code == "DuplicateUserName"
                    ? "این نام کاربری پیش‌تر ثبت شده است."
                    : "اطلاعات حساب ذخیره نشد؛ ورودی‌ها را بررسی کنید.");
            return Page();
        }
        await signInManager.RefreshSignInAsync(user);
        return RedirectToPage(new { notice = "profile" });
    }

    private void Populate(AuthUser user)
    {
        Email = user.Email ?? string.Empty;
        CreatedAtUtc = user.CreatedAtUtc;
    }
}
