using System;
using Microsoft.EntityFrameworkCore.Migrations;
using Npgsql.EntityFrameworkCore.PostgreSQL.Metadata;

#nullable disable

namespace TimeTracking.Api.Migrations
{
    /// <inheritdoc />
    public partial class AbsenceRequestsAndCustomTypes : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<int>(
                name: "AbsenceSelfServiceMode",
                table: "Organizations",
                type: "integer",
                nullable: false,
                defaultValue: 0);

            migrationBuilder.AddColumn<int>(
                name: "AbsenceSelfServiceMode",
                table: "Groups",
                type: "integer",
                nullable: true);

            migrationBuilder.AddColumn<int>(
                name: "CreatedByUserId",
                table: "AbsenceDays",
                type: "integer",
                nullable: true);

            migrationBuilder.AddColumn<int>(
                name: "CreatedViaRequestId",
                table: "AbsenceDays",
                type: "integer",
                nullable: true);

            migrationBuilder.AddColumn<int>(
                name: "CustomTypeId",
                table: "AbsenceDays",
                type: "integer",
                nullable: true);

            migrationBuilder.CreateTable(
                name: "AbsenceTypeDefinitions",
                columns: table => new
                {
                    Id = table.Column<int>(type: "integer", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    OrganizationId = table.Column<int>(type: "integer", nullable: false),
                    Name = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: false),
                    Color = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: true),
                    CreditMode = table.Column<int>(type: "integer", nullable: false),
                    CountsAsVacation = table.Column<bool>(type: "boolean", nullable: false),
                    IsActive = table.Column<bool>(type: "boolean", nullable: false, defaultValue: true),
                    CreatedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false, defaultValueSql: "CURRENT_TIMESTAMP")
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_AbsenceTypeDefinitions", x => x.Id);
                    table.ForeignKey(
                        name: "FK_AbsenceTypeDefinitions_Organizations_OrganizationId",
                        column: x => x.OrganizationId,
                        principalTable: "Organizations",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "IX_AbsenceDays_CustomTypeId",
                table: "AbsenceDays",
                column: "CustomTypeId");

            migrationBuilder.CreateIndex(
                name: "IX_AbsenceTypeDefinitions_OrganizationId_Name",
                table: "AbsenceTypeDefinitions",
                columns: new[] { "OrganizationId", "Name" },
                unique: true);

            migrationBuilder.AddForeignKey(
                name: "FK_AbsenceDays_AbsenceTypeDefinitions_CustomTypeId",
                table: "AbsenceDays",
                column: "CustomTypeId",
                principalTable: "AbsenceTypeDefinitions",
                principalColumn: "Id",
                onDelete: ReferentialAction.SetNull);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "FK_AbsenceDays_AbsenceTypeDefinitions_CustomTypeId",
                table: "AbsenceDays");

            migrationBuilder.DropTable(
                name: "AbsenceTypeDefinitions");

            migrationBuilder.DropIndex(
                name: "IX_AbsenceDays_CustomTypeId",
                table: "AbsenceDays");

            migrationBuilder.DropColumn(
                name: "AbsenceSelfServiceMode",
                table: "Organizations");

            migrationBuilder.DropColumn(
                name: "AbsenceSelfServiceMode",
                table: "Groups");

            migrationBuilder.DropColumn(
                name: "CreatedByUserId",
                table: "AbsenceDays");

            migrationBuilder.DropColumn(
                name: "CreatedViaRequestId",
                table: "AbsenceDays");

            migrationBuilder.DropColumn(
                name: "CustomTypeId",
                table: "AbsenceDays");
        }
    }
}
