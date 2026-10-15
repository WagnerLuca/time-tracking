using System;
using Microsoft.EntityFrameworkCore.Migrations;
using Npgsql.EntityFrameworkCore.PostgreSQL.Metadata;

#nullable disable

namespace TimeTracking.Api.Migrations
{
    /// <inheritdoc />
    public partial class ClosedPeriods : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<bool>(
                name: "InClosedPeriod",
                table: "TimeEntryChanges",
                type: "boolean",
                nullable: false,
                defaultValue: false);

            migrationBuilder.AddColumn<string>(
                name: "Reason",
                table: "TimeEntryChanges",
                type: "character varying(500)",
                maxLength: 500,
                nullable: true);

            migrationBuilder.CreateTable(
                name: "ClosedPeriodEvents",
                columns: table => new
                {
                    Id = table.Column<int>(type: "integer", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    OrganizationId = table.Column<int>(type: "integer", nullable: false),
                    Month = table.Column<DateOnly>(type: "date", nullable: false),
                    Kind = table.Column<int>(type: "integer", nullable: false),
                    UserId = table.Column<int>(type: "integer", nullable: true),
                    At = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    Reason = table.Column<string>(type: "character varying(500)", maxLength: 500, nullable: true),
                    MemberUserId = table.Column<int>(type: "integer", nullable: true),
                    Summary = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_ClosedPeriodEvents", x => x.Id);
                    table.ForeignKey(
                        name: "FK_ClosedPeriodEvents_Organizations_OrganizationId",
                        column: x => x.OrganizationId,
                        principalTable: "Organizations",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "FK_ClosedPeriodEvents_Users_MemberUserId",
                        column: x => x.MemberUserId,
                        principalTable: "Users",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.SetNull);
                    table.ForeignKey(
                        name: "FK_ClosedPeriodEvents_Users_UserId",
                        column: x => x.UserId,
                        principalTable: "Users",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.SetNull);
                });

            migrationBuilder.CreateTable(
                name: "ClosedPeriods",
                columns: table => new
                {
                    Id = table.Column<int>(type: "integer", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    OrganizationId = table.Column<int>(type: "integer", nullable: false),
                    Month = table.Column<DateOnly>(type: "date", nullable: false),
                    ClosedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    ClosedByUserId = table.Column<int>(type: "integer", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_ClosedPeriods", x => x.Id);
                    table.ForeignKey(
                        name: "FK_ClosedPeriods_Organizations_OrganizationId",
                        column: x => x.OrganizationId,
                        principalTable: "Organizations",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "FK_ClosedPeriods_Users_ClosedByUserId",
                        column: x => x.ClosedByUserId,
                        principalTable: "Users",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.SetNull);
                });

            migrationBuilder.CreateIndex(
                name: "IX_ClosedPeriodEvents_MemberUserId",
                table: "ClosedPeriodEvents",
                column: "MemberUserId");

            migrationBuilder.CreateIndex(
                name: "IX_ClosedPeriodEvents_OrganizationId_At",
                table: "ClosedPeriodEvents",
                columns: new[] { "OrganizationId", "At" });

            migrationBuilder.CreateIndex(
                name: "IX_ClosedPeriodEvents_UserId",
                table: "ClosedPeriodEvents",
                column: "UserId");

            migrationBuilder.CreateIndex(
                name: "IX_ClosedPeriods_ClosedByUserId",
                table: "ClosedPeriods",
                column: "ClosedByUserId");

            migrationBuilder.CreateIndex(
                name: "IX_ClosedPeriods_OrganizationId_Month",
                table: "ClosedPeriods",
                columns: new[] { "OrganizationId", "Month" },
                unique: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "ClosedPeriodEvents");

            migrationBuilder.DropTable(
                name: "ClosedPeriods");

            migrationBuilder.DropColumn(
                name: "InClosedPeriod",
                table: "TimeEntryChanges");

            migrationBuilder.DropColumn(
                name: "Reason",
                table: "TimeEntryChanges");
        }
    }
}
