namespace HesabYar.Web.Security;

public static class AuthenticationPrefetchGuard
{
    private static readonly PathString[] SensitivePaths =
    [
        new("/Account/Login"),
        new("/signin-oidc")
    ];

    public static bool ShouldSkip(HttpRequest request)
    {
        if (!HttpMethods.IsGet(request.Method) && !HttpMethods.IsHead(request.Method)) return false;

        var purpose = $"{request.Headers["Sec-Purpose"]} {request.Headers["Purpose"]}";
        var speculative = purpose.Contains("prefetch", StringComparison.OrdinalIgnoreCase)
            || purpose.Contains("prerender", StringComparison.OrdinalIgnoreCase);
        return speculative && SensitivePaths.Any(path => request.Path.StartsWithSegments(path));
    }

    public static async Task InvokeAsync(HttpContext context, RequestDelegate next)
    {
        if (!ShouldSkip(context.Request))
        {
            await next(context);
            return;
        }

        context.Response.StatusCode = StatusCodes.Status204NoContent;
        context.Response.Headers.CacheControl = "no-store, no-cache";
        context.Response.Headers.Pragma = "no-cache";
        context.Response.Headers.Vary = "Sec-Purpose, Purpose";
    }
}
