using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace TimeTracking.Api.Migrations
{
    /// <inheritdoc />
    public partial class CoreHoursGroupToleranceCatchUpBackfill : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<DateOnly>(
                name: "CoreHoursBackfillFrom",
                table: "Organizations",
                type: "date",
                nullable: true);

            migrationBuilder.AddColumn<DateTime>(
                name: "CoreHoursChangedAt",
                table: "Organizations",
                type: "timestamp with time zone",
                nullable: true);

            migrationBuilder.AddColumn<int>(
                name: "CoreHoursToleranceMinutes",
                table: "Groups",
                type: "integer",
                nullable: true);

            migrationBuilder.AddColumn<bool>(
                name: "Backfilled",
                table: "CoreHoursChecks",
                type: "boolean",
                nullable: false,
                defaultValue: false);

            migrationBuilder.AddColumn<bool>(
                name: "CheckedLate",
                table: "CoreHoursChecks",
                type: "boolean",
                nullable: false,
                defaultValue: false);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "CoreHoursBackfillFrom",
                table: "Organizations");

            migrationBuilder.DropColumn(
                name: "CoreHoursChangedAt",
                table: "Organizations");

            migrationBuilder.DropColumn(
                name: "CoreHoursToleranceMinutes",
                table: "Groups");

            migrationBuilder.DropColumn(
                name: "Backfilled",
                table: "CoreHoursChecks");

            migrationBuilder.DropColumn(
                name: "CheckedLate",
                table: "CoreHoursChecks");
        }
    }
}
