using HesabYar.Web.Domain;

namespace HesabYar.Web.Services;

public interface IWorkspaceContext
{
    Task<Workspace?> GetCurrentAsync(CancellationToken cancellationToken = default);
    Task<Workspace> RequireCurrentAsync(CancellationToken cancellationToken = default);
    Task<IReadOnlyList<Workspace>> GetAccessibleAsync(CancellationToken cancellationToken = default);
    Task<bool> CanAccessAsync(Guid workspaceId, CancellationToken cancellationToken = default);
    Task<bool> IsOwnerAsync(Guid workspaceId, CancellationToken cancellationToken = default);
    Task<bool> SetCurrentAsync(Guid workspaceId, CancellationToken cancellationToken = default);
    string? UserId { get; }
}
