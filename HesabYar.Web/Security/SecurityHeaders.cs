namespace HesabYar.Web.Security;

public static class SecurityHeaders
{
    private const string PolicyBeforeFormAction =
        "default-src 'self'; base-uri 'self'; frame-ancestors 'none'; form-action 'self'";
    private const string PolicyAfterFormAction =
        "; object-src 'none'; img-src 'self' data:; font-src 'self'; " +
        "style-src 'self' 'unsafe-inline'; script-src 'self'; connect-src 'self'";

    public static string BuildContentSecurityPolicy(Uri? centralAuthority = null)
    {
        var trustedAuthority = centralAuthority is { Scheme: "https" or "http" }
            ? $" {centralAuthority.GetLeftPart(UriPartial.Authority)}"
            : string.Empty;
        return PolicyBeforeFormAction + trustedAuthority + PolicyAfterFormAction;
    }

    public static IApplicationBuilder UseDonyshSecurityHeaders(
        this IApplicationBuilder app,
        Uri? centralAuthority = null) =>
        app.Use(async (context, next) =>
        {
            var headers = context.Response.Headers;
            headers.XContentTypeOptions = "nosniff";
            headers.XFrameOptions = "DENY";
            headers["Referrer-Policy"] = "strict-origin-when-cross-origin";
            headers["Permissions-Policy"] = "camera=(), microphone=(), geolocation=()";
            headers["Cross-Origin-Opener-Policy"] = "same-origin";
            headers["X-Permitted-Cross-Domain-Policies"] = "none";
            var contentSecurityPolicy = BuildContentSecurityPolicy(centralAuthority);
            headers.ContentSecurityPolicy = context.Request.IsHttps
                ? $"{contentSecurityPolicy}; upgrade-insecure-requests"
                : contentSecurityPolicy;
            await next();
        });
}
