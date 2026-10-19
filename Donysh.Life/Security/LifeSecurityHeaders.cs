namespace Donysh.Life.Security;

public static class LifeSecurityHeaders
{
    public static IApplicationBuilder UseLifeSecurityHeaders(this IApplicationBuilder app) =>
        app.Use(async (context, next) =>
        {
            var headers = context.Response.Headers;
            headers.ContentSecurityPolicy = "default-src 'self'; base-uri 'none'; frame-ancestors 'none'; form-action 'self'; object-src 'none'; img-src 'self' data:; style-src 'self'; script-src 'none'; font-src 'self'; connect-src 'self'";
            headers.XContentTypeOptions = "nosniff";
            headers.XFrameOptions = "DENY";
            headers["Referrer-Policy"] = "strict-origin-when-cross-origin";
            headers["Permissions-Policy"] = "camera=(), microphone=(), geolocation=(), payment=(), usb=()";
            headers.Append("Cross-Origin-Opener-Policy", "same-origin");
            await next();
        });
}
