using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace TimeTracking.Api.Migrations
{
    /// <inheritdoc />
    public partial class PerformanceIndexes : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "IX_OrgRequests_OrganizationId",
                table: "OrgRequests");

            migrationBuilder.CreateIndex(
                name: "IX_TimeEntries_UserId_Failure",
                table: "TimeEntries",
                column: "UserId",
                filter: "\"IsFailure\"");

            migrationBuilder.CreateIndex(
                name: "IX_TerminalPunches_ReceivedAt",
                table: "TerminalPunches",
                column: "ReceivedAt");

            migrationBuilder.CreateIndex(
                name: "IX_RefreshTokens_ExpiresAt",
                table: "RefreshTokens",
                column: "ExpiresAt");

            migrationBuilder.CreateIndex(
                name: "IX_OrgRequests_OrganizationId_CreatedAt",
                table: "OrgRequests",
                columns: new[] { "OrganizationId", "CreatedAt" });

            migrationBuilder.CreateIndex(
                name: "IX_OrgRequests_OrganizationId_Status",
                table: "OrgRequests",
                columns: new[] { "OrganizationId", "Status" });

            migrationBuilder.CreateIndex(
                name: "IX_Notifications_IsRead_CreatedAt",
                table: "Notifications",
                columns: new[] { "IsRead", "CreatedAt" });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "IX_TimeEntries_UserId_Failure",
                table: "TimeEntries");

            migrationBuilder.DropIndex(
                name: "IX_TerminalPunches_ReceivedAt",
                table: "TerminalPunches");

            migrationBuilder.DropIndex(
                name: "IX_RefreshTokens_ExpiresAt",
                table: "RefreshTokens");

            migrationBuilder.DropIndex(
                name: "IX_OrgRequests_OrganizationId_CreatedAt",
                table: "OrgRequests");

            migrationBuilder.DropIndex(
                name: "IX_OrgRequests_OrganizationId_Status",
                table: "OrgRequests");

            migrationBuilder.DropIndex(
                name: "IX_Notifications_IsRead_CreatedAt",
                table: "Notifications");

            migrationBuilder.CreateIndex(
                name: "IX_OrgRequests_OrganizationId",
                table: "OrgRequests",
                column: "OrganizationId");
        }
    }
}
