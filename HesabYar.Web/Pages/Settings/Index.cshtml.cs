using System.ComponentModel.DataAnnotations;
using HesabYar.Web.Domain;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;

namespace HesabYar.Web.Pages.Settings;

public sealed class IndexModel(UserManager<ApplicationUser> userManager) : PageModel
{
    [BindProperty]
    public ProfileInput Input { get; set; } = new();

    public string Email { get; private set; } = string.Empty;

    public sealed class ProfileInput
    {
        [Required(ErrorMessage = "نام کاربری را وارد کنید.")]
        [StringLength(100, MinimumLength = 2, ErrorMessage = "نام کاربری باید بین ۲ تا ۱۰۰ کاراکتر باشد.")]
        public string DisplayName { get; set; } = string.Empty;
    }

    public async Task<IActionResult> OnGetAsync()
    {
        var user = await userManager.GetUserAsync(User);
        if (user is null)
        {
            return Challenge();
        }

        Input.DisplayName = user.DisplayName;
        Email = user.Email ?? string.Empty;

        return Page();
    }

    public async Task<IActionResult> OnPostAsync()
    {
        var user = await userManager.GetUserAsync(User);
        if (user is null)
        {
            return Challenge();
        }

        Email = user.Email ?? string.Empty;

        if (!ModelState.IsValid)
        {
            return Page();
        }

        user.DisplayName = Input.DisplayName.Trim();

        var result = await userManager.UpdateAsync(user);
        if (!result.Succeeded)
        {
            ModelState.AddModelError(string.Empty, "امکان ذخیره نام کاربری وجود نداشت. دوباره تلاش کنید.");
            return Page();
        }

        TempData["Success"] = "نام کاربری با موفقیت ویرایش شد.";
        return RedirectToPage();
    }
}
