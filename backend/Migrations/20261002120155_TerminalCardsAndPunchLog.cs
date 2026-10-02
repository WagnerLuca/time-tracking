using System;
using Microsoft.EntityFrameworkCore.Migrations;
using Npgsql.EntityFrameworkCore.PostgreSQL.Metadata;

#nullable disable

namespace TimeTracking.Api.Migrations
{
    /// <inheritdoc />
    public partial class TerminalCardsAndPunchLog : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<int>(
                name: "Source",
                table: "TimeEntries",
                type: "integer",
                nullable: false,
                defaultValue: 0);

            migrationBuilder.CreateTable(
                name: "BadgeCards",
                columns: table => new
                {
                    Id = table.Column<int>(type: "integer", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    OrganizationId = table.Column<int>(type: "integer", nullable: false),
                    UserId = table.Column<int>(type: "integer", nullable: false),
                    CardHash = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    CardHint = table.Column<string>(type: "character varying(8)", maxLength: 8, nullable: false),
                    Label = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: true),
                    IsBlocked = table.Column<bool>(type: "boolean", nullable: false),
                    CreatedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    LastUsedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_BadgeCards", x => x.Id);
                    table.ForeignKey(
                        name: "FK_BadgeCards_Organizations_OrganizationId",
                        column: x => x.OrganizationId,
                        principalTable: "Organizations",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "FK_BadgeCards_Users_UserId",
                        column: x => x.UserId,
                        principalTable: "Users",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "TerminalPunches",
                columns: table => new
                {
                    Id = table.Column<long>(type: "bigint", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    OrganizationId = table.Column<int>(type: "integer", nullable: false),
                    ApiKeyId = table.Column<int>(type: "integer", nullable: true),
                    BadgeCardId = table.Column<int>(type: "integer", nullable: true),
                    UserId = table.Column<int>(type: "integer", nullable: true),
                    CardHash = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    CardHint = table.Column<string>(type: "character varying(8)", maxLength: 8, nullable: false),
                    Result = table.Column<int>(type: "integer", nullable: false),
                    ScannedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    ReceivedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    ClientPunchId = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: true),
                    TimeEntryId = table.Column<int>(type: "integer", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_TerminalPunches", x => x.Id);
                    table.ForeignKey(
                        name: "FK_TerminalPunches_ApiKeys_ApiKeyId",
                        column: x => x.ApiKeyId,
                        principalTable: "ApiKeys",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.SetNull);
                    table.ForeignKey(
                        name: "FK_TerminalPunches_BadgeCards_BadgeCardId",
                        column: x => x.BadgeCardId,
                        principalTable: "BadgeCards",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.SetNull);
                    table.ForeignKey(
                        name: "FK_TerminalPunches_Organizations_OrganizationId",
                        column: x => x.OrganizationId,
                        principalTable: "Organizations",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "FK_TerminalPunches_TimeEntries_TimeEntryId",
                        column: x => x.TimeEntryId,
                        principalTable: "TimeEntries",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.SetNull);
                    table.ForeignKey(
                        name: "FK_TerminalPunches_Users_UserId",
                        column: x => x.UserId,
                        principalTable: "Users",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.SetNull);
                });

            migrationBuilder.CreateIndex(
                name: "IX_UserOrganizations_OrganizationId",
                table: "UserOrganizations",
                column: "OrganizationId");

            migrationBuilder.CreateIndex(
                name: "IX_BadgeCards_OrganizationId_CardHash",
                table: "BadgeCards",
                columns: new[] { "OrganizationId", "CardHash" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_BadgeCards_UserId",
                table: "BadgeCards",
                column: "UserId");

            // Each member's old single badge ID becomes a card: only the hash of the normalised value
            // (trim, uppercase, without ':' and spaces; the same as CardIds) and its last 4 characters.
            // Values that collide after normalising keep the first one.
            migrationBuilder.Sql(@"
                INSERT INTO ""BadgeCards"" (""OrganizationId"", ""UserId"", ""CardHash"", ""CardHint"", ""IsBlocked"", ""CreatedAt"")
                SELECT ""OrganizationId"", ""UserId"",
                       upper(encode(sha256(convert_to(norm, 'UTF8')), 'hex')),
                       right(norm, 4), false, now()
                FROM (
                    SELECT ""OrganizationId"", ""UserId"",
                           upper(regexp_replace(btrim(""BadgeId""), '[:[:space:]]', '', 'g')) AS norm
                    FROM ""UserOrganizations""
                    WHERE ""IsActive"" AND ""BadgeId"" IS NOT NULL
                    ORDER BY ""Id""
                ) badges
                WHERE norm <> ''
                ON CONFLICT (""OrganizationId"", ""CardHash"") DO NOTHING;");

            migrationBuilder.DropIndex(
                name: "IX_UserOrganizations_OrganizationId_BadgeId",
                table: "UserOrganizations");

            migrationBuilder.DropColumn(
                name: "BadgeId",
                table: "UserOrganizations");


            migrationBuilder.CreateIndex(
                name: "IX_TerminalPunches_ApiKeyId_ClientPunchId",
                table: "TerminalPunches",
                columns: new[] { "ApiKeyId", "ClientPunchId" },
                unique: true,
                filter: "\"ClientPunchId\" IS NOT NULL");

            migrationBuilder.CreateIndex(
                name: "IX_TerminalPunches_ApiKeyId_ReceivedAt",
                table: "TerminalPunches",
                columns: new[] { "ApiKeyId", "ReceivedAt" });

            migrationBuilder.CreateIndex(
                name: "IX_TerminalPunches_BadgeCardId",
                table: "TerminalPunches",
                column: "BadgeCardId");

            migrationBuilder.CreateIndex(
                name: "IX_TerminalPunches_OrganizationId_ScannedAt",
                table: "TerminalPunches",
                columns: new[] { "OrganizationId", "ScannedAt" });

            migrationBuilder.CreateIndex(
                name: "IX_TerminalPunches_OrganizationId_UserId_ScannedAt",
                table: "TerminalPunches",
                columns: new[] { "OrganizationId", "UserId", "ScannedAt" });

            migrationBuilder.CreateIndex(
                name: "IX_TerminalPunches_TimeEntryId",
                table: "TerminalPunches",
                column: "TimeEntryId");

            migrationBuilder.CreateIndex(
                name: "IX_TerminalPunches_UserId",
                table: "TerminalPunches",
                column: "UserId");
        }

        /// <summary>The cards cannot go back into BadgeId (only their hashes exist); the column comes back empty.</summary>
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "TerminalPunches");

            migrationBuilder.DropTable(
                name: "BadgeCards");

            migrationBuilder.DropIndex(
                name: "IX_UserOrganizations_OrganizationId",
                table: "UserOrganizations");

            migrationBuilder.DropColumn(
                name: "Source",
                table: "TimeEntries");

            migrationBuilder.AddColumn<string>(
                name: "BadgeId",
                table: "UserOrganizations",
                type: "character varying(100)",
                maxLength: 100,
                nullable: true);

            migrationBuilder.CreateIndex(
                name: "IX_UserOrganizations_OrganizationId_BadgeId",
                table: "UserOrganizations",
                columns: new[] { "OrganizationId", "BadgeId" },
                unique: true);
        }
    }
}
