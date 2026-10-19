namespace Donysh.Auth.Security;

public static class AuthSecurityHeaders
{
    public static string BuildContentSecurityPolicy(IEnumerable<string> redirectUris)
    {
        var trustedOrigins = redirectUris
            .Select(value => Uri.TryCreate(value, UriKind.Absolute, out var uri) ? uri : null)
            .Where(uri => uri is { Scheme: "https" or "http" })
            .Select(uri => uri!.GetLeftPart(UriPartial.Authority))
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .Order(StringComparer.OrdinalIgnoreCase);
        var formActions = string.Join(' ', new[] { "'self'" }.Concat(trustedOrigins));
        return $"default-src 'self'; base-uri 'self'; frame-ancestors 'none'; form-action {formActions}; " +
            "object-src 'none'; img-src 'self' data:; font-src 'self'; style-src 'self'; " +
            "script-src 'self'; connect-src 'self'";
    }

    public static IApplicationBuilder UseAuthSecurityHeaders(
        this IApplicationBuilder app,
        IEnumerable<string> redirectUris) =>
        app.Use(async (context, next) =>
        {
            context.Response.OnStarting(() =>
            {
                var headers = context.Response.Headers;
                headers.XContentTypeOptions = "nosniff";
                headers.XFrameOptions = "DENY";
                headers["Referrer-Policy"] = "strict-origin-when-cross-origin";
                var contentSecurityPolicy = BuildContentSecurityPolicy(redirectUris);
                headers.ContentSecurityPolicy = context.Request.IsHttps
                    ? contentSecurityPolicy + "; upgrade-insecure-requests"
                    : contentSecurityPolicy;
                headers["Permissions-Policy"] = "camera=(), microphone=(), geolocation=(), payment=(), usb=()";
                headers["Cross-Origin-Opener-Policy"] = "same-origin";
                headers["X-Permitted-Cross-Domain-Policies"] = "none";
                if (context.User.Identity?.IsAuthenticated == true
                    || context.Request.Path.StartsWithSegments("/connect"))
                {
                    headers.CacheControl = "no-store, no-cache";
                    headers.Pragma = "no-cache";
                }
                return Task.CompletedTask;
            });
            await next();
        });
}
