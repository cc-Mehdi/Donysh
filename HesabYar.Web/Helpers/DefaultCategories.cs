using HesabYar.Web.Domain;

namespace HesabYar.Web.Helpers;

public static class DefaultCategories
{
    public static IReadOnlyList<ExpenseCategory> For(Guid workspaceId) =>
    [
        new() { WorkspaceId = workspaceId, Name = "خوراک", Icon = "🍽️", Color = "orange" },
        new() { WorkspaceId = workspaceId, Name = "حمل‌ونقل", Icon = "🚕", Color = "blue" },
        new() { WorkspaceId = workspaceId, Name = "تفریح", Icon = "🎮", Color = "violet" },
        new() { WorkspaceId = workspaceId, Name = "خرید", Icon = "🛍️", Color = "pink" },
        new() { WorkspaceId = workspaceId, Name = "قبوض", Icon = "🧾", Color = "amber" },
        new() { WorkspaceId = workspaceId, Name = "سلامت", Icon = "💊", Color = "emerald" },
        new() { WorkspaceId = workspaceId, Name = "سایر", Icon = "📦", Color = "slate" }
    ];
}
