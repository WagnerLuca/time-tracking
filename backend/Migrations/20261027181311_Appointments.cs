using System;
using Microsoft.EntityFrameworkCore.Migrations;
using Npgsql.EntityFrameworkCore.PostgreSQL.Metadata;

#nullable disable

namespace TimeTracking.Api.Migrations
{
    /// <inheritdoc />
    public partial class Appointments : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "Appointments",
                columns: table => new
                {
                    Id = table.Column<int>(type: "integer", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    OrganizationId = table.Column<int>(type: "integer", nullable: false),
                    UserId = table.Column<int>(type: "integer", nullable: false),
                    Date = table.Column<DateOnly>(type: "date", nullable: false),
                    StartTime = table.Column<TimeOnly>(type: "time without time zone", nullable: true),
                    EndTime = table.Column<TimeOnly>(type: "time without time zone", nullable: true),
                    Note = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: true),
                    CreatedByUserId = table.Column<int>(type: "integer", nullable: true),
                    CreatedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    UpdatedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Appointments", x => x.Id);
                    table.ForeignKey(
                        name: "FK_Appointments_Organizations_OrganizationId",
                        column: x => x.OrganizationId,
                        principalTable: "Organizations",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "FK_Appointments_Users_CreatedByUserId",
                        column: x => x.CreatedByUserId,
                        principalTable: "Users",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.SetNull);
                    table.ForeignKey(
                        name: "FK_Appointments_Users_UserId",
                        column: x => x.UserId,
                        principalTable: "Users",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "IX_Appointments_CreatedByUserId",
                table: "Appointments",
                column: "CreatedByUserId");

            migrationBuilder.CreateIndex(
                name: "IX_Appointments_OrganizationId_Date",
                table: "Appointments",
                columns: new[] { "OrganizationId", "Date" });

            migrationBuilder.CreateIndex(
                name: "IX_Appointments_UserId_Date",
                table: "Appointments",
                columns: new[] { "UserId", "Date" });

            // Same row-level security as TimeEntries: the owner and active members of the organization
            migrationBuilder.Sql(EnableRowLevelSecurity.Policy("Appointments"));

            // Visibility matrix kinds Appointments (2048) and AppointmentNotes (4096): the slot starts out
            // visible wherever vacations (16) are — org columns and group allow/deny overrides alike —, notes hidden
            migrationBuilder.Sql("""
                UPDATE "Organizations" SET
                    "VisibleInGroups" = "VisibleInGroups" | CASE WHEN "VisibleInGroups" & 16 <> 0 THEN 2048 ELSE 0 END,
                    "VisibleOutsideGroups" = "VisibleOutsideGroups" | CASE WHEN "VisibleOutsideGroups" & 16 <> 0 THEN 2048 ELSE 0 END;
                UPDATE "Groups" SET
                    "VisibilityAllowInGroups" = "VisibilityAllowInGroups" | CASE WHEN "VisibilityAllowInGroups" & 16 <> 0 THEN 2048 ELSE 0 END,
                    "VisibilityDenyInGroups" = "VisibilityDenyInGroups" | CASE WHEN "VisibilityDenyInGroups" & 16 <> 0 THEN 2048 ELSE 0 END,
                    "VisibilityAllowOutsideGroups" = "VisibilityAllowOutsideGroups" | CASE WHEN "VisibilityAllowOutsideGroups" & 16 <> 0 THEN 2048 ELSE 0 END,
                    "VisibilityDenyOutsideGroups" = "VisibilityDenyOutsideGroups" | CASE WHEN "VisibilityDenyOutsideGroups" & 16 <> 0 THEN 2048 ELSE 0 END;
                """);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql("""
                UPDATE "Organizations" SET "VisibleInGroups" = "VisibleInGroups" & ~6144, "VisibleOutsideGroups" = "VisibleOutsideGroups" & ~6144;
                UPDATE "Groups" SET "VisibilityAllowInGroups" = "VisibilityAllowInGroups" & ~6144, "VisibilityDenyInGroups" = "VisibilityDenyInGroups" & ~6144,
                    "VisibilityAllowOutsideGroups" = "VisibilityAllowOutsideGroups" & ~6144, "VisibilityDenyOutsideGroups" = "VisibilityDenyOutsideGroups" & ~6144;
                """);
            migrationBuilder.Sql(EnableRowLevelSecurity.DropPolicy("Appointments"));

            migrationBuilder.DropTable(
                name: "Appointments");
        }
    }
}
