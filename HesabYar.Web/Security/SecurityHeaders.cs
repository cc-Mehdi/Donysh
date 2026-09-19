namespace HesabYar.Web.Security;

public static class SecurityHeaders
{
    public const string ContentSecurityPolicy =
        "default-src 'self'; base-uri 'self'; frame-ancestors 'none'; form-action 'self'; " +
        "object-src 'none'; img-src 'self' data:; font-src 'self'; " +
        "style-src 'self' 'unsafe-inline'; script-src 'self'; connect-src 'self'; upgrade-insecure-requests";

    public static IApplicationBuilder UseDonyshSecurityHeaders(this IApplicationBuilder app) =>
        app.Use(async (context, next) =>
        {
            var headers = context.Response.Headers;
            headers.XContentTypeOptions = "nosniff";
            headers.XFrameOptions = "DENY";
            headers["Referrer-Policy"] = "strict-origin-when-cross-origin";
            headers["Permissions-Policy"] = "camera=(), microphone=(), geolocation=()";
            headers["Cross-Origin-Opener-Policy"] = "same-origin";
            headers["X-Permitted-Cross-Domain-Policies"] = "none";
            headers.ContentSecurityPolicy = context.Request.IsHttps
                ? $"{ContentSecurityPolicy}; upgrade-insecure-requests"
                : ContentSecurityPolicy;
            await next();
        });
}
