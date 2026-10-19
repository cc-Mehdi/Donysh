using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Donysh.Life.Data.Migrations
{
    /// <inheritdoc />
    public partial class AddProgressReportingIndex : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateIndex(
                name: "IX_LifeScheduleOccurrenceRecords_UserSubject_MovedToDate_Status",
                table: "LifeScheduleOccurrenceRecords",
                columns: new[] { "UserSubject", "MovedToDate", "Status" });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "IX_LifeScheduleOccurrenceRecords_UserSubject_MovedToDate_Status",
                table: "LifeScheduleOccurrenceRecords");
        }
    }
}
