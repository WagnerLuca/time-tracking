using System;
using Microsoft.EntityFrameworkCore.Migrations;
using Npgsql.EntityFrameworkCore.PostgreSQL.Metadata;

#nullable disable

namespace TimeTracking.Api.Migrations
{
    /// <summary>
    /// Vacation carry-over (Phase 2b of docs/PERFORMANCE_PLAN.md): the organization's rule — existing organizations get
    /// the statutory default (until 31 March) from 2026 on, so no earlier year's figures change and 2026 leftovers carry
    /// into 2027 — and the per-year entitlement/adjustment rows (none created: years without a row fall back to the
    /// member's allowance, exactly as before), with RLS like AbsenceDays.
    /// </summary>
    public partial class VacationCarryOver : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<int>(
                name: "VacationCarryOverExpiryDay",
                table: "Organizations",
                type: "integer",
                nullable: false,
                defaultValue: 31);

            migrationBuilder.AddColumn<int>(
                name: "VacationCarryOverExpiryMonth",
                table: "Organizations",
                type: "integer",
                nullable: false,
                defaultValue: 3);

            migrationBuilder.AddColumn<double>(
                name: "VacationCarryOverMaxDays",
                table: "Organizations",
                type: "double precision",
                nullable: true);

            migrationBuilder.AddColumn<int>(
                name: "VacationCarryOverMode",
                table: "Organizations",
                type: "integer",
                nullable: false,
                defaultValue: 1);

            migrationBuilder.AddColumn<int>(
                name: "VacationCarryOverSince",
                table: "Organizations",
                type: "integer",
                nullable: false,
                defaultValue: 2026);

            migrationBuilder.CreateTable(
                name: "VacationYears",
                columns: table => new
                {
                    Id = table.Column<int>(type: "integer", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    OrganizationId = table.Column<int>(type: "integer", nullable: false),
                    UserId = table.Column<int>(type: "integer", nullable: false),
                    Year = table.Column<int>(type: "integer", nullable: false),
                    Entitlement = table.Column<double>(type: "double precision", nullable: true),
                    Adjustment = table.Column<double>(type: "double precision", nullable: false),
                    AdjustmentNote = table.Column<string>(type: "character varying(500)", maxLength: 500, nullable: true),
                    UpdatedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    UpdatedByUserId = table.Column<int>(type: "integer", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_VacationYears", x => x.Id);
                    table.ForeignKey(
                        name: "FK_VacationYears_Organizations_OrganizationId",
                        column: x => x.OrganizationId,
                        principalTable: "Organizations",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "FK_VacationYears_Users_UpdatedByUserId",
                        column: x => x.UpdatedByUserId,
                        principalTable: "Users",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.SetNull);
                    table.ForeignKey(
                        name: "FK_VacationYears_Users_UserId",
                        column: x => x.UserId,
                        principalTable: "Users",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "IX_VacationYears_OrganizationId_UserId_Year",
                table: "VacationYears",
                columns: new[] { "OrganizationId", "UserId", "Year" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_VacationYears_UpdatedByUserId",
                table: "VacationYears",
                column: "UpdatedByUserId");

            migrationBuilder.CreateIndex(
                name: "IX_VacationYears_UserId",
                table: "VacationYears",
                column: "UserId");

            // Same row-level security as AbsenceDays: the owner and active members of the organization
            migrationBuilder.Sql(EnableRowLevelSecurity.Policy("VacationYears"));
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql(EnableRowLevelSecurity.DropPolicy("VacationYears"));

            migrationBuilder.DropTable(
                name: "VacationYears");

            migrationBuilder.DropColumn(
                name: "VacationCarryOverExpiryDay",
                table: "Organizations");

            migrationBuilder.DropColumn(
                name: "VacationCarryOverExpiryMonth",
                table: "Organizations");

            migrationBuilder.DropColumn(
                name: "VacationCarryOverMaxDays",
                table: "Organizations");

            migrationBuilder.DropColumn(
                name: "VacationCarryOverMode",
                table: "Organizations");

            migrationBuilder.DropColumn(
                name: "VacationCarryOverSince",
                table: "Organizations");
        }
    }
}
