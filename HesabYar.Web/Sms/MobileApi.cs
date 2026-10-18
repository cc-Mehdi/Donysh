using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Threading.RateLimiting;
using Donysh.Sms;
using HesabYar.Web.Data;
using HesabYar.Web.Domain;
using Microsoft.AspNetCore.Http.Features;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;

namespace HesabYar.Web.Sms;

public static class MobileApi
{
    public static string Hash(string value) => Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(value)));
    public static string NewSecret() => Convert.ToHexString(RandomNumberGenerator.GetBytes(32));
    private static bool IsHexSecret(string? value, int length) =>
        value is not null && value.Length == length && value.All(Uri.IsHexDigit);

    public static void AddMobileApi(this IServiceCollection services)
    {
        services.AddRateLimiter(o => {
            o.RejectionStatusCode = 429;
            o.OnRejected = (ctx, _) => { ctx.HttpContext.Response.Headers.RetryAfter = "60"; return ValueTask.CompletedTask; };
            o.AddPolicy("mobile", ctx => RateLimitPartition.GetFixedWindowLimiter(
                $"mobile:{ClientKey(ctx)}", _ => new() {
                PermitLimit = 60, Window = TimeSpan.FromMinutes(1), QueueLimit = 0,
                AutoReplenishment = true }));
            o.AddPolicy("account", ctx => RateLimitPartition.GetFixedWindowLimiter(
                $"account:{ClientKey(ctx)}", _ => new() {
                PermitLimit = 30, Window = TimeSpan.FromMinutes(1), QueueLimit = 0,
                AutoReplenishment = true }));
            o.AddPolicy("mobile-login", ctx => RateLimitPartition.GetFixedWindowLimiter(
                $"mobile-login:{ClientKey(ctx)}", _ => new() {
                PermitLimit = 5, Window = TimeSpan.FromMinutes(1), QueueLimit = 0,
                AutoReplenishment = true }));
            o.GlobalLimiter = PartitionedRateLimiter.Create<HttpContext, string>(ctx =>
                ctx.Request.Path.StartsWithSegments("/api/mobile")
                    ? RateLimitPartition.GetConcurrencyLimiter(
                        $"mobile-concurrency:{ClientKey(ctx)}", _ => new() { PermitLimit = 2, QueueLimit = 0 })
                    : RateLimitPartition.GetNoLimiter("web"));
        });
    }

    private static string ClientKey(HttpContext context) =>
        context.Connection.RemoteIpAddress?.ToString() ?? "unknown";

    public static void MapMobileApi(this WebApplication app)
    {
        var group = app.MapGroup("/api/mobile").RequireRateLimiting("mobile");
        group.MapPost("/pair", PairAsync);
        group.MapPost("/sms", ImportAsync);
        group.MapGet("/workspaces", WorkspacesAsync);
        group.MapPut("/destination", ChangeDestinationAsync);
        group.MapPost("/logout", LogoutAsync);
        app.MapPost("/api/mobile/login", LoginAsync).RequireRateLimiting("mobile-login");
    }

    private static async Task<IResult> LoginAsync(
        HttpContext ctx,
        ApplicationDbContext db,
        CancellationToken ct)
    {
        ctx.Response.Headers.CacheControl = "no-store";
        if (!ctx.Request.IsHttps) return Results.BadRequest(new { error = "HTTPS required" });
        var input = await ReadAsync<MobileLoginRequest>(ctx, ct);
        var email = input?.Email?.Trim();
        var password = input?.Password;
        var deviceName = input?.DeviceName?.Trim();
        if (string.IsNullOrWhiteSpace(email) || email.Length > 254
            || email.Any(char.IsControl)
            || string.IsNullOrEmpty(password) || password.Length > 256
            || !IsHexSecret(input?.InstallationId, 64)
            || string.IsNullOrWhiteSpace(deviceName) || deviceName.Length > 80 || deviceName.Any(char.IsControl))
            return Results.BadRequest(new { error = "Invalid login" });

        var users = ctx.RequestServices.GetRequiredService<UserManager<ApplicationUser>>();
        var signIn = ctx.RequestServices.GetRequiredService<SignInManager<ApplicationUser>>();
        var user = await users.FindByEmailAsync(email);
        // Use the same Identity password and lockout rules as the website. The response is
        // deliberately identical for unknown users and wrong passwords.
        if (user is null || !(await signIn.CheckPasswordSignInAsync(user, password, lockoutOnFailure: true)).Succeeded)
            return Results.Unauthorized();

        var workspaces = await AccessibleWorkspacesAsync(db, user.Id, ct);
        if (workspaces.Count == 0) return Results.Conflict(new { error = "No workspace" });

        var instanceHash = Hash(input!.InstallationId.ToUpperInvariant());
        var token = NewSecret();
        return await db.Database.CreateExecutionStrategy().ExecuteAsync<IResult>(async () =>
        {
            db.ChangeTracker.Clear();
            await using var tx = await db.Database.BeginTransactionAsync(ct);
            await db.Database.ExecuteSqlInterpolatedAsync($"SELECT pg_advisory_xact_lock(hashtextextended({user.Id}, 0))", ct);
            var device = await db.MobileDevices.SingleOrDefaultAsync(
                x => x.UserId == user.Id && x.ClientInstanceHash == instanceHash, ct);
            if (device is null)
            {
                if (await db.MobileDevices.CountAsync(x => x.UserId == user.Id && !x.Revoked, ct) >= 5)
                    return Results.Conflict(new { error = "Device limit" });
                var destination = await ResolveDestinationAsync(db, user.Id, workspaces[0].Id, ct);
                if (destination is null) return Results.Conflict(new { error = "No category" });
                device = new MobileDevice
                {
                    UserId = user.Id,
                    WorkspaceId = destination.Value.Workspace.Id,
                    CategoryId = destination.Value.Category.Id,
                    Name = deviceName,
                    ClientInstanceHash = instanceHash
                };
                db.MobileDevices.Add(device);
            }
            else
            {
                var destination = await ResolveDestinationAsync(db, user.Id, device.WorkspaceId, ct)
                    ?? await ResolveDestinationAsync(db, user.Id, workspaces[0].Id, ct);
                if (destination is null) return Results.Conflict(new { error = "No category" });
                device.WorkspaceId = destination.Value.Workspace.Id;
                device.CategoryId = destination.Value.Category.Id;
                device.Name = deviceName;
                device.Revoked = false;
            }
            device.TokenHash = Hash(token);
            device.PairCodeHash = null;
            device.PairExpiresAtUtc = DateTime.UnixEpoch;
            device.NextAllowedAtUtc = DateTime.UnixEpoch;
            await db.SaveChangesAsync(ct);
            await tx.CommitAsync(ct);
            var selected = workspaces.Single(x => x.Id == device.WorkspaceId);
            var categoryName = await db.ExpenseCategories.Where(x => x.Id == device.CategoryId).Select(x => x.Name).SingleAsync(ct);
            return Results.Ok(new MobileSessionResult(token, user.DisplayName, selected.Id, selected.Name,
                categoryName, workspaces));
        });
    }

    private static async Task<IResult> WorkspacesAsync(HttpContext ctx, ApplicationDbContext db, CancellationToken ct)
    {
        ctx.Response.Headers.CacheControl = "no-store";
        var authenticated = await AuthenticateDeviceAsync(ctx, db, ct);
        if (authenticated is null) return Results.Unauthorized();
        var workspaces = await AccessibleWorkspacesAsync(db, authenticated.Value.Device.UserId, ct);
        var selected = workspaces.FirstOrDefault(x => x.Id == authenticated.Value.Device.WorkspaceId);
        if (selected is null) return Results.StatusCode(403);
        var categoryName = await db.ExpenseCategories.AsNoTracking()
            .Where(x => x.Id == authenticated.Value.Device.CategoryId && !x.IsArchived)
            .Select(x => x.Name).SingleOrDefaultAsync(ct);
        if (categoryName is null) return Results.StatusCode(409);
        return Results.Ok(new MobileWorkspaceList(selected.Id, selected.Name, categoryName, workspaces));
    }

    private static async Task<IResult> ChangeDestinationAsync(HttpContext ctx, ApplicationDbContext db, CancellationToken ct)
    {
        ctx.Response.Headers.CacheControl = "no-store";
        if (!ctx.Request.IsHttps) return Results.BadRequest();
        var authenticated = await AuthenticateDeviceAsync(ctx, db, ct);
        if (authenticated is null) return Results.Unauthorized();
        var input = await ReadAsync<MobileDestinationRequest>(ctx, ct);
        if (input is null || input.WorkspaceId == Guid.Empty) return Results.BadRequest();
        return await db.Database.CreateExecutionStrategy().ExecuteAsync<IResult>(async () =>
        {
            db.ChangeTracker.Clear();
            await using var tx = await db.Database.BeginTransactionAsync(ct);
            await db.Database.ExecuteSqlInterpolatedAsync($"SELECT pg_advisory_xact_lock(hashtextextended({authenticated.Value.Device.UserId}, 0))", ct);
            var device = await db.MobileDevices.SingleOrDefaultAsync(
                x => x.Id == authenticated.Value.Device.Id && x.TokenHash == authenticated.Value.TokenHash && !x.Revoked, ct);
            if (device is null) return Results.Unauthorized();
            var destination = await ResolveDestinationAsync(db, device.UserId, input.WorkspaceId, ct);
            if (destination is null) return Results.StatusCode(403);
            device.WorkspaceId = destination.Value.Workspace.Id;
            device.CategoryId = destination.Value.Category.Id;
            await db.SaveChangesAsync(ct);
            await tx.CommitAsync(ct);
            return Results.Ok(new MobileDestinationResult(device.WorkspaceId,
                destination.Value.Workspace.Name, destination.Value.Category.Name));
        });
    }

    private static async Task<IResult> LogoutAsync(HttpContext ctx, ApplicationDbContext db, CancellationToken ct)
    {
        ctx.Response.Headers.CacheControl = "no-store";
        var authenticated = await AuthenticateDeviceAsync(ctx, db, ct);
        if (authenticated is null) return Results.Unauthorized();
        return await db.Database.CreateExecutionStrategy().ExecuteAsync<IResult>(async () =>
        {
            await using var tx = await db.Database.BeginTransactionAsync(ct);
            await db.Database.ExecuteSqlInterpolatedAsync(
                $"SELECT pg_advisory_xact_lock(hashtextextended({authenticated.Value.Device.UserId}, 0))", ct);
            var changed = await db.MobileDevices.Where(x => x.Id == authenticated.Value.Device.Id
                    && x.TokenHash == authenticated.Value.TokenHash && !x.Revoked)
                .ExecuteUpdateAsync(x => x.SetProperty(d => d.Revoked, true)
                    .SetProperty(d => d.TokenHash, (string?)null), ct);
            if (changed == 0) return Results.Unauthorized();
            await tx.CommitAsync(ct);
            return Results.NoContent();
        });
    }

    private static async Task<(MobileDevice Device, string TokenHash)?> AuthenticateDeviceAsync(
        HttpContext ctx, ApplicationDbContext db, CancellationToken ct)
    {
        if (!ctx.Request.IsHttps) return null;
        var auth = ctx.Request.Headers.Authorization.ToString();
        if (!auth.StartsWith("Bearer ", StringComparison.OrdinalIgnoreCase) || !IsHexSecret(auth[7..], 64)) return null;
        var tokenHash = Hash(auth[7..]);
        var device = await db.MobileDevices.AsNoTracking().SingleOrDefaultAsync(x => x.TokenHash == tokenHash && !x.Revoked, ct);
        return device is null ? null : (device, tokenHash);
    }

    private static Task<List<MobileWorkspace>> AccessibleWorkspacesAsync(ApplicationDbContext db, string userId, CancellationToken ct) =>
        db.WorkspaceMembers.AsNoTracking().Where(x => x.UserId == userId)
            .OrderBy(x => x.Workspace.Type).ThenBy(x => x.Workspace.CreatedAtUtc)
            .Select(x => new MobileWorkspace(x.WorkspaceId, x.Workspace.Name,
                x.Workspace.Type == WorkspaceType.Shared ? "shared" : "personal"))
            .ToListAsync(ct);

    private static async Task<(Workspace Workspace, ExpenseCategory Category)?> ResolveDestinationAsync(
        ApplicationDbContext db, string userId, Guid workspaceId, CancellationToken ct)
    {
        var workspace = await db.WorkspaceMembers.AsNoTracking()
            .Where(x => x.UserId == userId && x.WorkspaceId == workspaceId)
            .Select(x => x.Workspace).SingleOrDefaultAsync(ct);
        if (workspace is null) return null;
        var category = await db.ExpenseCategories.AsNoTracking()
            .Where(x => x.WorkspaceId == workspaceId && !x.IsArchived)
            .OrderByDescending(x => x.Name == "سایر").ThenBy(x => x.CreatedAtUtc)
            .FirstOrDefaultAsync(ct);
        return category is null ? null : (workspace, category);
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
        try
        {
            int count;
            while ((count = await ctx.Request.Body.ReadAsync(chunk, ct)) != 0)
            {
                if (buffer.Length + count > 32768)
                {
                    if (ctx.Request.Protocol == "HTTP/1.1") ctx.Response.Headers.Connection = "close";
                    return default;
                }
                buffer.Write(chunk, 0, count);
            }
            return JsonSerializer.Deserialize<T>(buffer.ToArray(), new JsonSerializerOptions(JsonSerializerDefaults.Web) { MaxDepth = 8 });
        }
        catch (JsonException) { return default; }
        catch (BadHttpRequestException ex) when (ex.StatusCode == StatusCodes.Status413PayloadTooLarge)
        {
            if (ctx.Request.Protocol == "HTTP/1.1") ctx.Response.Headers.Connection = "close";
            return default;
        }
    }

    private static async Task<IResult> PairAsync(HttpContext ctx, ApplicationDbContext db, CancellationToken ct)
    {
        ctx.Response.Headers.CacheControl = "no-store";
        if (!ctx.Request.IsHttps) return Results.BadRequest(new { error = "HTTPS required" });
        var input = await ReadAsync<PairRequest>(ctx, ct);
        if (!IsHexSecret(input?.Code, 32)) return Results.BadRequest(new { error = "Invalid pairing code" });
        var hash = Hash(input!.Code.ToUpperInvariant());
        var now = DateTime.UtcNow;
        var device = await db.MobileDevices.AsNoTracking().Include(x => x.Workspace).Include(x => x.Category)
            .SingleOrDefaultAsync(x => x.PairCodeHash == hash && !x.Revoked && x.PairExpiresAtUtc > now && x.TokenHash == null, ct);
        if (device is null || !await HasAccessAsync(db, device, ct)) return Results.Unauthorized();
        var token = NewSecret();
        // Recheck expiry and destination in the atomic consume, not only the earlier lookup.
        now = DateTime.UtcNow;
        var changed = await db.MobileDevices.Where(x => x.Id == device.Id && x.PairCodeHash == hash && !x.Revoked
                && x.TokenHash == null && x.PairExpiresAtUtc > now
                && db.WorkspaceMembers.Any(m => m.UserId == x.UserId && m.WorkspaceId == x.WorkspaceId)
                && db.ExpenseCategories.Any(c => c.Id == x.CategoryId && c.WorkspaceId == x.WorkspaceId && !c.IsArchived))
            .ExecuteUpdateAsync(s => s.SetProperty(x => x.TokenHash, Hash(token)).SetProperty(x => x.PairCodeHash, (string?)null), ct);
        return changed == 1 ? Results.Ok(new PairResult(token, device.Workspace.Name, device.Category.Name)) : Results.Unauthorized();
    }

    private static Task<bool> HasAccessAsync(ApplicationDbContext db, MobileDevice device, CancellationToken ct) =>
        db.WorkspaceMembers.AnyAsync(x => x.UserId == device.UserId && x.WorkspaceId == device.WorkspaceId, ct);

    private static async Task<IResult> ImportAsync(HttpContext ctx, ApplicationDbContext db, CancellationToken ct)
    {
        ctx.Response.Headers.CacheControl = "no-store";
        if (!ctx.Request.IsHttps) return Results.BadRequest();
        var authenticated = await AuthenticateDeviceAsync(ctx, db, ct);
        if (authenticated is null) return Results.Unauthorized();
        var tokenHash = authenticated.Value.TokenHash;
        var device = authenticated.Value.Device;
        var input = await ReadAsync<SmsBatch>(ctx, ct);
        if (input?.Messages is null || input.Messages.Count is < 1 or > SyncPolicy.BatchSize
            || input.Messages.Any(x => !SyncPolicy.Valid(x, DateTimeOffset.UtcNow))) return Results.BadRequest(new { error = "Invalid batch" });
        // Retry strategy covers the complete atomic unit. A receipt makes lost responses safe to retry.
        return await db.Database.CreateExecutionStrategy().ExecuteAsync<IResult>(async () => {
            db.ChangeTracker.Clear();
            await using var tx = await db.Database.BeginTransactionAsync(ct);
            // Serialize all devices of this user, not unrelated users. Hash collisions only delay; never grant access.
            await db.Database.ExecuteSqlInterpolatedAsync($"SELECT pg_advisory_xact_lock(hashtextextended({device.UserId}, 0))", ct);
            device = await db.MobileDevices.AsNoTracking().SingleOrDefaultAsync(
                x => x.Id == device.Id && x.TokenHash == tokenHash && !x.Revoked, ct);
            if (device is null) return Results.Unauthorized();
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
                var receivedAt = DateTimeOffset.FromUnixTimeMilliseconds(sms.SentAtUnixMs);
                var parsed = BankSmsParser.Parse(sms.Body, receivedAt);
                decimal amountToman;
                DateTime localDateTime;
                string reason;
                if (parsed is not null)
                {
                    amountToman = parsed.AmountToman;
                    localDateTime = parsed.LocalDateTime;
                    reason = $"برداشت پیامکی {parsed.Bank switch { "blu" => "بلو", "resalat" => "رسالت", _ => "ملت" }} {parsed.Account}";
                }
                else if (sms.Parsed is { Kind: "withdrawal" } clientParsed)
                {
                    amountToman = clientParsed.AmountRial / 10m;
                    localDateTime = receivedAt.ToOffset(TimeSpan.FromHours(3.5)).DateTime;
                    reason = $"برداشت پیامکی · {clientParsed.TemplateName.Trim()}";
                }
                else
                {
                    results.Add(new(key, "rejected"));
                    continue;
                }
                if (!string.IsNullOrWhiteSpace(sms.Title)) reason = sms.Title.Trim();
                db.Expenses.Add(new Expense {
                    WorkspaceId = device.WorkspaceId, CategoryId = device.CategoryId, CreatedByUserId = device.UserId,
                    Reason = reason, Amount = amountToman, ExpenseDate = DateOnly.FromDateTime(localDateTime),
                    SmsText = sms.Body, SmsLocalTime = localDateTime.ToString("HH:mm", System.Globalization.CultureInfo.InvariantCulture), NeedsReview = true
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
