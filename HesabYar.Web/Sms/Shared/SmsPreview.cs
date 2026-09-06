using System.Globalization;

namespace Donysh.Sms;

// A pure local preview: no queue writes, networking or financial mutations.
public static class SmsPreview
{
    public const string MellatSample = "حساب5555555555\nبرداشت50,000,000\nمانده111,111,111\n05/06/15-12:14";
    public const string BluSample = "بلو\nبرداشت پول\nمهدی عزیز، 10,000,000 ریال از حساب شما پرید.\nموجودی: 11,111,111 ریال\n12:18\n1405.06.15";

    public static string Describe(string? text)
    {
        var parsed = BankSmsParser.Parse(text);
        if (parsed is null) return "قالب برداشت معتبر تشخیص داده نشد؛ هیچ خرجی ثبت یا ارسال نشده است.";
        var calendar = new PersianCalendar();
        var time = parsed.LocalDateTime;
        return $"بانک: {(parsed.Bank == "blu" ? "بلو" : "ملت")}\nمبلغ: {parsed.AmountToman.ToString("N0", CultureInfo.InvariantCulture)} تومان"
            + $"\nتاریخ: {calendar.GetYear(time)}/{calendar.GetMonth(time):00}/{calendar.GetDayOfMonth(time):00} ساعت {time:HH:mm}"
            + $"\nحساب: {parsed.Account ?? "در پیامک ذکر نشده"}\nفقط پیش‌نمایش محلی؛ چیزی به سرور ارسال نشد.";
    }
}
