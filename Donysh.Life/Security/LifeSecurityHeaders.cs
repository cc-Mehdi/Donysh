namespace Donysh.Life.Security;

public static class LifeSecurityHeaders
{
    public static string BuildContentSecurityPolicy(Uri centralAuthority)
    {
        var trustedAuthority = centralAuthority is { Scheme: "https" or "http" }
            ? centralAuthority.GetLeftPart(UriPartial.Authority)
            : throw new ArgumentException("The central authority must use HTTP or HTTPS.", nameof(centralAuthority));
        return "default-src 'self'; base-uri 'none'; frame-ancestors 'none'; " +
            $"form-action 'self' {trustedAuthority}; object-src 'none'; img-src 'self' data:; " +
            "style-src 'self'; script-src 'self'; font-src 'self'; connect-src 'self'";
    }

    public static IApplicationBuilder UseLifeSecurityHeaders(
        this IApplicationBuilder app,
        Uri centralAuthority) =>
        app.Use(async (context, next) =>
        {
            var headers = context.Response.Headers;
            headers.ContentSecurityPolicy = BuildContentSecurityPolicy(centralAuthority);
            headers.XContentTypeOptions = "nosniff";
            headers.XFrameOptions = "DENY";
            headers["Referrer-Policy"] = "strict-origin-when-cross-origin";
            headers["Permissions-Policy"] = "camera=(), microphone=(), geolocation=(), payment=(), usb=()";
            headers.Append("Cross-Origin-Opener-Policy", "same-origin");
            await next();
        });
}
