using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Donysh.Life.Data.Migrations
{
    /// <inheritdoc />
    public partial class AddScheduleOccurrenceRecords : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "LifeScheduleOccurrenceRecords",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    PatternId = table.Column<Guid>(type: "uuid", nullable: false),
                    UserSubject = table.Column<string>(type: "character varying(450)", maxLength: 450, nullable: false),
                    OriginalDate = table.Column<DateOnly>(type: "date", nullable: false),
                    ScheduledDate = table.Column<DateOnly>(type: "date", nullable: false),
                    Status = table.Column<int>(type: "integer", nullable: false),
                    PlannedMinutes = table.Column<int>(type: "integer", nullable: false),
                    ActualMinutes = table.Column<int>(type: "integer", nullable: true),
                    MovedToDate = table.Column<DateOnly>(type: "date", nullable: true),
                    Note = table.Column<string>(type: "character varying(160)", maxLength: 160, nullable: true),
                    CreatedAtUtc = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    UpdatedAtUtc = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_LifeScheduleOccurrenceRecords", x => x.Id);
                    table.CheckConstraint("CK_LifeScheduleOccurrenceRecords_ActualMinutes", "\"ActualMinutes\" IS NULL OR \"ActualMinutes\" BETWEEN 0 AND 1440");
                    table.CheckConstraint("CK_LifeScheduleOccurrenceRecords_MovedDate", "(\"Status\" = 4 AND \"MovedToDate\" IS NOT NULL) OR (\"Status\" <> 4 AND \"MovedToDate\" IS NULL)");
                    table.CheckConstraint("CK_LifeScheduleOccurrenceRecords_PlannedMinutes", "\"PlannedMinutes\" BETWEEN 30 AND 1440");
                    table.CheckConstraint("CK_LifeScheduleOccurrenceRecords_Status", "\"Status\" BETWEEN 1 AND 4");
                    table.ForeignKey(
                        name: "FK_LifeScheduleOccurrenceRecords_LifeSchedulePatterns_PatternI~",
                        columns: x => new { x.PatternId, x.UserSubject },
                        principalTable: "LifeSchedulePatterns",
                        principalColumns: new[] { "Id", "UserSubject" },
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateIndex(
                name: "IX_LifeScheduleOccurrenceRecords_PatternId_UserSubject",
                table: "LifeScheduleOccurrenceRecords",
                columns: new[] { "PatternId", "UserSubject" });

            migrationBuilder.CreateIndex(
                name: "IX_LifeScheduleOccurrenceRecords_UserSubject_PatternId_Origina~",
                table: "LifeScheduleOccurrenceRecords",
                columns: new[] { "UserSubject", "PatternId", "OriginalDate" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_LifeScheduleOccurrenceRecords_UserSubject_ScheduledDate_Sta~",
                table: "LifeScheduleOccurrenceRecords",
                columns: new[] { "UserSubject", "ScheduledDate", "Status" });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "LifeScheduleOccurrenceRecords");
        }
    }
}
