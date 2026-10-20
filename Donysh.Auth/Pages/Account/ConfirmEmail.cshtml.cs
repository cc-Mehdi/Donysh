using System.Text;
using Donysh.Auth.Domain;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc.RazorPages;
using Microsoft.AspNetCore.WebUtilities;

namespace Donysh.Auth.Pages.Account;

[AllowAnonymous]
public sealed class ConfirmEmailModel(UserManager<AuthUser> users) : PageModel
{
    public bool Succeeded { get; private set; }
    public string Message { get; private set; } = "پیوند تأیید ناقص است.";

    public async Task OnGetAsync(string? userId, string? code)
    {
        if (string.IsNullOrWhiteSpace(userId) || string.IsNullOrWhiteSpace(code)) return;
        var user = await users.FindByIdAsync(userId);
        if (user is null) return;
        try
        {
            var token = Encoding.UTF8.GetString(WebEncoders.Base64UrlDecode(code));
            var result = await users.ConfirmEmailAsync(user, token);
            Succeeded = result.Succeeded;
            Message = result.Succeeded ? "اکنون می‌توانید وارد حساب مرکزی دُنیش شوید." : "این پیوند منقضی یا قبلاً استفاده شده است.";
        }
        catch (FormatException) { }
    }
}
