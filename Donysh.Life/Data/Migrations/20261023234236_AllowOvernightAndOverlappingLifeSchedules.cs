using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Donysh.Life.Data.Migrations
{
    /// <inheritdoc />
    public partial class AllowOvernightAndOverlappingLifeSchedules : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "IX_LifeScheduleSlots_UserSubject_DayOfWeek_SlotIndex",
                table: "LifeScheduleSlots");

            migrationBuilder.DropCheckConstraint(
                name: "CK_LifeSchedulePatterns_Duration",
                table: "LifeSchedulePatterns");

            migrationBuilder.DropCheckConstraint(
                name: "CK_LifeScheduleOccurrenceOverrides_Duration",
                table: "LifeScheduleOccurrenceOverrides");

            migrationBuilder.CreateIndex(
                name: "IX_LifeScheduleSlots_UserSubject_DayOfWeek_SlotIndex",
                table: "LifeScheduleSlots",
                columns: new[] { "UserSubject", "DayOfWeek", "SlotIndex" });

            migrationBuilder.AddCheckConstraint(
                name: "CK_LifeSchedulePatterns_Duration",
                table: "LifeSchedulePatterns",
                sql: "\"DurationMinutes\" BETWEEN 30 AND 1440 AND \"DurationMinutes\" % 30 = 0");

            migrationBuilder.AddCheckConstraint(
                name: "CK_LifeScheduleOccurrenceOverrides_Duration",
                table: "LifeScheduleOccurrenceOverrides",
                sql: "\"DurationMinutes\" BETWEEN 30 AND 1440 AND \"DurationMinutes\" % 30 = 0");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "IX_LifeScheduleSlots_UserSubject_DayOfWeek_SlotIndex",
                table: "LifeScheduleSlots");

            migrationBuilder.DropCheckConstraint(
                name: "CK_LifeSchedulePatterns_Duration",
                table: "LifeSchedulePatterns");

            migrationBuilder.DropCheckConstraint(
                name: "CK_LifeScheduleOccurrenceOverrides_Duration",
                table: "LifeScheduleOccurrenceOverrides");

            migrationBuilder.CreateIndex(
                name: "IX_LifeScheduleSlots_UserSubject_DayOfWeek_SlotIndex",
                table: "LifeScheduleSlots",
                columns: new[] { "UserSubject", "DayOfWeek", "SlotIndex" },
                unique: true);

            migrationBuilder.AddCheckConstraint(
                name: "CK_LifeSchedulePatterns_Duration",
                table: "LifeSchedulePatterns",
                sql: "\"DurationMinutes\" BETWEEN 30 AND 1440 AND \"DurationMinutes\" % 30 = 0 AND \"StartMinute\" + \"DurationMinutes\" <= 1440");

            migrationBuilder.AddCheckConstraint(
                name: "CK_LifeScheduleOccurrenceOverrides_Duration",
                table: "LifeScheduleOccurrenceOverrides",
                sql: "\"DurationMinutes\" BETWEEN 30 AND 1440 AND \"DurationMinutes\" % 30 = 0 AND \"StartMinute\" + \"DurationMinutes\" <= 1440");
        }
    }
}
