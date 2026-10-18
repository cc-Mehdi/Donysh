namespace Donysh.Auth.Security;

public static class SafeReturnUrl
{
    public static string Normalize(string? value) =>
        !string.IsNullOrWhiteSpace(value)
        && Uri.TryCreate(value, UriKind.Relative, out _)
        && value.StartsWith('/')
        && !value.StartsWith("//", StringComparison.Ordinal)
        && !value.StartsWith("/\\", StringComparison.Ordinal)
            ? value
            : "/";
}
