using Donysh.Life.Domain;
using Microsoft.EntityFrameworkCore;

namespace Donysh.Life.Data;

public sealed class LifeDbContext(DbContextOptions<LifeDbContext> options) : DbContext(options)
{
    public DbSet<LifeProfile> Profiles => Set<LifeProfile>();
    public DbSet<LifeActivity> Activities => Set<LifeActivity>();
    public DbSet<WeeklyGoal> WeeklyGoals => Set<WeeklyGoal>();
    public DbSet<RecurringSchedulePattern> SchedulePatterns => Set<RecurringSchedulePattern>();
    public DbSet<RecurringScheduleSlot> ScheduleSlots => Set<RecurringScheduleSlot>();
    public DbSet<ScheduleOccurrenceOverride> ScheduleOccurrenceOverrides => Set<ScheduleOccurrenceOverride>();
    public DbSet<ScheduleOccurrenceRecord> ScheduleOccurrenceRecords => Set<ScheduleOccurrenceRecord>();
    public DbSet<ActualActivityEntry> ActualActivityEntries => Set<ActualActivityEntry>();
    public DbSet<LifeMobileDevice> MobileDevices => Set<LifeMobileDevice>();

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        var mobile = modelBuilder.Entity<LifeMobileDevice>();
        mobile.ToTable("LifeMobileDevices");
        mobile.HasKey(item => item.Id);
        mobile.Property(item => item.UserSubject).HasMaxLength(450).IsRequired();
        mobile.Property(item => item.Name).HasMaxLength(80).IsRequired();
        mobile.Property(item => item.PairCodeHash).HasMaxLength(64);
        mobile.Property(item => item.TokenHash).HasMaxLength(64);
        mobile.HasIndex(item => item.PairCodeHash).IsUnique();
        mobile.HasIndex(item => item.TokenHash).IsUnique();
        mobile.HasIndex(item => new { item.UserSubject, item.CreatedAtUtc });
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

        var schedulePattern = modelBuilder.Entity<RecurringSchedulePattern>();
        schedulePattern.ToTable("LifeSchedulePatterns", table =>
        {
            table.HasCheckConstraint("CK_LifeSchedulePatterns_DayOfWeek", "\"DayOfWeek\" BETWEEN 0 AND 6");
            table.HasCheckConstraint("CK_LifeSchedulePatterns_StartMinute", "\"StartMinute\" BETWEEN 0 AND 1410 AND \"StartMinute\" % 30 = 0");
            table.HasCheckConstraint("CK_LifeSchedulePatterns_Duration", "\"DurationMinutes\" BETWEEN 30 AND 1440 AND \"DurationMinutes\" % 30 = 0");
            table.HasCheckConstraint("CK_LifeSchedulePatterns_Reminder", "\"ReminderMinutesBefore\" IS NULL OR \"ReminderMinutesBefore\" BETWEEN 0 AND 120");
        });
        schedulePattern.HasKey(item => item.Id);
        schedulePattern.Property(item => item.UserSubject).HasMaxLength(450).IsRequired();
        schedulePattern.Property(item => item.Note).HasMaxLength(160);
        schedulePattern.HasIndex(item => new { item.UserSubject, item.DayOfWeek, item.StartMinute });
        schedulePattern.HasAlternateKey(item => new { item.Id, item.UserSubject });
        schedulePattern.HasOne(item => item.Activity)
            .WithMany(item => item.SchedulePatterns)
            .HasPrincipalKey(item => new { item.Id, item.UserSubject })
            .HasForeignKey(item => new { item.ActivityId, item.UserSubject })
            .OnDelete(DeleteBehavior.Cascade);

        var scheduleSlot = modelBuilder.Entity<RecurringScheduleSlot>();
        scheduleSlot.ToTable("LifeScheduleSlots", table =>
        {
            table.HasCheckConstraint("CK_LifeScheduleSlots_DayOfWeek", "\"DayOfWeek\" BETWEEN 0 AND 6");
            table.HasCheckConstraint("CK_LifeScheduleSlots_SlotIndex", "\"SlotIndex\" BETWEEN 0 AND 47");
        });
        scheduleSlot.HasKey(item => new { item.PatternId, item.SlotIndex });
        scheduleSlot.Property(item => item.UserSubject).HasMaxLength(450).IsRequired();
        scheduleSlot.HasIndex(item => new { item.UserSubject, item.DayOfWeek, item.SlotIndex });
        scheduleSlot.HasOne(item => item.Pattern)
            .WithMany(item => item.Slots)
            .HasPrincipalKey(item => new { item.Id, item.UserSubject })
            .HasForeignKey(item => new { item.PatternId, item.UserSubject })
            .OnDelete(DeleteBehavior.Cascade);

