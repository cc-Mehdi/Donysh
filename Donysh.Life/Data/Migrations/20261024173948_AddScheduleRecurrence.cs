using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Donysh.Life.Data.Migrations
{
    /// <inheritdoc />
    public partial class AddScheduleRecurrence : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<int>(
                name: "RepeatCount",
                table: "LifeSchedulePatterns",
                type: "integer",
                nullable: true);

            migrationBuilder.AddColumn<int>(
                name: "RepeatDaysMask",
                table: "LifeSchedulePatterns",
                type: "integer",
                nullable: false,
                defaultValue: 0);

            migrationBuilder.AddColumn<string>(
                name: "RepeatFrequency",
                table: "LifeSchedulePatterns",
                type: "character varying(12)",
                maxLength: 12,
                nullable: false,
                defaultValue: "weekly");

            migrationBuilder.AddColumn<int>(
                name: "RepeatInterval",
                table: "LifeSchedulePatterns",
                type: "integer",
                nullable: false,
                defaultValue: 1);

            migrationBuilder.AddColumn<DateOnly>(
                name: "RepeatUntil",
                table: "LifeSchedulePatterns",
                type: "date",
                nullable: true);

            migrationBuilder.AddColumn<DateOnly>(
                name: "StartsOn",
                table: "LifeSchedulePatterns",
                type: "date",
                nullable: true);

            migrationBuilder.AddCheckConstraint(
                name: "CK_LifeSchedulePatterns_RepeatCount",
                table: "LifeSchedulePatterns",
                sql: "\"RepeatCount\" IS NULL OR \"RepeatCount\" BETWEEN 1 AND 730");

            migrationBuilder.AddCheckConstraint(
                name: "CK_LifeSchedulePatterns_RepeatDates",
                table: "LifeSchedulePatterns",
                sql: "\"RepeatUntil\" IS NULL OR (\"StartsOn\" IS NOT NULL AND \"RepeatUntil\" >= \"StartsOn\")");

            migrationBuilder.AddCheckConstraint(
                name: "CK_LifeSchedulePatterns_RepeatDaysMask",
                table: "LifeSchedulePatterns",
                sql: "\"RepeatDaysMask\" BETWEEN 0 AND 127");

            migrationBuilder.AddCheckConstraint(
                name: "CK_LifeSchedulePatterns_RepeatFrequency",
                table: "LifeSchedulePatterns",
                sql: "\"RepeatFrequency\" IN ('none', 'daily', 'weekly', 'monthly', 'yearly')");

            migrationBuilder.AddCheckConstraint(
                name: "CK_LifeSchedulePatterns_RepeatInterval",
                table: "LifeSchedulePatterns",
                sql: "\"RepeatInterval\" BETWEEN 1 AND 52");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropCheckConstraint(
                name: "CK_LifeSchedulePatterns_RepeatCount",
                table: "LifeSchedulePatterns");

            migrationBuilder.DropCheckConstraint(
                name: "CK_LifeSchedulePatterns_RepeatDates",
                table: "LifeSchedulePatterns");

            migrationBuilder.DropCheckConstraint(
                name: "CK_LifeSchedulePatterns_RepeatDaysMask",
                table: "LifeSchedulePatterns");

            migrationBuilder.DropCheckConstraint(
                name: "CK_LifeSchedulePatterns_RepeatFrequency",
                table: "LifeSchedulePatterns");

            migrationBuilder.DropCheckConstraint(
                name: "CK_LifeSchedulePatterns_RepeatInterval",
                table: "LifeSchedulePatterns");

            migrationBuilder.DropColumn(
                name: "RepeatCount",
                table: "LifeSchedulePatterns");

            migrationBuilder.DropColumn(
                name: "RepeatDaysMask",
                table: "LifeSchedulePatterns");

            migrationBuilder.DropColumn(
                name: "RepeatFrequency",
                table: "LifeSchedulePatterns");

            migrationBuilder.DropColumn(
                name: "RepeatInterval",
                table: "LifeSchedulePatterns");

            migrationBuilder.DropColumn(
                name: "RepeatUntil",
                table: "LifeSchedulePatterns");

            migrationBuilder.DropColumn(
                name: "StartsOn",
                table: "LifeSchedulePatterns");
        }
    }
}
