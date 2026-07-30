using System.Security.Claims;
using HesabYar.Web.Data;
using HesabYar.Web.Domain;
using Microsoft.EntityFrameworkCore;

namespace HesabYar.Web.Services;

public sealed class WorkspaceContext(
    ApplicationDbContext db,
    IHttpContextAccessor httpContextAccessor) : IWorkspaceContext
{
    private const string ActiveWorkspaceCookie = "hesabyar.active-workspace";
    private Workspace? _cachedCurrent;
    private IReadOnlyList<Workspace>? _cachedAccessible;

    public string? UserId => httpContextAccessor.HttpContext?.User.FindFirstValue(ClaimTypes.NameIdentifier);

    public async Task<Workspace?> GetCurrentAsync(CancellationToken cancellationToken = default)
    {
        if (_cachedCurrent is not null)
        {
            return _cachedCurrent;
        }

        var userId = UserId;
        if (string.IsNullOrWhiteSpace(userId))
        {
            return null;
        }

        var context = httpContextAccessor.HttpContext;
        if (context?.Request.Cookies.TryGetValue(ActiveWorkspaceCookie, out var rawId) == true
            && Guid.TryParse(rawId, out var workspaceId))
        {
            _cachedCurrent = await db.WorkspaceMembers
                .Where(x => x.UserId == userId && x.WorkspaceId == workspaceId)
                .Select(x => x.Workspace)
                .AsNoTracking()
                .SingleOrDefaultAsync(cancellationToken);
        }

        if (_cachedCurrent is null)
        {
            _cachedCurrent = await db.WorkspaceMembers
                .Where(x => x.UserId == userId)
                .OrderBy(x => x.Workspace.Type)
                .ThenBy(x => x.Workspace.CreatedAtUtc)
                .Select(x => x.Workspace)
                .AsNoTracking()
                .FirstOrDefaultAsync(cancellationToken);
        }

        return _cachedCurrent;
    }

    public async Task<Workspace> RequireCurrentAsync(CancellationToken cancellationToken = default)
        => await GetCurrentAsync(cancellationToken)
           ?? throw new InvalidOperationException("No accessible workspace exists for the current user.");

    public async Task<IReadOnlyList<Workspace>> GetAccessibleAsync(CancellationToken cancellationToken = default)
    {
        if (_cachedAccessible is not null)
        {
            return _cachedAccessible;
        }

        var userId = UserId;
        if (string.IsNullOrWhiteSpace(userId))
        {
            return [];
        }

        _cachedAccessible = await db.WorkspaceMembers
            .Where(x => x.UserId == userId)
            .OrderBy(x => x.Workspace.Type)
            .ThenBy(x => x.Workspace.Name)
            .Select(x => x.Workspace)
            .AsNoTracking()
            .ToListAsync(cancellationToken);

        return _cachedAccessible;
    }

    public Task<bool> CanAccessAsync(Guid workspaceId, CancellationToken cancellationToken = default)
    {
        var userId = UserId;
        return string.IsNullOrWhiteSpace(userId)
            ? Task.FromResult(false)
            : db.WorkspaceMembers.AnyAsync(x => x.UserId == userId && x.WorkspaceId == workspaceId, cancellationToken);
    }

    public Task<bool> IsOwnerAsync(Guid workspaceId, CancellationToken cancellationToken = default)
    {
        var userId = UserId;
        return string.IsNullOrWhiteSpace(userId)
            ? Task.FromResult(false)
            : db.WorkspaceMembers.AnyAsync(x => x.UserId == userId && x.WorkspaceId == workspaceId && x.Role == WorkspaceRole.Owner, cancellationToken);
    }

    public async Task<bool> SetCurrentAsync(Guid workspaceId, CancellationToken cancellationToken = default)
    {
        if (!await CanAccessAsync(workspaceId, cancellationToken))
        {
            return false;
        }

        var context = httpContextAccessor.HttpContext;
        context?.Response.Cookies.Append(ActiveWorkspaceCookie, workspaceId.ToString(), new CookieOptions
        {
            HttpOnly = true,
            IsEssential = true,
            SameSite = SameSiteMode.Lax,
            Secure = context.Request.IsHttps,
            Expires = DateTimeOffset.UtcNow.AddYears(1)
        });

        _cachedCurrent = null;
        _cachedAccessible = null;
        return true;
    }
}
