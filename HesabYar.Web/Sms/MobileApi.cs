using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Threading.RateLimiting;
using Donysh.Sms;
using HesabYar.Web.Data;
using HesabYar.Web.Domain;
using Microsoft.AspNetCore.Http.Features;
using Microsoft.EntityFrameworkCore;

namespace HesabYar.Web.Sms;

public static class MobileApi
{
    public static string Hash(string value) => Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(value)));
    public static string NewSecret() => Convert.ToHexString(RandomNumberGenerator.GetBytes(32));

    public static void AddMobileApi(this IServiceCollection services)
    {
        services.AddRateLimiter(o => {
            o.RejectionStatusCode = 429;
            o.OnRejected = (ctx, _) => { ctx.HttpContext.Response.Headers.RetryAfter = "60"; return ValueTask.CompletedTask; };
            o.AddPolicy("mobile", _ => RateLimitPartition.GetFixedWindowLimiter("mobile-global", _ => new() {
                PermitLimit = 60, Window = TimeSpan.FromMinutes(1), QueueLimit = 0 }));
            o.GlobalLimiter = PartitionedRateLimiter.Create<HttpContext, string>(ctx =>
                ctx.Request.Path.StartsWithSegments("/api/mobile")
                    ? RateLimitPartition.GetConcurrencyLimiter("mobile", _ => new() { PermitLimit = 2, QueueLimit = 0 })
                    : RateLimitPartition.GetNoLimiter("web"));
        });
    }

    public static void MapMobileApi(this WebApplication app)
    {
        var group = app.MapGroup("/api/mobile").RequireRateLimiting("mobile");
        group.MapPost("/pair", PairAsync);
        group.MapPost("/sms", ImportAsync);
    }

    // Manual bounded JSON read avoids model-binding large bodies before device validation.
    private static async Task<T?> ReadAsync<T>(HttpContext ctx, CancellationToken ct)
    {
        if (ctx.Request.ContentLength > 32768)
        {
            // Do not reuse a connection with an unread oversized body.
            if (ctx.Request.Protocol == "HTTP/1.1") ctx.Response.Headers.Connection = "close";
            return default;
        }
        var feature = ctx.Features.Get<IHttpMaxRequestBodySizeFeature>();
        if (feature is { IsReadOnly: false }) feature.MaxRequestBodySize = 32768;
        if (!ctx.Request.HasJsonContentType()) return default;
        using var buffer = new MemoryStream();
        var chunk = new byte[4096];
        int count;
        while ((count = await ctx.Request.Body.ReadAsync(chunk, ct)) != 0)
        {
            if (buffer.Length + count > 32768) return default;
            buffer.Write(chunk, 0, count);
        }
        try { return JsonSerializer.Deserialize<T>(buffer.ToArray(), new JsonSerializerOptions(JsonSerializerDefaults.Web) { MaxDepth = 8 }); }
        catch (JsonException) { return default; }
    }

    private static async Task<IResult> PairAsync(HttpContext ctx, ApplicationDbContext db, CancellationToken ct)
    {
        ctx.Response.Headers.CacheControl = "no-store";
        if (!ctx.Request.IsHttps) return Results.BadRequest(new { error = "HTTPS required" });
        var input = await ReadAsync<PairRequest>(ctx, ct);
        if (input?.Code is not { Length: 32 }) return Results.BadRequest(new { error = "Invalid pairing code" });
        var hash = Hash(input.Code.ToUpperInvariant());
        var now = DateTime.UtcNow;
        var device = await db.MobileDevices.AsNoTracking().Include(x => x.Workspace).Include(x => x.Category)
            .SingleOrDefaultAsync(x => x.PairCodeHash == hash && !x.Revoked && x.PairExpiresAtUtc > now && x.TokenHash == null, ct);
        if (device is null || !await HasAccessAsync(db, device, ct)) return Results.Unauthorized();
        var token = NewSecret();
        var changed = await db.MobileDevices.Where(x => x.Id == device.Id && x.PairCodeHash == hash && !x.Revoked && x.TokenHash == null)
            .ExecuteUpdateAsync(s => s.SetProperty(x => x.TokenHash, Hash(token)).SetProperty(x => x.PairCodeHash, (string?)null), ct);
        return changed == 1 ? Results.Ok(new PairResult(token, device.Workspace.Name, device.Category.Name)) : Results.Unauthorized();
    }

    private static Task<bool> HasAccessAsync(ApplicationDbContext db, MobileDevice device, CancellationToken ct) =>
        db.WorkspaceMembers.AnyAsync(x => x.UserId == device.UserId && x.WorkspaceId == device.WorkspaceId, ct);

    private static async Task<IResult> ImportAsync(HttpContext ctx, ApplicationDbContext db, CancellationToken ct)
    {
        ctx.Response.Headers.CacheControl = "no-store";
        if (!ctx.Request.IsHttps) return Results.BadRequest();
        var auth = ctx.Request.Headers.Authorization.ToString();
        if (!auth.StartsWith("Bearer ", StringComparison.Ordinal) || auth.Length != 71) return Results.Unauthorized();
        var tokenHash = Hash(auth[7..]);
        var device = await db.MobileDevices.AsNoTracking().SingleOrDefaultAsync(x => x.TokenHash == tokenHash && !x.Revoked, ct);
        if (device is null) return Results.Unauthorized();
        var input = await ReadAsync<SmsBatch>(ctx, ct);
        if (input?.Messages is null || input.Messages.Count is < 1 or > SyncPolicy.BatchSize
            || input.Messages.Any(x => !SyncPolicy.Valid(x, DateTimeOffset.UtcNow))) return Results.BadRequest(new { error = "Invalid batch" });
        // Retry strategy covers the complete atomic unit. A receipt makes lost responses safe to retry.
        return await db.Database.CreateExecutionStrategy().ExecuteAsync<IResult>(async () => {
            db.ChangeTracker.Clear();
            await using var tx = await db.Database.BeginTransactionAsync(ct);
            // Serialize all devices of this user, not unrelated users. Hash collisions only delay; never grant access.
            await db.Database.ExecuteSqlInterpolatedAsync($"SELECT pg_advisory_xact_lock(hashtextextended({device.UserId}, 0))", ct);
            var now = DateTime.UtcNow;
            if (!await HasAccessAsync(db, device, ct) || !await db.ExpenseCategories.AnyAsync(x => x.Id == device.CategoryId && x.WorkspaceId == device.WorkspaceId && !x.IsArchived, ct))
                return Results.StatusCode(403);
            var changed = await db.MobileDevices.Where(x => x.Id == device.Id && !x.Revoked && x.TokenHash == tokenHash && x.NextAllowedAtUtc <= now)
                .ExecuteUpdateAsync(s => s.SetProperty(x => x.NextAllowedAtUtc, now.AddSeconds(30)), ct);
            if (changed == 0) { ctx.Response.Headers.RetryAfter = "30"; return Results.StatusCode(429); }
            var results = new List<SmsResult>();
            var seen = new HashSet<string>();
            foreach (var sms in input.Messages)
            {
                var key = sms.Key;
                if (!seen.Add(key) || await db.SmsReceipts.AnyAsync(x => x.UserId == device.UserId && x.Key == key, ct)) {
                    results.Add(new(key, "duplicate")); continue;
                }
                var parsed = BankSmsParser.Parse(sms.Body);
                if (parsed is null) { results.Add(new(key, "rejected")); continue; }
                db.Expenses.Add(new Expense {
                    WorkspaceId = device.WorkspaceId, CategoryId = device.CategoryId, CreatedByUserId = device.UserId,
                    Reason = $"برداشت پیامکی {(parsed.Bank == "blu" ? "بلو" : "ملت")} {parsed.Account}",
                    Amount = parsed.AmountToman, ExpenseDate = DateOnly.FromDateTime(parsed.LocalDateTime),
                    SmsText = sms.Body, SmsLocalTime = parsed.LocalDateTime.ToString("HH:mm", System.Globalization.CultureInfo.InvariantCulture), NeedsReview = true
                });
                db.SmsReceipts.Add(new SmsReceipt { UserId = device.UserId, Key = key });
                results.Add(new(key, "created"));
            }
            await db.SaveChangesAsync(ct);
            await tx.CommitAsync(ct);
            return Results.Ok(new SmsBatchResult(results));
        });
    }
}
