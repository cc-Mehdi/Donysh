using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Donysh.Life.Data.Migrations
{
    /// <inheritdoc />
    public partial class AddWeeklySchedulePatterns : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "LifeSchedulePatterns",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    UserSubject = table.Column<string>(type: "character varying(450)", maxLength: 450, nullable: false),
                    ActivityId = table.Column<Guid>(type: "uuid", nullable: false),
                    DayOfWeek = table.Column<int>(type: "integer", nullable: false),
                    StartMinute = table.Column<int>(type: "integer", nullable: false),
                    DurationMinutes = table.Column<int>(type: "integer", nullable: false),
                    Note = table.Column<string>(type: "character varying(160)", maxLength: 160, nullable: true),
                    IsActive = table.Column<bool>(type: "boolean", nullable: false),
                    CreatedAtUtc = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    UpdatedAtUtc = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_LifeSchedulePatterns", x => x.Id);
                    table.UniqueConstraint("AK_LifeSchedulePatterns_Id_UserSubject", x => new { x.Id, x.UserSubject });
                    table.CheckConstraint("CK_LifeSchedulePatterns_DayOfWeek", "\"DayOfWeek\" BETWEEN 0 AND 6");
                    table.CheckConstraint("CK_LifeSchedulePatterns_Duration", "\"DurationMinutes\" BETWEEN 30 AND 1440 AND \"DurationMinutes\" % 30 = 0 AND \"StartMinute\" + \"DurationMinutes\" <= 1440");
                    table.CheckConstraint("CK_LifeSchedulePatterns_StartMinute", "\"StartMinute\" BETWEEN 0 AND 1410 AND \"StartMinute\" % 30 = 0");
                    table.ForeignKey(
                        name: "FK_LifeSchedulePatterns_LifeActivities_ActivityId_UserSubject",
                        columns: x => new { x.ActivityId, x.UserSubject },
                        principalTable: "LifeActivities",
                        principalColumns: new[] { "Id", "UserSubject" },
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "LifeScheduleSlots",
                columns: table => new
                {
                    PatternId = table.Column<Guid>(type: "uuid", nullable: false),
                    SlotIndex = table.Column<int>(type: "integer", nullable: false),
                    UserSubject = table.Column<string>(type: "character varying(450)", maxLength: 450, nullable: false),
                    DayOfWeek = table.Column<int>(type: "integer", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_LifeScheduleSlots", x => new { x.PatternId, x.SlotIndex });
                    table.CheckConstraint("CK_LifeScheduleSlots_DayOfWeek", "\"DayOfWeek\" BETWEEN 0 AND 6");
                    table.CheckConstraint("CK_LifeScheduleSlots_SlotIndex", "\"SlotIndex\" BETWEEN 0 AND 47");
                    table.ForeignKey(
                        name: "FK_LifeScheduleSlots_LifeSchedulePatterns_PatternId_UserSubject",
                        columns: x => new { x.PatternId, x.UserSubject },
                        principalTable: "LifeSchedulePatterns",
                        principalColumns: new[] { "Id", "UserSubject" },
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "IX_LifeSchedulePatterns_ActivityId_UserSubject",
                table: "LifeSchedulePatterns",
                columns: new[] { "ActivityId", "UserSubject" });

            migrationBuilder.CreateIndex(
                name: "IX_LifeSchedulePatterns_UserSubject_DayOfWeek_StartMinute",
                table: "LifeSchedulePatterns",
                columns: new[] { "UserSubject", "DayOfWeek", "StartMinute" });

            migrationBuilder.CreateIndex(
                name: "IX_LifeScheduleSlots_PatternId_UserSubject",
                table: "LifeScheduleSlots",
                columns: new[] { "PatternId", "UserSubject" });

            migrationBuilder.CreateIndex(
                name: "IX_LifeScheduleSlots_UserSubject_DayOfWeek_SlotIndex",
                table: "LifeScheduleSlots",
                columns: new[] { "UserSubject", "DayOfWeek", "SlotIndex" },
                unique: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "LifeScheduleSlots");

            migrationBuilder.DropTable(
                name: "LifeSchedulePatterns");
        }
    }
}
