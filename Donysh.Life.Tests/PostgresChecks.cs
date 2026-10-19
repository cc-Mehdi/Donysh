using Donysh.Life.Data;
using Donysh.Life.Domain;
using Donysh.Life.Features.Scheduling;
using Microsoft.EntityFrameworkCore;
using Npgsql;

internal static class PostgresChecks
{
    public static async Task RunAsync(string adminConnection)
    {
        var databaseName = "donysh_life_test_" + Guid.NewGuid().ToString("N");
        var connection = new NpgsqlConnectionStringBuilder(adminConnection)
        {
            Database = "postgres",
            Pooling = false,
            Timeout = 5,
            CommandTimeout = 15
        };

        await using var admin = new NpgsqlConnection(connection.ConnectionString);
        await admin.OpenAsync();
        await using (var create = new NpgsqlCommand($"CREATE DATABASE \"{databaseName}\"", admin))
            await create.ExecuteNonQueryAsync();

        try
        {
            connection.Database = databaseName;
            var options = new DbContextOptionsBuilder<LifeDbContext>().UseNpgsql(connection.ConnectionString).Options;
            await using var db = new LifeDbContext(options);
            await db.Database.MigrateAsync();

            var profile = new LifeProfile
            {
                Id = Guid.NewGuid(),
                UserSubject = "central-user-id",
                CreatedAtUtc = DateTimeOffset.UtcNow,
                UpdatedAtUtc = DateTimeOffset.UtcNow
            };
            db.Profiles.Add(profile);
            await db.SaveChangesAsync();
            var stored = await db.Profiles.AsNoTracking().SingleAsync();
            if (stored.UserSubject != profile.UserSubject
                || stored.WeekStartsOn != DayOfWeek.Saturday
                || stored.TimeZoneId != "Asia/Tehran")
                throw new Exception("FAIL PostgreSQL Life profile defaults were not preserved");

            var ownerAActivity = new LifeActivity
            {
                Id = Guid.NewGuid(), UserSubject = "owner-a", Title = "مطالعه", NormalizedTitle = "مطالعه",
                ColorHex = "#2563EB", IconKey = "book", IsActive = true,
                CreatedAtUtc = DateTimeOffset.UtcNow, UpdatedAtUtc = DateTimeOffset.UtcNow
            };
            ownerAActivity.WeeklyGoal = new WeeklyGoal
            {
                Id = Guid.NewGuid(), ActivityId = ownerAActivity.Id, UserSubject = "owner-a", TargetMinutes = 420,
                CreatedAtUtc = DateTimeOffset.UtcNow, UpdatedAtUtc = DateTimeOffset.UtcNow
            };
            var ownerBActivity = new LifeActivity
            {
                Id = Guid.NewGuid(), UserSubject = "owner-b", Title = "مطالعه", NormalizedTitle = "مطالعه",
                ColorHex = "#7C3AED", IconKey = "book", IsActive = true,
                CreatedAtUtc = DateTimeOffset.UtcNow, UpdatedAtUtc = DateTimeOffset.UtcNow
            };
            db.Activities.AddRange(ownerAActivity, ownerBActivity);
            await db.SaveChangesAsync();
            if (await db.Activities.CountAsync(item => item.NormalizedTitle == "مطالعه") != 2)
                throw new Exception("FAIL PostgreSQL Life activity titles were not isolated per user");

            var ownerAPattern = CreatePattern(ownerAActivity, "owner-a");
            var ownerBPattern = CreatePattern(ownerBActivity, "owner-b");
            db.SchedulePatterns.AddRange(ownerAPattern, ownerBPattern);
            await db.SaveChangesAsync();

            var conflictingPattern = CreatePattern(ownerAActivity, "owner-a");
            db.SchedulePatterns.Add(conflictingPattern);
            var overlapRejected = false;
            try
            {
                await db.SaveChangesAsync();
            }
            catch (DbUpdateException exception) when (exception.InnerException is PostgresException { SqlState: PostgresErrorCodes.UniqueViolation })
            {
                overlapRejected = true;
                db.ChangeTracker.Clear();
            }
            if (!overlapRejected)
                throw new Exception("FAIL PostgreSQL Life schedule slots allowed an overlapping block");

            var originalDate = DateOnly.FromDateTime(DateTime.UtcNow);
            originalDate = originalDate.AddDays(-(((int)originalDate.DayOfWeek - (int)DayOfWeek.Saturday + 7) % 7));
            db.ScheduleOccurrenceOverrides.Add(new ScheduleOccurrenceOverride
            {
                Id = Guid.NewGuid(), PatternId = ownerAPattern.Id, UserSubject = "owner-a",
                OriginalDate = originalDate, ScheduledDate = originalDate.AddDays(1),
                StartMinute = 600, DurationMinutes = 30,
                CreatedAtUtc = DateTimeOffset.UtcNow, UpdatedAtUtc = DateTimeOffset.UtcNow
            });
            await db.SaveChangesAsync();
            db.ScheduleOccurrenceOverrides.Add(new ScheduleOccurrenceOverride
            {
                Id = Guid.NewGuid(), PatternId = ownerAPattern.Id, UserSubject = "owner-a",
                OriginalDate = originalDate, ScheduledDate = originalDate.AddDays(2),
                StartMinute = 660, DurationMinutes = 30,
                CreatedAtUtc = DateTimeOffset.UtcNow, UpdatedAtUtc = DateTimeOffset.UtcNow
            });
            var duplicateOverrideRejected = false;
            try { await db.SaveChangesAsync(); }
            catch (DbUpdateException exception) when (exception.InnerException is PostgresException { SqlState: PostgresErrorCodes.UniqueViolation })
            {
                duplicateOverrideRejected = true;
                db.ChangeTracker.Clear();
            }
            if (!duplicateOverrideRejected)
                throw new Exception("FAIL PostgreSQL Life allowed duplicate overrides for one occurrence");

            db.ScheduleOccurrenceRecords.Add(new ScheduleOccurrenceRecord
            {
                Id = Guid.NewGuid(), PatternId = ownerAPattern.Id, UserSubject = "owner-a",
                OriginalDate = originalDate, ScheduledDate = originalDate.AddDays(1),
                Status = OccurrenceOutcomeStatus.Partial, PlannedMinutes = 30, ActualMinutes = 20,
                CreatedAtUtc = DateTimeOffset.UtcNow, UpdatedAtUtc = DateTimeOffset.UtcNow
            });
            await db.SaveChangesAsync();
            db.ScheduleOccurrenceRecords.Add(new ScheduleOccurrenceRecord
            {
                Id = Guid.NewGuid(), PatternId = ownerAPattern.Id, UserSubject = "owner-a",
                OriginalDate = originalDate, ScheduledDate = originalDate.AddDays(1),
                Status = OccurrenceOutcomeStatus.Completed, PlannedMinutes = 30, ActualMinutes = 30,
                CreatedAtUtc = DateTimeOffset.UtcNow, UpdatedAtUtc = DateTimeOffset.UtcNow
            });
            var duplicateRecordRejected = false;
            try { await db.SaveChangesAsync(); }
            catch (DbUpdateException exception) when (exception.InnerException is PostgresException { SqlState: PostgresErrorCodes.UniqueViolation })
            {
                duplicateRecordRejected = true;
                db.ChangeTracker.Clear();
            }
            if (!duplicateRecordRejected)
                throw new Exception("FAIL PostgreSQL Life allowed duplicate results for one occurrence");

            db.ActualActivityEntries.Add(new ActualActivityEntry
            {
                Id = Guid.NewGuid(), ActivityId = ownerAActivity.Id, UserSubject = "owner-a",
                OccurredDate = originalDate, StartMinute = 570, ActualMinutes = 45, Note = "ثبت دستی",
                CreatedAtUtc = DateTimeOffset.UtcNow, UpdatedAtUtc = DateTimeOffset.UtcNow
            });
            await db.SaveChangesAsync();
            if (await db.ActualActivityEntries.CountAsync(item => item.UserSubject == "owner-a" && item.ActivityId == ownerAActivity.Id) != 1)
                throw new Exception("FAIL PostgreSQL Life did not preserve manual actual activity ownership");

            await using var verify = new NpgsqlConnection(connection.ConnectionString);
            await verify.OpenAsync();
            await using var command = new NpgsqlCommand("""
                SELECT count(*) = 8
                FROM information_schema.tables
                WHERE table_schema = current_schema()
                  AND table_name IN ('LifeProfiles', 'LifeActivities', 'LifeWeeklyGoals', 'LifeSchedulePatterns', 'LifeScheduleSlots', 'LifeScheduleOccurrenceOverrides', 'LifeScheduleOccurrenceRecords', 'LifeActualActivityEntries')
                """, verify);
            if (!Convert.ToBoolean(await command.ExecuteScalarAsync()))
                throw new Exception("FAIL PostgreSQL Life migration did not create its isolated schema");
            await using var reportingIndex = new NpgsqlCommand("""
                SELECT count(*) = 1
                FROM pg_indexes
                WHERE schemaname = current_schema()
                  AND tablename = 'LifeScheduleOccurrenceRecords'
                  AND indexname = 'IX_LifeScheduleOccurrenceRecords_UserSubject_MovedToDate_Status'
                """, verify);
            if (!Convert.ToBoolean(await reportingIndex.ExecuteScalarAsync()))
                throw new Exception("FAIL PostgreSQL Life migration did not create the moved occurrence reporting index");

            Console.WriteLine("PASS PostgreSQL Life migration creates an isolated schema");
            Console.WriteLine("PASS PostgreSQL Life profile keeps Saturday and Tehran defaults");
            Console.WriteLine("PASS PostgreSQL Life activities and weekly goals preserve per-user ownership");
            Console.WriteLine("PASS PostgreSQL Life schedule slots reject overlaps and remain isolated per user");
            Console.WriteLine("PASS PostgreSQL Life occurrence overrides are unique per recurring occurrence");
            Console.WriteLine("PASS PostgreSQL Life stores one actual result per recurring occurrence");
            Console.WriteLine("PASS PostgreSQL Life stores owned manual actual activities");
            Console.WriteLine("PASS PostgreSQL Life reporting index supports moved occurrence date queries");
        }
        finally
        {
            NpgsqlConnection.ClearAllPools();
            connection.Database = "postgres";
            await using var cleanup = new NpgsqlConnection(connection.ConnectionString);
            await cleanup.OpenAsync();
            await using var drop = new NpgsqlCommand($"DROP DATABASE IF EXISTS \"{databaseName}\" WITH (FORCE)", cleanup);
            await drop.ExecuteNonQueryAsync();
        }
    }

    private static RecurringSchedulePattern CreatePattern(LifeActivity activity, string subject)
    {
        var pattern = new RecurringSchedulePattern
        {
            Id = Guid.NewGuid(), UserSubject = subject, ActivityId = activity.Id,
            DayOfWeek = DayOfWeek.Saturday, StartMinute = 480, DurationMinutes = 60, IsActive = true,
            CreatedAtUtc = DateTimeOffset.UtcNow, UpdatedAtUtc = DateTimeOffset.UtcNow
        };
        pattern.Slots.Add(new RecurringScheduleSlot
        {
            PatternId = pattern.Id, UserSubject = subject, DayOfWeek = DayOfWeek.Saturday, SlotIndex = 16
        });
        pattern.Slots.Add(new RecurringScheduleSlot
        {
            PatternId = pattern.Id, UserSubject = subject, DayOfWeek = DayOfWeek.Saturday, SlotIndex = 17
        });
        return pattern;
    }
}
