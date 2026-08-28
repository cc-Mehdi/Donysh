using System.ComponentModel.DataAnnotations;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using HesabYar.Web.Helpers;
using HesabYar.Web.Services;
using Microsoft.AspNetCore.DataProtection;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;

namespace HesabYar.Web.Pages.Ai;

public sealed class IndexModel(
    AiWorkspaceService aiWorkspaceService,
    IWorkspaceContext workspaceContext,
    IDataProtectionProvider dataProtectionProvider) : PageModel
{
    private readonly IDataProtector _protector = dataProtectionProvider.CreateProtector("Donysh.AiChangePreview.v1");

    [BindProperty]
    [StringLength(AiWorkspaceService.MaxJsonLength, ErrorMessage = "حجم JSON بیشتر از حد مجاز است.")]
    public string ChangesJson { get; set; } = string.Empty;

    [BindProperty]
    public string PreviewToken { get; set; } = string.Empty;

    [BindProperty]
    public List<string> SelectedIds { get; set; } = [];

    [BindProperty]
    public string ReportPeriod { get; set; } = string.Empty;

    public string WorkspaceName { get; private set; } = string.Empty;
    public IReadOnlyList<AiReportPeriod> AvailablePeriods { get; private set; } = [];
    public string UserPrompt => AiWorkspaceService.SuggestedUserMessage;
    public IReadOnlyList<AiChangePreview> PreviewItems { get; private set; } = [];
    public bool EmptyPreview { get; private set; }
    public bool PreviewSubmitted { get; private set; }

    public async Task OnGetAsync(CancellationToken cancellationToken)
    {
        await LoadWorkspaceAsync(cancellationToken);
    }

    public async Task<IActionResult> OnPostDownloadAsync(CancellationToken cancellationToken)
    {
        await LoadWorkspaceAsync(cancellationToken, setDefaultPeriod: false);
        var selected = AvailablePeriods.SingleOrDefault(x => x.Value == ReportPeriod);
        if (selected is null)
        {
            ModelState.AddModelError(nameof(ReportPeriod), "ماه انتخاب‌شده معتبر نیست یا داده‌ای ندارد.");
            return Page();
        }

        var workspace = await workspaceContext.RequireCurrentAsync(cancellationToken);
        var period = new PersianYearMonth(selected.Year, selected.Month);
        var content = await aiWorkspaceService.BuildExportAsync(workspace.Id, period, cancellationToken);
        var fileName = $"donysh-ai-context-{selected.Value}-{DateTime.UtcNow:yyyyMMdd-HHmm}.json";
        return File(Encoding.UTF8.GetBytes(content), "application/json; charset=utf-8", fileName);
    }

    public async Task<IActionResult> OnPostPreviewAsync(CancellationToken cancellationToken)
    {
        PreviewSubmitted = true;
        await LoadWorkspaceAsync(cancellationToken);
        if (!ModelState.IsValid)
        {
            return Page();
        }

        try
        {
            var workspace = await workspaceContext.RequireCurrentAsync(cancellationToken);
            var preview = await aiWorkspaceService.PreviewAsync(workspace.Id, ChangesJson, null, cancellationToken);
            PreviewItems = preview.Items;
            EmptyPreview = PreviewItems.Count == 0;
            ChangesJson = preview.NormalizedJson;
            SelectedIds = PreviewItems.Where(x => x.IsValid).Select(x => x.Id).ToList();
            PreviewToken = Protect(new PreviewEnvelope(
                Guid.NewGuid(),
                workspace.Id,
                workspaceContext.UserId!,
                DateTimeOffset.UtcNow.AddMinutes(20),
                preview.NormalizedJson));
        }
        catch (Exception ex) when (ex is JsonException or InvalidOperationException)
        {
            ModelState.AddModelError(nameof(ChangesJson), ex.Message);
        }

        return Page();
    }

    public async Task<IActionResult> OnPostApplyAsync(CancellationToken cancellationToken)
    {
        await LoadWorkspaceAsync(cancellationToken);
        var workspace = await workspaceContext.RequireCurrentAsync(cancellationToken);
        PreviewEnvelope envelope;
        try
        {
            envelope = Unprotect(PreviewToken);
        }
        catch (Exception ex) when (ex is CryptographicException or JsonException or InvalidOperationException)
        {
            ModelState.AddModelError(string.Empty, "پیش‌نمایش معتبر نیست یا منقضی شده است. JSON را دوباره preview کنید.");
            return Page();
        }

        if (envelope.ExpiresAtUtc < DateTimeOffset.UtcNow ||
            envelope.WorkspaceId != workspace.Id ||
            !string.Equals(envelope.UserId, workspaceContext.UserId, StringComparison.Ordinal))
        {
            ModelState.AddModelError(string.Empty, "پیش‌نمایش متعلق به این کاربر و فضای فعال نیست یا منقضی شده است.");
            return Page();
        }

        if (SelectedIds.Count > AiWorkspaceService.MaxChanges || SelectedIds.Any(x => x.Length > 64))
        {
            ModelState.AddModelError(string.Empty, "فهرست تغییرات انتخاب‌شده معتبر نیست.");
            return Page();
        }

        var selected = SelectedIds.ToHashSet(StringComparer.Ordinal);
        var result = await aiWorkspaceService.ApplyAsync(envelope.RequestId, workspace.Id, workspaceContext.UserId!, envelope.Json, selected, cancellationToken);
        if (!result.Succeeded)
        {
            foreach (var error in result.Errors)
            {
                ModelState.AddModelError(string.Empty, error);
            }

            var preview = await aiWorkspaceService.PreviewAsync(workspace.Id, envelope.Json, null, cancellationToken);
            PreviewItems = preview.Items;
            ChangesJson = preview.NormalizedJson;
            PreviewToken = Protect(envelope with { ExpiresAtUtc = DateTimeOffset.UtcNow.AddMinutes(20) });
            return Page();
        }

        TempData["Success"] = $"{result.AppliedCount} تغییر تأییدشده با موفقیت روی فضای «{workspace.Name}» اعمال شد.";
        return RedirectToPage();
    }

    private async Task LoadWorkspaceAsync(CancellationToken cancellationToken, bool setDefaultPeriod = true)
    {
        var workspace = await workspaceContext.RequireCurrentAsync(cancellationToken);
        WorkspaceName = workspace.Name;
        AvailablePeriods = await aiWorkspaceService.GetAvailablePeriodsAsync(workspace.Id, cancellationToken);
        if (setDefaultPeriod && AvailablePeriods.All(x => x.Value != ReportPeriod))
        {
            var current = PersianCalendarHelper.GetYearMonth(DateOnly.FromDateTime(DateTime.Now));
            ReportPeriod = AvailablePeriods
                .FirstOrDefault(x => x.Year == current.Year && x.Month == current.Month)?.Value
                ?? AvailablePeriods.First().Value;
        }
    }

    private string Protect(PreviewEnvelope envelope)
        => _protector.Protect(JsonSerializer.Serialize(envelope));

    private PreviewEnvelope Unprotect(string token)
    {
        if (string.IsNullOrWhiteSpace(token) || token.Length > 200_000)
        {
            throw new InvalidOperationException("Invalid preview token.");
        }
        return JsonSerializer.Deserialize<PreviewEnvelope>(_protector.Unprotect(token))
            ?? throw new InvalidOperationException("Invalid preview token.");
    }

    private sealed record PreviewEnvelope(Guid RequestId, Guid WorkspaceId, string UserId, DateTimeOffset ExpiresAtUtc, string Json);
}
