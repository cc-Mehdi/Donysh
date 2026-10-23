using System.Net;

namespace Donysh.Auth.Services;

public static class AuthEmailTemplates
{
    public static string ConfirmAccount(string displayName, string callbackUrl) => Build(
        "تأیید حساب دُنیش",
        $"سلام {WebUtility.HtmlEncode(displayName)}،",
        "برای فعال‌شدن حساب دُنیش، ایمیل خود را تأیید کنید.",
        "تأیید ایمیل و فعال‌سازی حساب",
        callbackUrl,
        "اگر شما این حساب را نساخته‌اید، این پیام را نادیده بگیرید.");

    public static string ResetPassword(string callbackUrl) => Build(
        "بازیابی رمز عبور دُنیش",
        "درخواست بازیابی دریافت شد.",
        "برای تعیین رمز عبور جدید از دکمهٔ زیر استفاده کنید.",
        "تعیین رمز عبور جدید",
        callbackUrl,
        "اگر این درخواست را ثبت نکرده‌اید، نیازی به انجام کاری نیست.");

    public static string ChangeEmail(string callbackUrl) => Build(
        "تأیید ایمیل تازه",
        "درخواست تغییر ایمیل حساب دُنیش دریافت شد.",
        "برای ثبت نشانی تازه، پیوند زیر را باز کنید.",
        "تأیید ایمیل تازه",
        callbackUrl,
        "اگر شما این درخواست را ثبت نکرده‌اید، این پیام را نادیده بگیرید.");

    private static string Build(
        string title,
        string greeting,
        string message,
        string action,
        string callbackUrl,
        string footer)
    {
        var safeUrl = WebUtility.HtmlEncode(callbackUrl);
        return $$"""
            <!doctype html>
            <html lang="fa" dir="rtl">
            <body style="margin:0;background:#f1f5f9;font-family:Tahoma,Arial,sans-serif;color:#0f172a">
              <table role="presentation" width="100%" cellspacing="0" cellpadding="0" style="padding:32px 12px;background:#f1f5f9">
                <tr><td align="center">
                  <table role="presentation" width="100%" cellspacing="0" cellpadding="0" style="max-width:560px;background:#fff;border:1px solid #e2e8f0;border-radius:24px;overflow:hidden">
                    <tr><td style="height:8px;background:linear-gradient(90deg,#4f46e5,#14b8a6)"></td></tr>
                    <tr><td style="padding:32px">
                      <p style="margin:0 0 8px;color:#4f46e5;font-size:13px;font-weight:700">حساب مرکزی دُنیش</p>
                      <h1 style="margin:0 0 24px;font-size:25px">{{WebUtility.HtmlEncode(title)}}</h1>
                      <p style="margin:0 0 12px;line-height:1.9">{{greeting}}</p>
                      <p style="margin:0 0 24px;color:#475569;line-height:1.9">{{WebUtility.HtmlEncode(message)}}</p>
                      <p style="margin:0 0 28px"><a href="{{safeUrl}}" style="display:inline-block;padding:13px 22px;border-radius:12px;background:#4f46e5;color:#fff;text-decoration:none;font-weight:700">{{WebUtility.HtmlEncode(action)}}</a></p>
                      <p style="margin:0;padding-top:20px;border-top:1px solid #e2e8f0;color:#64748b;font-size:12px;line-height:1.8">{{WebUtility.HtmlEncode(footer)}}</p>
                    </td></tr>
                  </table>
                </td></tr>
              </table>
            </body>
            </html>
            """;
    }
}
