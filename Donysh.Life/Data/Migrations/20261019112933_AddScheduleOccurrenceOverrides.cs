using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Donysh.Life.Data.Migrations
{
    /// <inheritdoc />
    public partial class AddScheduleOccurrenceOverrides : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "LifeScheduleOccurrenceOverrides",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    PatternId = table.Column<Guid>(type: "uuid", nullable: false),
                    UserSubject = table.Column<string>(type: "character varying(450)", maxLength: 450, nullable: false),
                    OriginalDate = table.Column<DateOnly>(type: "date", nullable: false),
                    ScheduledDate = table.Column<DateOnly>(type: "date", nullable: false),
                    StartMinute = table.Column<int>(type: "integer", nullable: false),
                    DurationMinutes = table.Column<int>(type: "integer", nullable: false),
                    IsCancelled = table.Column<bool>(type: "boolean", nullable: false),
                    CreatedAtUtc = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    UpdatedAtUtc = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_LifeScheduleOccurrenceOverrides", x => x.Id);
                    table.CheckConstraint("CK_LifeScheduleOccurrenceOverrides_Duration", "\"DurationMinutes\" BETWEEN 30 AND 1440 AND \"DurationMinutes\" % 30 = 0 AND \"StartMinute\" + \"DurationMinutes\" <= 1440");
                    table.CheckConstraint("CK_LifeScheduleOccurrenceOverrides_StartMinute", "\"StartMinute\" BETWEEN 0 AND 1410 AND \"StartMinute\" % 30 = 0");
                    table.ForeignKey(
                        name: "FK_LifeScheduleOccurrenceOverrides_LifeSchedulePatterns_Patter~",
                        columns: x => new { x.PatternId, x.UserSubject },
                        principalTable: "LifeSchedulePatterns",
                        principalColumns: new[] { "Id", "UserSubject" },
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "IX_LifeScheduleOccurrenceOverrides_PatternId_UserSubject",
                table: "LifeScheduleOccurrenceOverrides",
                columns: new[] { "PatternId", "UserSubject" });

            migrationBuilder.CreateIndex(
                name: "IX_LifeScheduleOccurrenceOverrides_UserSubject_PatternId_Origi~",
                table: "LifeScheduleOccurrenceOverrides",
                columns: new[] { "UserSubject", "PatternId", "OriginalDate" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_LifeScheduleOccurrenceOverrides_UserSubject_ScheduledDate_S~",
                table: "LifeScheduleOccurrenceOverrides",
                columns: new[] { "UserSubject", "ScheduledDate", "StartMinute" });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "LifeScheduleOccurrenceOverrides");
        }
    }
}
