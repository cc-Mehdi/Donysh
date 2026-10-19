using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Donysh.Life.Data.Migrations
{
    /// <inheritdoc />
    public partial class AddLifeActivitiesAndWeeklyGoals : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "LifeActivities",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    UserSubject = table.Column<string>(type: "character varying(450)", maxLength: 450, nullable: false),
                    Title = table.Column<string>(type: "character varying(80)", maxLength: 80, nullable: false),
                    NormalizedTitle = table.Column<string>(type: "character varying(80)", maxLength: 80, nullable: false),
                    ColorHex = table.Column<string>(type: "character varying(7)", maxLength: 7, nullable: false),
                    IconKey = table.Column<string>(type: "character varying(32)", maxLength: 32, nullable: false),
                    Category = table.Column<string>(type: "character varying(60)", maxLength: 60, nullable: true),
                    IsActive = table.Column<bool>(type: "boolean", nullable: false),
                    CreatedAtUtc = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    UpdatedAtUtc = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_LifeActivities", x => x.Id);
                    table.UniqueConstraint("AK_LifeActivities_Id_UserSubject", x => new { x.Id, x.UserSubject });
                    table.CheckConstraint("CK_LifeActivities_ColorHex", "\"ColorHex\" ~ '^#[0-9A-F]{6}$'");
                    table.CheckConstraint("CK_LifeActivities_IconKey", "length(\"IconKey\") BETWEEN 1 AND 32");
                });

            migrationBuilder.CreateTable(
                name: "LifeWeeklyGoals",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    ActivityId = table.Column<Guid>(type: "uuid", nullable: false),
                    UserSubject = table.Column<string>(type: "character varying(450)", maxLength: 450, nullable: false),
                    TargetMinutes = table.Column<int>(type: "integer", nullable: false),
                    CreatedAtUtc = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    UpdatedAtUtc = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_LifeWeeklyGoals", x => x.Id);
                    table.CheckConstraint("CK_LifeWeeklyGoals_TargetMinutes", "\"TargetMinutes\" BETWEEN 30 AND 10080");
                    table.ForeignKey(
                        name: "FK_LifeWeeklyGoals_LifeActivities_ActivityId_UserSubject",
                        columns: x => new { x.ActivityId, x.UserSubject },
                        principalTable: "LifeActivities",
                        principalColumns: new[] { "Id", "UserSubject" },
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "IX_LifeActivities_UserSubject_IsActive",
                table: "LifeActivities",
                columns: new[] { "UserSubject", "IsActive" });

            migrationBuilder.CreateIndex(
                name: "IX_LifeActivities_UserSubject_NormalizedTitle",
                table: "LifeActivities",
                columns: new[] { "UserSubject", "NormalizedTitle" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_LifeWeeklyGoals_ActivityId",
                table: "LifeWeeklyGoals",
                column: "ActivityId",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_LifeWeeklyGoals_ActivityId_UserSubject",
                table: "LifeWeeklyGoals",
                columns: new[] { "ActivityId", "UserSubject" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_LifeWeeklyGoals_UserSubject",
                table: "LifeWeeklyGoals",
                column: "UserSubject");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "LifeWeeklyGoals");

            migrationBuilder.DropTable(
                name: "LifeActivities");
        }
    }
}
