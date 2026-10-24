using System.Security.Claims;
using System.Text;
using System.Text.Json;
using Donysh.Life.Features.AiPlanning;
using Donysh.Life.Features.Activities;
using Microsoft.AspNetCore.DataProtection;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;

namespace Donysh.Life.Pages;

public sealed class AiModel(
    LifeAiPlanService plans,
    LifeAiContextExportService contextExport,
    LifeActivityService activities,
    IDataProtectionProvider protection) : PageModel
{
    private readonly IDataProtector _protector = protection.CreateProtector("Donysh.Life.AiPlanPreview.v1");
    [BindProperty] public string? JsonText { get; set; }
    [BindProperty] public IFormFile? JsonFile { get; set; }
    [BindProperty] public string? PreviewToken { get; set; }
    [BindProperty] public string Mode { get; set; } = "new";
    [BindProperty] public List<AiActivitySelection> Selections { get; set; } = [];
    public AiPlan? Preview { get; private set; }
    public string? Error { get; private set; }
    public string SuggestedPrompt => LifeAiPlanService.Prompt;
    public string ImprovementPrompt => LifeAiPlanService.ImprovementPrompt;
    public bool HasCurrentPlan { get; private set; }
    public static IReadOnlyList<DayOfWeek> Days { get; } =
        [DayOfWeek.Saturday, DayOfWeek.Sunday, DayOfWeek.Monday, DayOfWeek.Tuesday, DayOfWeek.Wednesday, DayOfWeek.Thursday, DayOfWeek.Friday];

    public async Task OnGetAsync(CancellationToken cancellationToken)
    {
        HasCurrentPlan = (await activities.GetOverviewAsync(cancellationToken)).ActiveActivityCount > 0;
        Mode = HasCurrentPlan ? "improve" : "new";
    }

    public async Task<IActionResult> OnGetExportAsync(CancellationToken cancellationToken)
    {
        Response.Headers.CacheControl = "private, no-store";
        var json = await contextExport.ExportAsync(cancellationToken);
        return File(json, "application/json; charset=utf-8", "donysh-life-current-plan.json");
    }

    public async Task<IActionResult> OnPostPreviewAsync(CancellationToken cancellationToken)
    {
        Mode = Mode == "improve" ? "improve" : "new";
        if (JsonFile is not null)
        {
            if (JsonFile.Length > LifeAiPlanService.MaxJsonLength * 4L)
            { Error = "حجم فایل JSON بیشتر از حد مجاز است."; return Page(); }
            if (!string.Equals(Path.GetExtension(JsonFile.FileName), ".json", StringComparison.OrdinalIgnoreCase))
            { Error = "فقط فایل با پسوند .json پذیرفته می‌شود."; return Page(); }
            using var reader = new StreamReader(JsonFile.OpenReadStream(), new UTF8Encoding(false, true));
            try { JsonText = await reader.ReadToEndAsync(cancellationToken); }
            catch (DecoderFallbackException) { Error = "فایل باید با کدگذاری UTF-8 باشد."; return Page(); }
        }
        var (plan, error) = LifeAiPlanService.Parse(JsonText);
        if (error is not null) { Error = error; return Page(); }
        Error = await plans.ValidateExistingTargetsAsync(plan!, cancellationToken);
        if (Error is not null) return Page();
        Preview = plan;
        Selections = plan!.Activities!.Select(activity => new AiActivitySelection
        {
            Included = true,
            Slots = activity.Slots!.Select(slot => new AiSlotSelection
            { Included = true, Day = slot.Day, Start = slot.Start, DurationMinutes = slot.DurationMinutes,
                ReminderMinutesBefore = slot.ReminderMinutesBefore }).ToList()
        }).ToList();
        PreviewToken = _protector.Protect(JsonSerializer.Serialize(new PreviewPayload(User.FindFirstValue("sub"), DateTimeOffset.UtcNow.AddMinutes(30), JsonText!, Mode)));
        JsonText = null;
        return Page();
    }

    public async Task<IActionResult> OnPostApplyAsync(CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(PreviewToken)) return BadRequest();
        PreviewPayload? payload;
        try { payload = JsonSerializer.Deserialize<PreviewPayload>(_protector.Unprotect(PreviewToken)); }
        catch (Exception ex) when (ex is System.Security.Cryptography.CryptographicException or JsonException)
        { Error = "اعتبار پیش‌نمایش تمام شده است؛ فایل را دوباره بارگذاری کنید."; return Page(); }
        if (payload is null || payload.Subject != User.FindFirstValue("sub") || payload.ExpiresAtUtc < DateTimeOffset.UtcNow)
        { Error = "اعتبار پیش‌نمایش تمام شده است؛ فایل را دوباره بارگذاری کنید."; return Page(); }
        Mode = payload.Mode;
        var (plan, error) = LifeAiPlanService.Parse(payload.Json);
        if (error is not null) { Error = error; return Page(); }
        Preview = plan;
        if (Selections.Count != plan!.Activities!.Count || Selections.Any(x => x.Slots is null))
        { Error = "پیش‌نمایش تغییر کرده است؛ فایل را دوباره بارگذاری کنید."; return Page(); }
        Error = await plans.ValidateAndApplyAsync(plan, Selections, cancellationToken);
        if (Error is not null) return Page();
        TempData["ScheduleMessage"] = plan.Activities.Any(activity => activity.ExistingActivityId.HasValue)
            ? "فعالیت‌ها و برنامهٔ هفتگی انتخاب‌شده به‌روزرسانی شدند."
            : "فعالیت‌ها و برنامهٔ هفتگی انتخاب‌شده ساخته شدند.";
        return RedirectToPage("/Week");
    }

    private sealed record PreviewPayload(string? Subject, DateTimeOffset ExpiresAtUtc, string Json, string Mode);
}
