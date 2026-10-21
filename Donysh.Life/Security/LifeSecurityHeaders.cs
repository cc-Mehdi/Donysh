namespace Donysh.Life.Security;

public static class LifeSecurityHeaders
{
    public static string BuildContentSecurityPolicy(Uri centralAuthority, Uri financeProduct)
    {
        var trustedAuthority = centralAuthority is { Scheme: "https" or "http" }
            ? centralAuthority.GetLeftPart(UriPartial.Authority)
            : throw new ArgumentException("The central authority must use HTTP or HTTPS.", nameof(centralAuthority));
        var trustedFinance = financeProduct is { Scheme: "https" or "http" }
            ? financeProduct.GetLeftPart(UriPartial.Authority)
            : throw new ArgumentException("The Finance product must use HTTP or HTTPS.", nameof(financeProduct));
        return "default-src 'self'; base-uri 'none'; frame-ancestors 'none'; " +
            $"form-action 'self' {trustedAuthority} {trustedFinance}; object-src 'none'; img-src 'self' data:; " +
            "style-src 'self'; script-src 'self'; font-src 'self'; connect-src 'self'";
    }

    public static IApplicationBuilder UseLifeSecurityHeaders(
        this IApplicationBuilder app,
        Uri centralAuthority,
        Uri financeProduct) =>
        app.Use(async (context, next) =>
        {
            var headers = context.Response.Headers;
            headers.ContentSecurityPolicy = BuildContentSecurityPolicy(centralAuthority, financeProduct);
            headers.XContentTypeOptions = "nosniff";
            headers.XFrameOptions = "DENY";
            headers["Referrer-Policy"] = "strict-origin-when-cross-origin";
            headers["Permissions-Policy"] = "camera=(), microphone=(), geolocation=(), payment=(), usb=()";
            headers.Append("Cross-Origin-Opener-Policy", "same-origin");
            headers.Append("Cross-Origin-Resource-Policy", "same-origin");
            headers.Append("X-Permitted-Cross-Domain-Policies", "none");
            await next();
        });
}
