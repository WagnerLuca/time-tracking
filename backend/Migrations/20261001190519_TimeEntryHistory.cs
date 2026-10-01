using System;
using Microsoft.EntityFrameworkCore.Migrations;
using Npgsql.EntityFrameworkCore.PostgreSQL.Metadata;

#nullable disable

namespace TimeTracking.Api.Migrations
{
    /// <inheritdoc />
    public partial class TimeEntryHistory : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "TimeEntryChanges",
                columns: table => new
                {
                    Id = table.Column<int>(type: "integer", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    TimeEntryId = table.Column<int>(type: "integer", nullable: false),
                    UserId = table.Column<int>(type: "integer", nullable: false),
                    OrganizationId = table.Column<int>(type: "integer", nullable: true),
                    ChangedByUserId = table.Column<int>(type: "integer", nullable: true),
                    ChangedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    Kind = table.Column<int>(type: "integer", nullable: false),
                    Changes = table.Column<string>(type: "text", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_TimeEntryChanges", x => x.Id);
                    table.ForeignKey(
                        name: "FK_TimeEntryChanges_Users_ChangedByUserId",
                        column: x => x.ChangedByUserId,
                        principalTable: "Users",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.SetNull);
                    table.ForeignKey(
                        name: "FK_TimeEntryChanges_Users_UserId",
                        column: x => x.UserId,
                        principalTable: "Users",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "IX_TimeEntryChanges_ChangedByUserId",
                table: "TimeEntryChanges",
                column: "ChangedByUserId");

            migrationBuilder.CreateIndex(
                name: "IX_TimeEntryChanges_TimeEntryId_ChangedAt",
                table: "TimeEntryChanges",
                columns: new[] { "TimeEntryId", "ChangedAt" });

            migrationBuilder.CreateIndex(
                name: "IX_TimeEntryChanges_UserId",
                table: "TimeEntryChanges",
                column: "UserId");

            // Same row-level security as TimeEntries: the entry's owner and active members of its organization
            migrationBuilder.Sql(EnableRowLevelSecurity.Policy("TimeEntryChanges"));
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql(EnableRowLevelSecurity.DropPolicy("TimeEntryChanges"));
            migrationBuilder.DropTable(
                name: "TimeEntryChanges");
        }
    }
}
