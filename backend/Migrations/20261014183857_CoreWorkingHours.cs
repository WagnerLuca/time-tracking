using System;
using Microsoft.EntityFrameworkCore.Migrations;
using Npgsql.EntityFrameworkCore.PostgreSQL.Metadata;

#nullable disable

namespace TimeTracking.Api.Migrations
{
    /// <summary>
    /// Core working hours: org setting (existing orgs get 09:00-15:00, 5 min tolerance, off), group
    /// override, and the daily check table, which holds per-user time data and so gets the same
    /// row-level security policy as TimeEntries/AbsenceDays.
    /// </summary>
    public partial class CoreWorkingHours : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<bool>(
                name: "CoreHoursEnabled",
                table: "Organizations",
                type: "boolean",
                nullable: false,
                defaultValue: false);

            migrationBuilder.AddColumn<TimeOnly>(
                name: "CoreHoursEnd",
                table: "Organizations",
                type: "time without time zone",
                nullable: false,
                defaultValue: new TimeOnly(15, 0, 0));

            migrationBuilder.AddColumn<TimeOnly>(
                name: "CoreHoursStart",
                table: "Organizations",
                type: "time without time zone",
                nullable: false,
                defaultValue: new TimeOnly(9, 0, 0));

            migrationBuilder.AddColumn<int>(
                name: "CoreHoursToleranceMinutes",
                table: "Organizations",
                type: "integer",
                nullable: false,
                defaultValue: 5);

            migrationBuilder.AddColumn<TimeOnly>(
                name: "CoreHoursEnd",
                table: "Groups",
                type: "time without time zone",
                nullable: true);

            migrationBuilder.AddColumn<int>(
                name: "CoreHoursMode",
                table: "Groups",
                type: "integer",
                nullable: true);

            migrationBuilder.AddColumn<TimeOnly>(
                name: "CoreHoursStart",
                table: "Groups",
                type: "time without time zone",
                nullable: true);

            migrationBuilder.CreateTable(
                name: "CoreHoursChecks",
                columns: table => new
                {
                    Id = table.Column<int>(type: "integer", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    OrganizationId = table.Column<int>(type: "integer", nullable: false),
                    UserId = table.Column<int>(type: "integer", nullable: false),
                    Date = table.Column<DateOnly>(type: "date", nullable: false),
                    WindowStart = table.Column<TimeOnly>(type: "time without time zone", nullable: false),
                    WindowEnd = table.Column<TimeOnly>(type: "time without time zone", nullable: false),
                    HalfDay = table.Column<bool>(type: "boolean", nullable: false),
                    ToleranceMinutes = table.Column<int>(type: "integer", nullable: false),
                    Issues = table.Column<int>(type: "integer", nullable: false),
                    MissingMinutes = table.Column<int>(type: "integer", nullable: false),
                    CheckedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    NotifiedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_CoreHoursChecks", x => x.Id);
                    table.ForeignKey(
                        name: "FK_CoreHoursChecks_Organizations_OrganizationId",
                        column: x => x.OrganizationId,
                        principalTable: "Organizations",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "FK_CoreHoursChecks_Users_UserId",
                        column: x => x.UserId,
                        principalTable: "Users",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "IX_CoreHoursChecks_OrganizationId_UserId_Date",
                table: "CoreHoursChecks",
                columns: new[] { "OrganizationId", "UserId", "Date" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_CoreHoursChecks_UserId",
                table: "CoreHoursChecks",
                column: "UserId");

            migrationBuilder.Sql(EnableRowLevelSecurity.Policy("CoreHoursChecks"));
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql(EnableRowLevelSecurity.DropPolicy("CoreHoursChecks"));

            migrationBuilder.DropTable(
                name: "CoreHoursChecks");

            migrationBuilder.DropColumn(
                name: "CoreHoursEnabled",
                table: "Organizations");

            migrationBuilder.DropColumn(
                name: "CoreHoursEnd",
                table: "Organizations");

            migrationBuilder.DropColumn(
                name: "CoreHoursStart",
                table: "Organizations");

            migrationBuilder.DropColumn(
                name: "CoreHoursToleranceMinutes",
                table: "Organizations");

            migrationBuilder.DropColumn(
                name: "CoreHoursEnd",
                table: "Groups");

            migrationBuilder.DropColumn(
                name: "CoreHoursMode",
                table: "Groups");

            migrationBuilder.DropColumn(
                name: "CoreHoursStart",
                table: "Groups");
        }
    }
}
