using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using Donysh.Sms;
using HesabYar.Web.Data;
using HesabYar.Web.Domain;
using HesabYar.Web.Sms;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Hosting.Server;
using Microsoft.AspNetCore.Hosting.Server.Features;
using Microsoft.AspNetCore.Http;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Npgsql;

internal static class PostgresChecks
{
    public static async Task RunAsync(string adminConnection)
    {
        // Never reset an existing database: create and later drop only this exact random test database.
        var databaseName = "donysh_sms_test_" + Guid.NewGuid().ToString("N");
        var cs = new NpgsqlConnectionStringBuilder(adminConnection) { Database = "postgres", Pooling = false };
        await using var admin = new NpgsqlConnection(cs.ConnectionString);
        await admin.OpenAsync();
        await using (var create = new NpgsqlCommand($"CREATE DATABASE \"{databaseName}\"", admin)) await create.ExecuteNonQueryAsync();
        try {
            cs.Database = databaseName;
            var options = new DbContextOptionsBuilder<ApplicationDbContext>().UseNpgsql(cs.ConnectionString).Options;
            await using var db = new ApplicationDbContext(options);
            await db.Database.EnsureCreatedAsync();
            // Simulate a pre-feature database, then run the additive upgrade twice.
            await db.Database.ExecuteSqlRawAsync("""
                DROP TABLE "MobileDevices", "SmsReceipts";
                ALTER TABLE "Expenses" DROP COLUMN "SmsText", DROP COLUMN "SmsLocalTime", DROP COLUMN "NeedsReview";
                """);
            await MobileSchema.UpgradeAsync(db, default);
            await MobileSchema.UpgradeAsync(db, default);
            var user = new ApplicationUser { Id = Guid.NewGuid().ToString(), UserName = "sms-test", NormalizedUserName = "SMS-TEST" };
            var workspace = new Workspace { Name = "Test destination", OwnerUserId = user.Id, Type = WorkspaceType.Personal };
            var other = new Workspace { Name = "Other workspace", OwnerUserId = user.Id, Type = WorkspaceType.Personal };
            var category = new ExpenseCategory { Name = "Review", WorkspaceId = workspace.Id };
            var code = Convert.ToHexString(System.Security.Cryptography.RandomNumberGenerator.GetBytes(16));
            var device = new MobileDevice { Name = "Test", UserId = user.Id, WorkspaceId = workspace.Id, CategoryId = category.Id,
                PairCodeHash = MobileApi.Hash(code), PairExpiresAtUtc = DateTime.UtcNow.AddMinutes(10) };
            db.AddRange(user, workspace, other, category, device,
                new WorkspaceMember { UserId = user.Id, WorkspaceId = workspace.Id, Role = WorkspaceRole.Owner });
            await db.SaveChangesAsync();

            var builder = WebApplication.CreateBuilder();
            builder.Logging.ClearProviders(); builder.WebHost.UseUrls("http://127.0.0.1:0");
            builder.Services.AddDbContext<ApplicationDbContext>(o => o.UseNpgsql(cs.ConnectionString, p => p.EnableRetryOnFailure()));
            builder.Services.AddMobileApi();
            await using var app = builder.Build();
            app.UseRouting(); app.UseRateLimiter();
            app.Use(async (ctx, next) => { ctx.Request.Scheme = "https"; await next(); });
            app.MapMobileApi(); await app.StartAsync();
            using var client = new HttpClient { BaseAddress = new(app.Services.GetRequiredService<IServer>().Features.Get<IServerAddressesFeature>()!.Addresses.Single()) };
            void Check(bool value, string name) { if (!value) throw new Exception("FAIL PostgreSQL: " + name); Console.WriteLine("PASS PostgreSQL: " + name); }
            using var paired = await client.PostAsJsonAsync("/api/mobile/pair", new PairRequest(code));
            Check(paired.StatusCode == HttpStatusCode.OK, "pairing");
            var pair = (await paired.Content.ReadFromJsonAsync<PairResult>())!;
            using var replay = await client.PostAsJsonAsync("/api/mobile/pair", new PairRequest(code));
            Check(replay.StatusCode == HttpStatusCode.Unauthorized, "pairing code is single-use");
            client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", pair.Token);
            var sms = new SmsEnvelope("Mellat", "حساب5555555555\nبرداشت50,000,000\nمانده111,111,111\n05/06/15-12:14", DateTimeOffset.UtcNow.ToUnixTimeMilliseconds());
            using var oversized = await client.PostAsJsonAsync("/api/mobile/sms", new SmsBatch(Enumerable.Repeat(sms, 6).ToList()));
            Check(oversized.StatusCode == HttpStatusCode.BadRequest, "batch count cap");
            using var imported = await client.PostAsJsonAsync("/api/mobile/sms", new { workspaceId = other.Id, userId = "forged", messages = new[] { sms } });
            Check(imported.StatusCode == HttpStatusCode.OK, "import");
            var expense = await db.Expenses.AsNoTracking().SingleAsync();
            Check(expense.WorkspaceId == workspace.Id && expense.CreatedByUserId == user.Id, "destination bound to token, forged scope ignored");
            Check(expense.Amount == 5_000_000 && expense.NeedsReview && expense.SmsText == sms.Body && expense.ExpenseDate == new DateOnly(2026, 9, 6), "expense values and review metadata");
            using var paced = await client.PostAsJsonAsync("/api/mobile/sms", new SmsBatch([sms]));
            Check(paced.StatusCode == HttpStatusCode.TooManyRequests && paced.Headers.RetryAfter is not null, "persistent device pacing");
            async Task Unpace() => await db.MobileDevices.Where(x => x.Id == device.Id).ExecuteUpdateAsync(s => s.SetProperty(x => x.NextAllowedAtUtc, DateTime.UnixEpoch));
            await Unpace();
            using var retry = await client.PostAsJsonAsync("/api/mobile/sms", new SmsBatch([sms]));
            Check((await retry.Content.ReadFromJsonAsync<SmsBatchResult>())!.Results.Single().Status == "duplicate" && await db.Expenses.CountAsync() == 1, "lost response retry is idempotent");
            await db.Expenses.ExecuteDeleteAsync(); await Unpace();
            using var deletedRetry = await client.PostAsJsonAsync("/api/mobile/sms", new SmsBatch([sms]));
            Check(deletedRetry.StatusCode == HttpStatusCode.OK && await db.Expenses.CountAsync() == 0, "deleted expense is not recreated");
            await Unpace();
            await db.ExpenseCategories.Where(x => x.Id == category.Id).ExecuteUpdateAsync(s => s.SetProperty(x => x.IsArchived, true));
            using var archived = await client.PostAsJsonAsync("/api/mobile/sms", new SmsBatch([sms]));
            Check(archived.StatusCode == HttpStatusCode.Forbidden, "archived category denied");
            await db.ExpenseCategories.Where(x => x.Id == category.Id).ExecuteUpdateAsync(s => s.SetProperty(x => x.IsArchived, false));
            await db.WorkspaceMembers.ExecuteDeleteAsync();
            using var removed = await client.PostAsJsonAsync("/api/mobile/sms", new SmsBatch([sms]));
            Check(removed.StatusCode == HttpStatusCode.Forbidden, "removed workspace membership denied");
            await db.MobileDevices.Where(x => x.Id == device.Id).ExecuteUpdateAsync(s => s.SetProperty(x => x.Revoked, true));
            using var revoked = await client.PostAsJsonAsync("/api/mobile/sms", new SmsBatch([sms]));
            Check(revoked.StatusCode == HttpStatusCode.Unauthorized, "revoked device denied");
            await app.StopAsync();
        } finally {
            await using var drop = new NpgsqlCommand($"DROP DATABASE \"{databaseName}\" WITH (FORCE)", admin);
            await drop.ExecuteNonQueryAsync();
        }
    }
}
