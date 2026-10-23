using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Donysh.Life.Data.Migrations
{
    /// <inheritdoc />
    public partial class AddScheduleReminders : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<int>(
                name: "ReminderMinutesBefore",
                table: "LifeSchedulePatterns",
                type: "integer",
                nullable: true);

            migrationBuilder.AddCheckConstraint(
                name: "CK_LifeSchedulePatterns_Reminder",
                table: "LifeSchedulePatterns",
                sql: "\"ReminderMinutesBefore\" IS NULL OR \"ReminderMinutesBefore\" BETWEEN 0 AND 120");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropCheckConstraint(
                name: "CK_LifeSchedulePatterns_Reminder",
                table: "LifeSchedulePatterns");

            migrationBuilder.DropColumn(
                name: "ReminderMinutesBefore",
                table: "LifeSchedulePatterns");
        }
    }
}