        var occurrenceOverride = modelBuilder.Entity<ScheduleOccurrenceOverride>();
        occurrenceOverride.ToTable("LifeScheduleOccurrenceOverrides", table =>
        {
            table.HasCheckConstraint("CK_LifeScheduleOccurrenceOverrides_StartMinute", "\"StartMinute\" BETWEEN 0 AND 1410 AND \"StartMinute\" % 30 = 0");
            table.HasCheckConstraint("CK_LifeScheduleOccurrenceOverrides_Duration", "\"DurationMinutes\" BETWEEN 30 AND 1440 AND \"DurationMinutes\" % 30 = 0");
        });
        occurrenceOverride.HasKey(item => item.Id);
        occurrenceOverride.Property(item => item.UserSubject).HasMaxLength(450).IsRequired();
        occurrenceOverride.HasIndex(item => new { item.UserSubject, item.PatternId, item.OriginalDate }).IsUnique();
        occurrenceOverride.HasIndex(item => new { item.UserSubject, item.ScheduledDate, item.StartMinute });
        occurrenceOverride.HasOne(item => item.Pattern)
            .WithMany(item => item.OccurrenceOverrides)
            .HasPrincipalKey(item => new { item.Id, item.UserSubject })
            .HasForeignKey(item => new { item.PatternId, item.UserSubject })
            .OnDelete(DeleteBehavior.Cascade);

        var occurrenceRecord = modelBuilder.Entity<ScheduleOccurrenceRecord>();
        occurrenceRecord.ToTable("LifeScheduleOccurrenceRecords", table =>
        {
            table.HasCheckConstraint("CK_LifeScheduleOccurrenceRecords_Status", "\"Status\" BETWEEN 1 AND 4");
            table.HasCheckConstraint("CK_LifeScheduleOccurrenceRecords_PlannedMinutes", "\"PlannedMinutes\" BETWEEN 30 AND 1440");
            table.HasCheckConstraint("CK_LifeScheduleOccurrenceRecords_ActualMinutes", "\"ActualMinutes\" IS NULL OR \"ActualMinutes\" BETWEEN 0 AND 1440");
            table.HasCheckConstraint("CK_LifeScheduleOccurrenceRecords_MovedDate", "(\"Status\" = 4 AND \"MovedToDate\" IS NOT NULL) OR (\"Status\" <> 4 AND \"MovedToDate\" IS NULL)");
        });
        occurrenceRecord.HasKey(item => item.Id);
        occurrenceRecord.Property(item => item.UserSubject).HasMaxLength(450).IsRequired();
        occurrenceRecord.Property(item => item.Note).HasMaxLength(160);
        occurrenceRecord.HasIndex(item => new { item.UserSubject, item.PatternId, item.OriginalDate }).IsUnique();
        occurrenceRecord.HasIndex(item => new { item.UserSubject, item.ScheduledDate, item.Status });
        occurrenceRecord.HasIndex(item => new { item.UserSubject, item.MovedToDate, item.Status });
        occurrenceRecord.HasOne(item => item.Pattern)
            .WithMany(item => item.OccurrenceRecords)
            .HasPrincipalKey(item => new { item.Id, item.UserSubject })
            .HasForeignKey(item => new { item.PatternId, item.UserSubject })
            .OnDelete(DeleteBehavior.Restrict);

        var actualEntry = modelBuilder.Entity<ActualActivityEntry>();
        actualEntry.ToTable("LifeActualActivityEntries", table =>
        {
            table.HasCheckConstraint("CK_LifeActualActivityEntries_StartMinute", "\"StartMinute\" IS NULL OR \"StartMinute\" BETWEEN 0 AND 1439");
            table.HasCheckConstraint("CK_LifeActualActivityEntries_ActualMinutes", "\"ActualMinutes\" BETWEEN 1 AND 1440");
        });
        actualEntry.HasKey(item => item.Id);
        actualEntry.Property(item => item.UserSubject).HasMaxLength(450).IsRequired();
        actualEntry.Property(item => item.Note).HasMaxLength(160);
        actualEntry.HasIndex(item => new { item.UserSubject, item.OccurredDate });
        actualEntry.HasOne(item => item.Activity)
            .WithMany(item => item.ActualEntries)
            .HasPrincipalKey(item => new { item.Id, item.UserSubject })
            .HasForeignKey(item => new { item.ActivityId, item.UserSubject })
            .OnDelete(DeleteBehavior.Restrict);
    }
}
