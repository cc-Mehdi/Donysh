namespace Donysh.Life.Security;

public static class SessionStatusEndpoint
{
    public static IResult Handle(HttpContext context)
    {
        context.Response.Headers.CacheControl = "no-store, no-cache";
        context.Response.Headers.Pragma = "no-cache";
        return context.User.Identity?.IsAuthenticated == true
            ? TypedResults.NoContent()
            : TypedResults.Unauthorized();
    }
}
