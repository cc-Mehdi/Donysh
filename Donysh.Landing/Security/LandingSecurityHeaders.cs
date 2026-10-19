namespace Donysh.Landing.Security;

public static class LandingSecurityHeaders
{
    public static IApplicationBuilder UseLandingSecurityHeaders(this IApplicationBuilder app) =>
        app.Use(async (context, next) =>
        {
            context.Response.OnStarting(() =>
            {
                var headers = context.Response.Headers;
                var contentSecurityPolicy = string.Join(' ',
                    "default-src 'self';",
                    "base-uri 'self';",
                    "object-src 'none';",
                    "frame-ancestors 'none';",
                    "form-action 'self';",
                    "script-src 'none';",
                    "style-src 'self';",
                    "img-src 'self' data:;",
                    "font-src 'self';",
                    "connect-src 'self';");
                headers.ContentSecurityPolicy = context.Request.IsHttps
                    ? contentSecurityPolicy + " upgrade-insecure-requests;"
                    : contentSecurityPolicy;
                headers.XContentTypeOptions = "nosniff";
                headers.XFrameOptions = "DENY";
                headers["Referrer-Policy"] = "strict-origin-when-cross-origin";
                headers["Permissions-Policy"] = "camera=(), microphone=(), geolocation=(), payment=(), usb=()";
                headers["Cross-Origin-Opener-Policy"] = "same-origin";
                headers.Append("Cross-Origin-Resource-Policy", "same-origin");
                headers.Append("X-Permitted-Cross-Domain-Policies", "none");
                return Task.CompletedTask;
            });

            await next();
        });
}
