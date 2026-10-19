using System;
using Microsoft.EntityFrameworkCore.Migrations;
using Npgsql.EntityFrameworkCore.PostgreSQL.Metadata;

#nullable disable

namespace TimeTracking.Api.Migrations
{
    /// <summary>
    /// Overtime balance checkpoints (Phase 2 of docs/PERFORMANCE_PLAN.md): the derived table, the member and organization
    /// version counters that invalidate it, the pre-warm marker, and RLS like TimeEntries.
    /// </summary>
    public partial class BalanceCheckpoints : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<int>(
                name: "BalanceVersion",
                table: "UserOrganizations",
                type: "integer",
                nullable: false,
                defaultValue: 0);

            migrationBuilder.AddColumn<int>(
                name: "BalanceVersion",
                table: "Organizations",
                type: "integer",
                nullable: false,
                defaultValue: 0);

            migrationBuilder.AddColumn<DateOnly>(
                name: "BalanceWarmedMonth",
                table: "Organizations",
                type: "date",
                nullable: true);

            migrationBuilder.CreateTable(
                name: "BalanceCheckpoints",
                columns: table => new
                {
                    Id = table.Column<int>(type: "integer", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    OrganizationId = table.Column<int>(type: "integer", nullable: false),
                    UserId = table.Column<int>(type: "integer", nullable: false),
                    Month = table.Column<DateOnly>(type: "date", nullable: false),
                    FirstDay = table.Column<DateOnly>(type: "date", nullable: false),
                    OvertimeDelta = table.Column<double>(type: "double precision", nullable: false),
                    Version = table.Column<int>(type: "integer", nullable: false),
                    OrgVersion = table.Column<int>(type: "integer", nullable: false),
                    Algorithm = table.Column<int>(type: "integer", nullable: false),
                    ComputedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_BalanceCheckpoints", x => x.Id);
                    table.ForeignKey(
                        name: "FK_BalanceCheckpoints_Organizations_OrganizationId",
                        column: x => x.OrganizationId,
                        principalTable: "Organizations",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "FK_BalanceCheckpoints_Users_UserId",
                        column: x => x.UserId,
                        principalTable: "Users",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "IX_BalanceCheckpoints_OrganizationId_UserId_Month",
                table: "BalanceCheckpoints",
                columns: new[] { "OrganizationId", "UserId", "Month" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_BalanceCheckpoints_UserId",
                table: "BalanceCheckpoints",
                column: "UserId");

            // Same row-level security as TimeEntries: the owner and active members of the organization
            migrationBuilder.Sql(EnableRowLevelSecurity.Policy("BalanceCheckpoints"));
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql(EnableRowLevelSecurity.DropPolicy("BalanceCheckpoints"));

            migrationBuilder.DropTable(
                name: "BalanceCheckpoints");

            migrationBuilder.DropColumn(
                name: "BalanceVersion",
                table: "UserOrganizations");

            migrationBuilder.DropColumn(
                name: "BalanceVersion",
                table: "Organizations");

            migrationBuilder.DropColumn(
                name: "BalanceWarmedMonth",
                table: "Organizations");
        }
    }
}
