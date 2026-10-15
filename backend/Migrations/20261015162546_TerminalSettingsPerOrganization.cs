using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace TimeTracking.Api.Migrations
{
    /// <inheritdoc />
    public partial class TerminalSettingsPerOrganization : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<int>(
                name: "TerminalDoubleScanSeconds",
                table: "Organizations",
                type: "integer",
                nullable: false,
                defaultValue: 60);

            migrationBuilder.AddColumn<int>(
                name: "TerminalOfflineHours",
                table: "Organizations",
                type: "integer",
                nullable: false,
                defaultValue: 72);

            migrationBuilder.AddColumn<int>(
                name: "TerminalUnknownCardLimit",
                table: "Organizations",
                type: "integer",
                nullable: false,
                defaultValue: 20);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "TerminalDoubleScanSeconds",
                table: "Organizations");

            migrationBuilder.DropColumn(
                name: "TerminalOfflineHours",
                table: "Organizations");

            migrationBuilder.DropColumn(
                name: "TerminalUnknownCardLimit",
                table: "Organizations");
        }
    }
}
