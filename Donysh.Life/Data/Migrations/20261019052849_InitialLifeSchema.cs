using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Donysh.Life.Data.Migrations
{
    /// <inheritdoc />
    public partial class InitialLifeSchema : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "LifeProfiles",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    UserSubject = table.Column<string>(type: "character varying(450)", maxLength: 450, nullable: false),
                    TimeZoneId = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: false),
                    WeekStartsOn = table.Column<int>(type: "integer", nullable: false),
                    CreatedAtUtc = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    UpdatedAtUtc = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_LifeProfiles", x => x.Id);
                });

            migrationBuilder.CreateIndex(
                name: "IX_LifeProfiles_UserSubject",
                table: "LifeProfiles",
                column: "UserSubject",
                unique: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "LifeProfiles");
        }
    }
}
