using System.ComponentModel.DataAnnotations;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using HesabYar.Web.Domain;
using HesabYar.Web.Helpers;
using HesabYar.Web.Services;
using Microsoft.AspNetCore.DataProtection;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;

namespace HesabYar.Web.Pages.Ai;

public sealed class IndexModel(
    AiWorkspaceService aiWorkspaceService,
    IWorkspaceContext workspaceContext,
    IDataProtectionProvider dataProtectionProvider,
    ILogger<IndexModel> logger) : PageModel
{
    private const int MaxWorkspaceReports = 10;
    private const int MaxReportRangeDays = 1095;
    private readonly IDataProtector _protector = dataProtectionProvider.CreateProtector("Donysh.AiChangePreview.v1");

    [BindProperty]
    [StringLength(AiWorkspaceService.MaxJsonLength, ErrorMessage = "حجم JSON بیشتر از حد مجاز است.")]
    public string? ChangesJson { get; set; }

    [BindProperty]
    public string? PreviewToken { get; set; }

    [BindProperty]
    public List<string> SelectedIds { get; set; } = [];

    [BindProperty]
    public List<WorkspaceReportInput> ReportWorkspaces { get; set; } = [];

    [BindProperty]
    public Guid? TargetWorkspaceId { get; set; }

    public IReadOnlyList<Workspace> AccessibleWorkspaces { get; private set; } = [];
    public string TargetWorkspaceName { get; private set; } = string.Empty;
    public string UserPrompt => AiWorkspaceService.SuggestedUserMessage;
    public IReadOnlyList<AiChangePreview> PreviewItems { get; private set; } = [];
    public bool EmptyPreview { get; private set; }
    public bool PreviewSubmitted { get; private set; }

    public async Task OnGetAsync(CancellationToken cancellationToken)
    {
        await LoadPageAsync(cancellationToken, initializeReportRows: true);
    }

    public async Task<IActionResult> OnPostDownloadAsync(CancellationToken cancellationToken)
    {
        await LoadPageAsync(cancellationToken, initializeReportRows: false);
        var requests = ValidateReportRequests();
        MergeReportRows();
        if (!ModelState.IsValid) return Page();

        var content = await aiWorkspaceService.BuildMultiWorkspaceExportAsync(requests, cancellationToken);
        var fileName = $"donysh-ai-context-{requests.Count}-spaces-{DateTime.UtcNow:yyyyMMdd-HHmm}.json";
        return File(Encoding.UTF8.GetBytes(content), "application/json; charset=utf-8", fileName);
    }

    public async Task<IActionResult> OnPostPreviewAsync(CancellationToken cancellationToken)
    {
        PreviewSubmitted = true;
        await LoadPageAsync(cancellationToken, initializeReportRows: false);
        MergeReportRows();
        var target = ResolveTargetWorkspace();
        if (!ModelState.IsValid || target is null)
        {
            return Page();
        }

        try
        {
            var preview = await aiWorkspaceService.PreviewAsync(target.Id, ChangesJson ?? string.Empty, null, cancellationToken);
            PreviewItems = preview.Items;
            EmptyPreview = PreviewItems.Count == 0;
            ChangesJson = preview.NormalizedJson;
            SelectedIds = PreviewItems.Where(x => x.IsValid).Select(x => x.Id).ToList();
            PreviewToken = Protect(new PreviewEnvelope(
                Guid.NewGuid(),
                target.Id,
                workspaceContext.UserId!,
                DateTimeOffset.UtcNow.AddMinutes(20),
                preview.NormalizedJson));
            TargetWorkspaceName = target.Name;
        }
        catch (Exception ex) when (ex is JsonException or InvalidOperationException)
        {
            ModelState.AddModelError(nameof(ChangesJson), ex.Message);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "AI change preview failed for user {UserId} and workspace {WorkspaceId}", workspaceContext.UserId, target.Id);
            ModelState.AddModelError(
                nameof(ChangesJson),
                "ساخت پیش‌نمایش به خطای موقت خورد. هیچ تغییری اعمال نشده است؛ دوباره تلاش کنید.");
        }

        return Page();
    }

    public async Task<IActionResult> OnPostApplyAsync(CancellationToken cancellationToken)
    {
        await LoadPageAsync(cancellationToken, initializeReportRows: false);
        MergeReportRows();
        PreviewEnvelope envelope;
        try
        {
            envelope = Unprotect(PreviewToken ?? string.Empty);
        }
        catch (Exception ex) when (ex is CryptographicException or JsonException or InvalidOperationException)
        {
            ModelState.AddModelError(string.Empty, "پیش‌نمایش معتبر نیست یا منقضی شده است. JSON را دوباره preview کنید.");
            return Page();
        }

        var canAccess = await workspaceContext.CanAccessAsync(envelope.WorkspaceId, cancellationToken);
        if (envelope.ExpiresAtUtc < DateTimeOffset.UtcNow ||
            TargetWorkspaceId != envelope.WorkspaceId ||
            !canAccess ||
            !string.Equals(envelope.UserId, workspaceContext.UserId, StringComparison.Ordinal))
        {
            ModelState.AddModelError(string.Empty, "پیش‌نمایش متعلق به این کاربر و فضای مقصد انتخاب‌شده نیست یا منقضی شده است.");
            return Page();
        }

        var workspace = AccessibleWorkspaces.Single(x => x.Id == envelope.WorkspaceId);
        TargetWorkspaceName = workspace.Name;

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

    public string WorkspaceTypeLabel(Workspace workspace)
        => workspace.Type == WorkspaceType.Personal ? "شخصی" : "اشتراکی";

    private async Task LoadPageAsync(CancellationToken cancellationToken, bool initializeReportRows)
    {
        AccessibleWorkspaces = await workspaceContext.GetAccessibleAsync(cancellationToken);
        var current = await workspaceContext.RequireCurrentAsync(cancellationToken);
        if (TargetWorkspaceId is null || AccessibleWorkspaces.All(x => x.Id != TargetWorkspaceId)) TargetWorkspaceId = current.Id;
        TargetWorkspaceName = AccessibleWorkspaces.FirstOrDefault(x => x.Id == TargetWorkspaceId)?.Name ?? current.Name;
        if (!initializeReportRows) return;

        var today = DateOnly.FromDateTime(DateTime.Now);
        var monthStart = PersianCalendarHelper.StartOfMonth(PersianCalendarHelper.GetYearMonth(today));
        ReportWorkspaces = AccessibleWorkspaces.Select(x => new WorkspaceReportInput
        {
            WorkspaceId = x.Id,
            IsSelected = x.Id == current.Id,
            StartDate = PersianCalendarHelper.ToInput(monthStart),
            EndDate = PersianCalendarHelper.ToInput(today)
        }).ToList();
    }

    private List<AiWorkspaceReportRequest> ValidateReportRequests()
    {
        var accessibleIds = AccessibleWorkspaces.Select(x => x.Id).ToHashSet();
        var selectedRows = ReportWorkspaces.Where(x => x.IsSelected).ToList();
        var requests = new List<AiWorkspaceReportRequest>(selectedRows.Count);
        if (selectedRows.Count == 0)
        {
            ModelState.AddModelError(nameof(ReportWorkspaces), "حداقل یک فضای مالی را برای گزارش انتخاب کنید.");
            return requests;
        }
        if (selectedRows.Count > MaxWorkspaceReports || selectedRows.Select(x => x.WorkspaceId).Distinct().Count() != selectedRows.Count)
        {
            ModelState.AddModelError(nameof(ReportWorkspaces), "فهرست فضاهای انتخاب‌شده معتبر نیست.");
            return requests;
        }

        foreach (var row in selectedRows)
        {
            var index = ReportWorkspaces.IndexOf(row);
            if (!accessibleIds.Contains(row.WorkspaceId))
            {
                ModelState.AddModelError(nameof(ReportWorkspaces), "به یکی از فضاهای انتخاب‌شده دسترسی ندارید.");
                continue;
            }
            if (!PersianCalendarHelper.TryParseInput(row.StartDate, out var start))
            {
                ModelState.AddModelError($"ReportWorkspaces[{index}].StartDate", "تاریخ شروع معتبر نیست.");
                continue;
            }
            if (!PersianCalendarHelper.TryParseInput(row.EndDate, out var end))
            {
                ModelState.AddModelError($"ReportWorkspaces[{index}].EndDate", "تاریخ پایان معتبر نیست.");
                continue;
            }
            if (end < start)
            {
                ModelState.AddModelError($"ReportWorkspaces[{index}].EndDate", "تاریخ پایان نباید قبل از تاریخ شروع باشد.");
                continue;
            }
            if (end.DayNumber - start.DayNumber > MaxReportRangeDays)
            {
                ModelState.AddModelError($"ReportWorkspaces[{index}].EndDate", "بازه هر فضا حداکثر سه سال است.");
                continue;
            }
            requests.Add(new AiWorkspaceReportRequest(row.WorkspaceId, start, end));
        }
        return requests;
    }

    private void MergeReportRows()
    {
        var submitted = ReportWorkspaces.GroupBy(x => x.WorkspaceId).ToDictionary(x => x.Key, x => x.First());
        var today = DateOnly.FromDateTime(DateTime.Now);
        var monthStart = PersianCalendarHelper.StartOfMonth(PersianCalendarHelper.GetYearMonth(today));
        ReportWorkspaces = AccessibleWorkspaces.Select(workspace => submitted.TryGetValue(workspace.Id, out var row) ? row : new WorkspaceReportInput
        {
            WorkspaceId = workspace.Id,
            StartDate = PersianCalendarHelper.ToInput(monthStart),
            EndDate = PersianCalendarHelper.ToInput(today)
        }).ToList();
    }

    private Workspace? ResolveTargetWorkspace()
    {
        if (TargetWorkspaceId is null)
        {
            ModelState.AddModelError(nameof(TargetWorkspaceId), "فضای مالی مقصد را انتخاب کنید.");
            return null;
        }
        var target = AccessibleWorkspaces.SingleOrDefault(x => x.Id == TargetWorkspaceId);
        if (target is null) ModelState.AddModelError(nameof(TargetWorkspaceId), "به فضای مالی مقصد دسترسی ندارید.");
        return target;
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

    public sealed class WorkspaceReportInput
    {
        public Guid WorkspaceId { get; set; }
        public bool IsSelected { get; set; }
        public string? StartDate { get; set; }
        public string? EndDate { get; set; }
    }

    private sealed record PreviewEnvelope(Guid RequestId, Guid WorkspaceId, string UserId, DateTimeOffset ExpiresAtUtc, string Json);
}
