using Microsoft.EntityFrameworkCore;
using HesabYar.Web.Data;

namespace HesabYar.Web.Sms;

public static class MobileSchema
{
    public static void Configure(ModelBuilder builder)
    {
        builder.Entity<MobileDevice>(e => {
            e.HasIndex(x => x.TokenHash).IsUnique();
            e.HasIndex(x => x.PairCodeHash).IsUnique();
            e.HasIndex(x => x.UserId);
            e.HasOne(x => x.User).WithMany().HasForeignKey(x => x.UserId).OnDelete(DeleteBehavior.Cascade);
            e.HasOne(x => x.Workspace).WithMany().HasForeignKey(x => x.WorkspaceId).OnDelete(DeleteBehavior.Cascade);
            e.HasOne(x => x.Category).WithMany().HasForeignKey(x => x.CategoryId).OnDelete(DeleteBehavior.Cascade);
        });
        builder.Entity<SmsReceipt>(e => {
            e.HasKey(x => new { x.UserId, x.Key });
            e.HasOne(x => x.User).WithMany().HasForeignKey(x => x.UserId).OnDelete(DeleteBehavior.Cascade);
        });
    }

    public static Task UpgradeAsync(ApplicationDbContext db, CancellationToken ct) => db.Database.ExecuteSqlRawAsync("""
        ALTER TABLE "Expenses" ADD COLUMN IF NOT EXISTS "SmsText" character varying(2048);
        ALTER TABLE "Expenses" ADD COLUMN IF NOT EXISTS "SmsLocalTime" character varying(5);
        ALTER TABLE "Expenses" ADD COLUMN IF NOT EXISTS "NeedsReview" boolean NOT NULL DEFAULT FALSE;
        CREATE TABLE IF NOT EXISTS "MobileDevices" (
            "Id" uuid PRIMARY KEY, "UserId" text NOT NULL REFERENCES "AspNetUsers"("Id") ON DELETE CASCADE,
            "WorkspaceId" uuid NOT NULL REFERENCES "Workspaces"("Id") ON DELETE CASCADE,
            "CategoryId" uuid NOT NULL REFERENCES "ExpenseCategories"("Id") ON DELETE CASCADE,
            "Name" character varying(80) NOT NULL, "PairCodeHash" character varying(64), "TokenHash" character varying(64),
            "PairExpiresAtUtc" timestamp with time zone NOT NULL, "NextAllowedAtUtc" timestamp with time zone NOT NULL,
            "CreatedAtUtc" timestamp with time zone NOT NULL, "Revoked" boolean NOT NULL
        );
        CREATE UNIQUE INDEX IF NOT EXISTS "IX_MobileDevices_TokenHash" ON "MobileDevices"("TokenHash");
        CREATE UNIQUE INDEX IF NOT EXISTS "IX_MobileDevices_PairCodeHash" ON "MobileDevices"("PairCodeHash");
        CREATE INDEX IF NOT EXISTS "IX_MobileDevices_UserId" ON "MobileDevices"("UserId");
        CREATE INDEX IF NOT EXISTS "IX_MobileDevices_WorkspaceId" ON "MobileDevices"("WorkspaceId");
        CREATE INDEX IF NOT EXISTS "IX_MobileDevices_CategoryId" ON "MobileDevices"("CategoryId");
        CREATE TABLE IF NOT EXISTS "SmsReceipts" (
            "UserId" text NOT NULL REFERENCES "AspNetUsers"("Id") ON DELETE CASCADE,
            "Key" character varying(64) NOT NULL, "CreatedAtUtc" timestamp with time zone NOT NULL,
            PRIMARY KEY("UserId", "Key")
        );
        """, ct);
}
