using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Security.Claims;
using Donysh.Sms;
using Donysh.Shared;
using HesabYar.Web.Data;
using HesabYar.Web.Domain;
using HesabYar.Web.Sms;
using HesabYar.Web.Services;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Hosting.Server;
using Microsoft.AspNetCore.Hosting.Server.Features;
using Microsoft.AspNetCore.Http;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.AspNetCore.Mvc.RazorPages;
using Microsoft.AspNetCore.Identity;
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
            var initializer = new DatabaseInitializer(db, NullLogger<DatabaseInitializer>.Instance);
            await initializer.InitializeAsync();

            var owner = new ApplicationUser { Id = Guid.NewGuid().ToString(), UserName = "owner", NormalizedUserName = "OWNER" };
            var successor = new ApplicationUser { Id = Guid.NewGuid().ToString(), UserName = "successor", NormalizedUserName = "SUCCESSOR" };
            var secondMember = new ApplicationUser { Id = Guid.NewGuid().ToString(), UserName = "second", NormalizedUserName = "SECOND" };
            var outsider = new ApplicationUser { Id = Guid.NewGuid().ToString(), UserName = "outsider", NormalizedUserName = "OUTSIDER" };
            var sharedWorkspace = new Workspace { Name = "Shared transfer test", OwnerUserId = owner.Id, Type = WorkspaceType.Shared };
            db.AddRange(owner, successor, secondMember, outsider, sharedWorkspace,
                new WorkspaceMember { UserId = owner.Id, WorkspaceId = sharedWorkspace.Id, Role = WorkspaceRole.Owner },
                new WorkspaceMember { UserId = successor.Id, WorkspaceId = sharedWorkspace.Id, Role = WorkspaceRole.Member },
                new WorkspaceMember { UserId = secondMember.Id, WorkspaceId = sharedWorkspace.Id, Role = WorkspaceRole.Member });
            await db.SaveChangesAsync();
            db.ChangeTracker.Clear();

            var ownership = new WorkspaceOwnershipService(db);
            var transferred = await ownership.TransferAsync(sharedWorkspace.Id, owner.Id, successor.Id);
            Check(transferred == OwnershipTransferResult.Success, "shared workspace ownership transfer succeeds");
            Check(await db.Workspaces.AnyAsync(x => x.Id == sharedWorkspace.Id && x.OwnerUserId == successor.Id)
                  && await db.WorkspaceMembers.AnyAsync(x => x.WorkspaceId == sharedWorkspace.Id && x.UserId == successor.Id && x.Role == WorkspaceRole.Owner)
                  && await db.WorkspaceMembers.AnyAsync(x => x.WorkspaceId == sharedWorkspace.Id && x.UserId == owner.Id && x.Role == WorkspaceRole.Member),
                "workspace owner and both membership roles change atomically");
            Check(await ownership.TransferAsync(sharedWorkspace.Id, owner.Id, secondMember.Id) == OwnershipTransferResult.NotCurrentOwner,
                "former owner cannot transfer ownership again");
            Check(await ownership.TransferAsync(sharedWorkspace.Id, successor.Id, outsider.Id) == OwnershipTransferResult.TargetNotMember,
                "ownership cannot be transferred to a non-member");

            var user = new ApplicationUser { Id = Guid.NewGuid().ToString(), UserName = "sms-test@example.com", NormalizedUserName = "SMS-TEST@EXAMPLE.COM",
                Email = "sms-test@example.com", NormalizedEmail = "SMS-TEST@EXAMPLE.COM", DisplayName = "کاربر آزمون" };
            user.PasswordHash = new PasswordHasher<ApplicationUser>().HashPassword(user, "Secret123");
            var workspace = new Workspace { Name = "Test destination", OwnerUserId = user.Id, Type = WorkspaceType.Personal };
            var other = new Workspace { Name = "Other workspace", OwnerUserId = user.Id, Type = WorkspaceType.Personal };
            var category = new ExpenseCategory { Name = "Review", WorkspaceId = workspace.Id };
            var otherCategory = new ExpenseCategory { Name = "سایر", WorkspaceId = other.Id };
            var initialToken = Convert.ToHexString(System.Security.Cryptography.RandomNumberGenerator.GetBytes(32));
            var device = new MobileDevice { Name = "Test", UserId = user.Id, WorkspaceId = workspace.Id, CategoryId = category.Id,
                TokenHash = MobileApi.Hash(initialToken) };
            db.AddRange(user, workspace, other, category, otherCategory, device,
                new WorkspaceMember { UserId = user.Id, WorkspaceId = workspace.Id, Role = WorkspaceRole.Owner },
                new WorkspaceMember { UserId = user.Id, WorkspaceId = other.Id, Role = WorkspaceRole.Owner },
                new WorkspaceMember { UserId = successor.Id, WorkspaceId = workspace.Id, Role = WorkspaceRole.Member },
                new WorkspaceMember { UserId = successor.Id, WorkspaceId = other.Id, Role = WorkspaceRole.Member });
            await db.SaveChangesAsync();

            var transferableExpense = new Expense
            {
                WorkspaceId = workspace.Id,
                CategoryId = category.Id,
                CreatedByUserId = user.Id,
                Reason = "عنوان پیش از انتقال",
                Amount = 250_000,
                ExpenseDate = DateOnly.FromDateTime(DateTime.UtcNow)
            };
            db.Expenses.Add(transferableExpense);
            await db.SaveChangesAsync();
            var expenseTransfer = new ExpenseTransferService(db);
            Check(await expenseTransfer.TransferAsync(workspace.Id, transferableExpense.Id, sharedWorkspace.Id,
                      otherCategory.Id, "عنوان مقصد", user.Id) == ExpenseTransferResult.AccessDenied,
                "expense transfer rejects a destination outside the user's workspaces");
            Check(await expenseTransfer.TransferAsync(workspace.Id, transferableExpense.Id, other.Id,
                      category.Id, "عنوان مقصد", user.Id) == ExpenseTransferResult.CategoryNotFound,
                "expense transfer rejects a category from another workspace");
            Check(await expenseTransfer.TransferAsync(workspace.Id, transferableExpense.Id, other.Id,
                      otherCategory.Id, "عنوان مقصد", successor.Id) == ExpenseTransferResult.NotAllowed,
                "ordinary members cannot move another member's expense");
            Check(await expenseTransfer.TransferAsync(workspace.Id, transferableExpense.Id, other.Id,
                      otherCategory.Id, "  عنوان مقصد  ", user.Id) == ExpenseTransferResult.Success,
                "expense transfer succeeds between two accessible workspaces");
            db.ChangeTracker.Clear();
            var movedExpense = await db.Expenses.SingleAsync(x => x.Id == transferableExpense.Id);
            Check(movedExpense.WorkspaceId == other.Id
                  && movedExpense.CategoryId == otherCategory.Id
                  && movedExpense.Reason == "عنوان مقصد"
                  && movedExpense.Amount == 250_000,
                "expense transfer preserves financial values and applies destination fields");
            db.Expenses.Remove(movedExpense);
            await db.SaveChangesAsync();

            var obligation = new RecurringObligation
            {
                WorkspaceId = workspace.Id,
                CategoryId = category.Id,
                CreatedByUserId = user.Id,
                Title = "قسط آزمون انتقال",
                Type = RecurringObligationType.Installment,
                Amount = 300_000,
                StartYear = 1405,
                StartMonth = 8,
                DurationMonths = 2
            };
            var obligationExpense = new Expense
            {
                WorkspaceId = workspace.Id,
                CategoryId = category.Id,
                CreatedByUserId = user.Id,
                Reason = obligation.Title,
                Amount = 100_000,
                ExpenseDate = DateOnly.FromDateTime(DateTime.UtcNow)
            };
            var obligationPayment = new RecurringObligationPayment
            {
                SubmissionKey = Guid.NewGuid(),
                RecurringObligationId = obligation.Id,
                ExpenseId = obligationExpense.Id,
                PaidByUserId = user.Id,
                PeriodYear = 1405,
                PeriodMonth = 8,
                Amount = obligationExpense.Amount,
                PaidDate = obligationExpense.ExpenseDate
            };
            var secondObligationExpense = new Expense
            {
                WorkspaceId = workspace.Id,
                CategoryId = category.Id,
                CreatedByUserId = user.Id,
                Reason = obligation.Title,
                Amount = 200_000,
                ExpenseDate = obligationExpense.ExpenseDate
            };
            var secondObligationPayment = new RecurringObligationPayment
            {
                SubmissionKey = Guid.NewGuid(),
                RecurringObligationId = obligation.Id,
                ExpenseId = secondObligationExpense.Id,
                PaidByUserId = user.Id,
                PeriodYear = 1405,
                PeriodMonth = 8,
                Amount = secondObligationExpense.Amount,
                PaidDate = secondObligationExpense.ExpenseDate
            };
            db.AddRange(obligation, obligationExpense, obligationPayment, secondObligationExpense, secondObligationPayment);
            await db.SaveChangesAsync();
            Check(await db.RecurringObligationPayments.CountAsync(x => x.RecurringObligationId == obligation.Id) == 2,
                "multiple partial obligation payments are allowed in one month");
            Check(await expenseTransfer.TransferAsync(workspace.Id, obligationExpense.Id, other.Id,
                      otherCategory.Id, "قسط منتقل‌شده", user.Id) == ExpenseTransferResult.LinkedToObligation,
                "expense transfer cannot detach a recorded obligation payment");
            db.RemoveRange(obligationPayment, secondObligationPayment, obligationExpense, secondObligationExpense, obligation);
            await db.SaveChangesAsync();

            var historical = new Expense
            {
                WorkspaceId = workspace.Id, CategoryId = category.Id, CreatedByUserId = user.Id,
                Reason = "پیام قدیمی", Amount = 1000, ExpenseDate = DateOnly.FromDateTime(DateTime.UtcNow),
                SmsText = "برداشت 10,000 ریال\nموجودی: 999,999,999 ریال"
            };
            db.Expenses.Add(historical);
            await db.SaveChangesAsync();
            await initializer.InitializeAsync();
            db.ChangeTracker.Clear();
            var cleanedHistorical = await db.Expenses.SingleAsync(x => x.Id == historical.Id);
            Check(cleanedHistorical.SmsText is not null
                  && !cleanedHistorical.SmsText.Contains("موجودی", StringComparison.Ordinal)
                  && !cleanedHistorical.SmsText.Contains("999,999,999", StringComparison.Ordinal),
                "upgrade removes balances from previously stored SMS text");
            db.Expenses.Remove(cleanedHistorical);
            await db.SaveChangesAsync();

            var builder = WebApplication.CreateBuilder();
            const string mobileTicketSecret = "donysh-postgres-mobile-ticket-test-secret-2026";
            builder.Configuration["CrossAppLogout:Secret"] = mobileTicketSecret;
            builder.Logging.ClearProviders(); builder.WebHost.UseUrls("http://127.0.0.1:0");
            builder.Services.AddDbContext<ApplicationDbContext>(o => o.UseNpgsql(cs.ConnectionString, p => p.EnableRetryOnFailure()));
            // AddIdentityCore registers SignInManager's Identity dependencies, while the
            // authentication scheme provider is supplied by AddAuthentication.
            builder.Services.AddAuthentication();
            builder.Services.AddIdentityCore<ApplicationUser>(options =>
                {
                    // Keep this host aligned with the production Identity policy. Relying on
                    // framework defaults makes the login regression test SDK-version dependent.
                    options.User.RequireUniqueEmail = true;
                    options.SignIn.RequireConfirmedAccount = false;
                    options.Password.RequiredLength = 8;
                    options.Password.RequireDigit = true;
                    options.Password.RequireLowercase = true;
                    options.Password.RequireUppercase = false;
                    options.Password.RequireNonAlphanumeric = false;
                    options.Lockout.MaxFailedAccessAttempts = 5;
                    options.Lockout.DefaultLockoutTimeSpan = TimeSpan.FromMinutes(15);
                })
                .AddRoles<IdentityRole>()
                .AddEntityFrameworkStores<ApplicationDbContext>()
                .AddSignInManager();
            builder.Services.AddScoped<FinanceAccountProvisioner>();
            builder.Services.AddScoped<FinanceSessionPrincipalFactory>();
            builder.Services.AddMobileApi();
            await using var app = builder.Build();
            app.UseRouting(); app.UseRateLimiter();
            app.Use(async (ctx, next) => { ctx.Request.Scheme = "https"; await next(); });
            app.MapMobileApi(); await app.StartAsync();
            using var client = new HttpClient { BaseAddress = new(app.Services.GetRequiredService<IServer>().Features.Get<IServerAddressesFeature>()!.Addresses.Single()) };
            void Check(bool value, string name) { if (!value) throw new Exception("FAIL PostgreSQL: " + name); Console.WriteLine("PASS PostgreSQL: " + name); }
            await using (var provisioningScope = app.Services.CreateAsyncScope())
            {
                var provisioner = provisioningScope.ServiceProvider.GetRequiredService<FinanceAccountProvisioner>();
                const string centralSubject = "new-central-user";
                var firstLink = await provisioner.EnsureAsync(
                    centralSubject, "new-central-user@example.com", false, "کاربر مرکزی", CancellationToken.None);
                var secondLink = await provisioner.EnsureAsync(
                    centralSubject, "new-central-user@example.com", false, "کاربر مرکزی", CancellationToken.None);
                var provisioningDb = provisioningScope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
                var personalWorkspace = await provisioningDb.Workspaces
                    .SingleAsync(x => x.OwnerUserId == centralSubject && x.Type == WorkspaceType.Personal);
                Check(firstLink.UserId == centralSubject
                      && secondLink.UserId == centralSubject
                      && await provisioningDb.WorkspaceMembers.CountAsync(x => x.UserId == centralSubject) == 1
                      && await provisioningDb.ExpenseCategories.CountAsync(x => x.WorkspaceId == personalWorkspace.Id) == 7,
                    "first central login provisions one complete personal finance workspace");

                var legacyUser = new ApplicationUser
                {
                    Id = "legacy-finance-id",
                    UserName = "legacy@example.com",
                    Email = "legacy@example.com",
                    EmailConfirmed = true,
                    DisplayName = "کاربر قدیمی"
                };
                var legacyCreated = await provisioningScope.ServiceProvider
                    .GetRequiredService<UserManager<ApplicationUser>>()
                    .CreateAsync(legacyUser);
                Check(legacyCreated.Succeeded, "legacy Finance user fixture is created");
                var legacyLink = await provisioner.EnsureAsync(
                    "different-central-subject", "LEGACY@example.com", true, "نام مرکزی", CancellationToken.None);
                Check(legacyLink.UserId == legacyUser.Id
                      && await provisioningDb.Users.CountAsync(x => x.NormalizedEmail == "LEGACY@EXAMPLE.COM") == 1,
                    "verified central email links an existing Finance account without a redirect loop");
                var unverifiedRejected = false;
                try
                {
                    await provisioner.EnsureAsync("unverified-central-subject", "LEGACY@example.com", false,
                        "کاربر تازه", CancellationToken.None);
                }
                catch (InvalidOperationException)
                {
                    unverifiedRejected = true;
                }
                Check(unverifiedRejected && await provisioningDb.Users.CountAsync(x => x.NormalizedEmail == "LEGACY@EXAMPLE.COM") == 1,
                    "unverified email cannot claim a legacy Finance account");

                var centralPrincipal = new ClaimsPrincipal(new ClaimsIdentity(
                    [new Claim("sub", "different-central-subject")], "oidc"));
                var financePrincipal = await provisioningScope.ServiceProvider
                    .GetRequiredService<FinanceSessionPrincipalFactory>()
                    .CreateAsync(centralPrincipal, legacyLink);
                var stampType = new IdentityOptions().ClaimsIdentity.SecurityStampClaimType;
                Check(financePrincipal.Identity?.AuthenticationType == IdentityConstants.ApplicationScheme
                      && financePrincipal.FindFirstValue(ClaimTypes.NameIdentifier) == legacyUser.Id
                      && financePrincipal.FindFirstValue("sub") == "different-central-subject"
                      && financePrincipal.FindFirstValue(stampType) == legacyUser.SecurityStamp,
                    "central login creates a framework-valid Finance Identity session");
            }
            var installationId = Convert.ToHexString(System.Security.Cryptography.RandomNumberGenerator.GetBytes(32));
            var ticket = MobileSignInTicket.Create(mobileTicketSecret, user.Id, user.Email!,
                user.DisplayName, installationId, emailVerified: true);
            using var legacyPassword = await client.PostAsJsonAsync("/api/mobile/login",
                new { email = user.Email, password = "Secret123", installationId, deviceName = "Old client" });
            Check(legacyPassword.StatusCode == HttpStatusCode.Unauthorized,
                "Finance rejects account passwords and requires a central ticket");
            using var login = await client.PostAsJsonAsync("/api/mobile/login",
                new MobileTicketRequest(ticket, installationId, "Login device"));
            Check(login.StatusCode == HttpStatusCode.OK, "central ticket creates a Finance mobile session");
            var session = (await login.Content.ReadFromJsonAsync<MobileSessionResult>())!;
            Check(session.Workspaces.Count == 2 && session.Workspaces.All(x => x.Id == workspace.Id || x.Id == other.Id),
                "mobile session only lists member workspaces");
            using var loginClient = new HttpClient { BaseAddress = client.BaseAddress };
            loginClient.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", session.Token);
            using var changedDestination = await loginClient.PutAsJsonAsync("/api/mobile/destination", new MobileDestinationRequest(other.Id));
            var changed = await changedDestination.Content.ReadFromJsonAsync<MobileDestinationResult>();
            Check(changedDestination.StatusCode == HttpStatusCode.OK && changed?.WorkspaceId == other.Id,
                "authenticated device can change to a member workspace");
            using var deniedDestination = await loginClient.PutAsJsonAsync("/api/mobile/destination", new MobileDestinationRequest(sharedWorkspace.Id));
            Check(deniedDestination.StatusCode == HttpStatusCode.Forbidden,
                "device cannot change to another user's workspace");
            using var renewed = await client.PostAsJsonAsync("/api/mobile/login",
                new MobileTicketRequest(ticket, installationId, "Login device", session.Token));
            Check(renewed.StatusCode == HttpStatusCode.OK
                && await db.MobileDevices.CountAsync(x => x.UserId == user.Id && !x.Revoked && x.TokenHash != null) == 2,
                "signing in again rotates one installation session without adding a device");
            using var retiredToken = await loginClient.GetAsync("/api/mobile/workspaces");
            Check(retiredToken.StatusCode == HttpStatusCode.Unauthorized,
                "rotated Finance token cannot access mobile data");
            var unrelatedTicket = MobileSignInTicket.Create(mobileTicketSecret, outsider.Id,
                "outsider@example.com", "Other user", installationId, emailVerified: false);
            using var noFinance = await client.PostAsJsonAsync("/api/mobile/login",
                new MobileTicketRequest(unrelatedTicket, installationId, "Other phone"));
            Check(noFinance.StatusCode == HttpStatusCode.Forbidden,
                "an Auth account without Finance membership cannot enter Finance");
            client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", initialToken);            var sms = new SmsEnvelope("Mellat", "حساب5555555555\nبرداشت50,000,000\nمانده111,111,111\n05/06/15-12:14", DateTimeOffset.UtcNow.ToUnixTimeMilliseconds());
            using var oversized = await client.PostAsJsonAsync("/api/mobile/sms", new SmsBatch(Enumerable.Repeat(sms, 6).ToList()));
            Check(oversized.StatusCode == HttpStatusCode.BadRequest, "batch count cap");
            using var imported = await client.PostAsJsonAsync("/api/mobile/sms", new { workspaceId = other.Id, userId = "forged", messages = new[] { sms } });
            Check(imported.StatusCode == HttpStatusCode.OK, "import");
            var expense = await db.Expenses.AsNoTracking().SingleAsync();
            Check(expense.WorkspaceId == workspace.Id && expense.CreatedByUserId == user.Id, "destination bound to token, forged scope ignored");
            Check(expense.Amount == 5_000_000 && expense.NeedsReview
                  && expense.SmsText is not null && !expense.SmsText.Contains("111,111,111", StringComparison.Ordinal)
                  && !expense.SmsText.Contains("مانده", StringComparison.Ordinal)
                  && expense.ExpenseDate == new DateOnly(2026, 9, 6),
                "expense values and review metadata exclude the account balance");
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
