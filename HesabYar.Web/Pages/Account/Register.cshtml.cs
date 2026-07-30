using System.ComponentModel.DataAnnotations;
using HesabYar.Web.Data;
using HesabYar.Web.Domain;
using HesabYar.Web.Helpers;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;

namespace HesabYar.Web.Pages.Account;

[AllowAnonymous]
public sealed class RegisterModel(
    UserManager<ApplicationUser> userManager,
    SignInManager<ApplicationUser> signInManager,
    ApplicationDbContext db) : PageModel
{
    [BindProperty]
    public InputModel Input { get; set; } = new();

    [BindProperty(SupportsGet = true)]
    public string? ReturnUrl { get; set; }

    public sealed class InputModel
    {
        [Required(ErrorMessage = "نام را وارد کنید.")]
        [StringLength(100, MinimumLength = 2, ErrorMessage = "نام باید بین ۲ تا ۱۰۰ کاراکتر باشد.")]
        [Display(Name = "نام نمایشی")]
        public string DisplayName { get; set; } = string.Empty;

        [Required(ErrorMessage = "ایمیل را وارد کنید.")]
        [EmailAddress(ErrorMessage = "ایمیل معتبر نیست.")]
        [Display(Name = "ایمیل")]
        public string Email { get; set; } = string.Empty;

        [Required(ErrorMessage = "رمز عبور را وارد کنید.")]
        [StringLength(100, MinimumLength = 8, ErrorMessage = "رمز عبور باید حداقل ۸ کاراکتر باشد.")]
        [DataType(DataType.Password)]
        [Display(Name = "رمز عبور")]
        public string Password { get; set; } = string.Empty;

        [DataType(DataType.Password)]
        [Compare(nameof(Password), ErrorMessage = "تکرار رمز عبور یکسان نیست.")]
        [Display(Name = "تکرار رمز عبور")]
        public string ConfirmPassword { get; set; } = string.Empty;
    }

    public IActionResult OnGet()
    {
        if (User.Identity?.IsAuthenticated == true)
        {
            return RedirectToPage("/Index");
        }

        return Page();
    }

    public async Task<IActionResult> OnPostAsync()
    {
        ReturnUrl ??= Url.Content("~/");
        if (!ModelState.IsValid)
        {
            return Page();
        }

        var email = Input.Email.Trim().ToLowerInvariant();
        var user = new ApplicationUser
        {
            UserName = email,
            Email = email,
            DisplayName = Input.DisplayName.Trim(),
            EmailConfirmed = true
        };

        var result = await userManager.CreateAsync(user, Input.Password);
        if (!result.Succeeded)
        {
            foreach (var error in result.Errors)
            {
                ModelState.AddModelError(string.Empty, TranslateIdentityError(error.Code));
            }
            return Page();
        }

        try
        {
            var workspace = new Workspace
            {
                Name = $"فضای شخصی {user.DisplayName}",
                Type = WorkspaceType.Personal,
                OwnerUserId = user.Id
            };
            db.Workspaces.Add(workspace);
            db.WorkspaceMembers.Add(new WorkspaceMember
            {
                WorkspaceId = workspace.Id,
                UserId = user.Id,
                Role = WorkspaceRole.Owner
            });
            db.ExpenseCategories.AddRange(DefaultCategories.For(workspace.Id));
            await db.SaveChangesAsync();
        }
        catch
        {
            await userManager.DeleteAsync(user);
            throw;
        }

        await signInManager.SignInAsync(user, isPersistent: false);
        return LocalRedirect(ReturnUrl);
    }

    private static string TranslateIdentityError(string code) => code switch
    {
        "DuplicateEmail" or "DuplicateUserName" => "این ایمیل قبلاً ثبت شده است.",
        "PasswordRequiresDigit" => "رمز عبور باید حداقل یک عدد داشته باشد.",
        "PasswordTooShort" => "رمز عبور کوتاه است.",
        "PasswordRequiresLower" => "رمز عبور باید حداقل یک حرف انگلیسی کوچک داشته باشد.",
        _ => "امکان ساخت حساب وجود نداشت. اطلاعات را بررسی کنید."
    };
}
