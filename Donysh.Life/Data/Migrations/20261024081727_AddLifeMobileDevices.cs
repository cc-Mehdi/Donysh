using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Donysh.Life.Data.Migrations
{
    /// <inheritdoc />
    public partial class AddLifeMobileDevices : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "LifeMobileDevices",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    UserSubject = table.Column<string>(type: "character varying(450)", maxLength: 450, nullable: false),
                    Name = table.Column<string>(type: "character varying(80)", maxLength: 80, nullable: false),
                    PairCodeHash = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: true),
                    TokenHash = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: true),
                    PairExpiresAtUtc = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    CreatedAtUtc = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    LastSeenAtUtc = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    Revoked = table.Column<bool>(type: "boolean", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_LifeMobileDevices", x => x.Id);
                });

            migrationBuilder.CreateIndex(
                name: "IX_LifeMobileDevices_PairCodeHash",
                table: "LifeMobileDevices",
                column: "PairCodeHash",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_LifeMobileDevices_TokenHash",
                table: "LifeMobileDevices",
                column: "TokenHash",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_LifeMobileDevices_UserSubject_CreatedAtUtc",
                table: "LifeMobileDevices",
                columns: new[] { "UserSubject", "CreatedAtUtc" });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "LifeMobileDevices");
        }
    }
}
