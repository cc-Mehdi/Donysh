using Donysh.Life.Domain;
using Microsoft.EntityFrameworkCore;

namespace Donysh.Life.Data;

public sealed class LifeDbContext(DbContextOptions<LifeDbContext> options) : DbContext(options)
{
    public DbSet<LifeProfile> Profiles => Set<LifeProfile>();
    public DbSet<LifeActivity> Activities => Set<LifeActivity>();
    public DbSet<WeeklyGoal> WeeklyGoals => Set<WeeklyGoal>();

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        var profile = modelBuilder.Entity<LifeProfile>();
        profile.ToTable("LifeProfiles");
        profile.HasKey(item => item.Id);
        profile.Property(item => item.UserSubject).HasMaxLength(450).IsRequired();
        profile.HasIndex(item => item.UserSubject).IsUnique();
        profile.Property(item => item.TimeZoneId).HasMaxLength(100).IsRequired();

        var activity = modelBuilder.Entity<LifeActivity>();
        activity.ToTable("LifeActivities", table =>
        {
            table.HasCheckConstraint("CK_LifeActivities_ColorHex", "\"ColorHex\" ~ '^#[0-9A-F]{6}$'");
            table.HasCheckConstraint("CK_LifeActivities_IconKey", "length(\"IconKey\") BETWEEN 1 AND 32");
        });
        activity.HasKey(item => item.Id);
        activity.Property(item => item.UserSubject).HasMaxLength(450).IsRequired();
        activity.Property(item => item.Title).HasMaxLength(80).IsRequired();
        activity.Property(item => item.NormalizedTitle).HasMaxLength(80).IsRequired();
        activity.Property(item => item.ColorHex).HasMaxLength(7).IsRequired();
        activity.Property(item => item.IconKey).HasMaxLength(32).IsRequired();
        activity.Property(item => item.Category).HasMaxLength(60);
        activity.HasIndex(item => new { item.UserSubject, item.NormalizedTitle }).IsUnique();
        activity.HasIndex(item => new { item.UserSubject, item.IsActive });
        activity.HasAlternateKey(item => new { item.Id, item.UserSubject });

        var weeklyGoal = modelBuilder.Entity<WeeklyGoal>();
        weeklyGoal.ToTable("LifeWeeklyGoals", table =>
            table.HasCheckConstraint("CK_LifeWeeklyGoals_TargetMinutes", "\"TargetMinutes\" BETWEEN 30 AND 10080"));
        weeklyGoal.HasKey(item => item.Id);
        weeklyGoal.Property(item => item.UserSubject).HasMaxLength(450).IsRequired();
        weeklyGoal.HasIndex(item => item.ActivityId).IsUnique();
        weeklyGoal.HasIndex(item => item.UserSubject);
        weeklyGoal.HasOne(item => item.Activity)
            .WithOne(item => item.WeeklyGoal)
            .HasPrincipalKey<LifeActivity>(item => new { item.Id, item.UserSubject })
            .HasForeignKey<WeeklyGoal>(item => new { item.ActivityId, item.UserSubject })
            .OnDelete(DeleteBehavior.Cascade);
    }
}
