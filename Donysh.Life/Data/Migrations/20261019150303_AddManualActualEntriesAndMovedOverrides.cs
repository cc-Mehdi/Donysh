using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Donysh.Life.Data.Migrations
{
    /// <inheritdoc />
    public partial class AddManualActualEntriesAndMovedOverrides : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<bool>(
                name: "CreatedByOutcome",
                table: "LifeScheduleOccurrenceOverrides",
                type: "boolean",
                nullable: false,
                defaultValue: false);

            migrationBuilder.CreateTable(
                name: "LifeActualActivityEntries",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    UserSubject = table.Column<string>(type: "character varying(450)", maxLength: 450, nullable: false),
                    ActivityId = table.Column<Guid>(type: "uuid", nullable: false),
                    OccurredDate = table.Column<DateOnly>(type: "date", nullable: false),
                    StartMinute = table.Column<int>(type: "integer", nullable: true),
                    ActualMinutes = table.Column<int>(type: "integer", nullable: false),
                    Note = table.Column<string>(type: "character varying(160)", maxLength: 160, nullable: true),
                    CreatedAtUtc = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    UpdatedAtUtc = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_LifeActualActivityEntries", x => x.Id);
                    table.CheckConstraint("CK_LifeActualActivityEntries_ActualMinutes", "\"ActualMinutes\" BETWEEN 1 AND 1440");
                    table.CheckConstraint("CK_LifeActualActivityEntries_StartMinute", "\"StartMinute\" IS NULL OR \"StartMinute\" BETWEEN 0 AND 1439");
                    table.ForeignKey(
                        name: "FK_LifeActualActivityEntries_LifeActivities_ActivityId_UserSub~",
                        columns: x => new { x.ActivityId, x.UserSubject },
                        principalTable: "LifeActivities",
                        principalColumns: new[] { "Id", "UserSubject" },
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateIndex(
                name: "IX_LifeActualActivityEntries_ActivityId_UserSubject",
                table: "LifeActualActivityEntries",
                columns: new[] { "ActivityId", "UserSubject" });

            migrationBuilder.CreateIndex(
                name: "IX_LifeActualActivityEntries_UserSubject_OccurredDate",
                table: "LifeActualActivityEntries",
                columns: new[] { "UserSubject", "OccurredDate" });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "LifeActualActivityEntries");

            migrationBuilder.DropColumn(
                name: "CreatedByOutcome",
                table: "LifeScheduleOccurrenceOverrides");
        }
    }
}
