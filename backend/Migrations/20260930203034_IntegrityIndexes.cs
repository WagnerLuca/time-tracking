using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace TimeTracking.Api.Migrations
{
    /// <inheritdoc />
    public partial class IntegrityIndexes : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            // Existing data could already hold several running timers per user, which the new
            // unique partial index would reject: keep only the newest one running. RLS is forced
            // on TimeEntries, so bypass it for this migration's transaction.
            migrationBuilder.Sql(@"
                SELECT set_config('app.bypass_rls', 'on', true);
                UPDATE ""TimeEntries"" t SET ""IsRunning"" = false, ""EndTime"" = now(), ""UpdatedAt"" = now()
                WHERE t.""IsRunning"" AND EXISTS (
                    SELECT 1 FROM ""TimeEntries"" n
                    WHERE n.""UserId"" = t.""UserId"" AND n.""IsRunning"" AND (n.""StartTime"", n.""Id"") > (t.""StartTime"", t.""Id""));");

            migrationBuilder.DropIndex(
                name: "IX_Users_GitHubId",
                table: "Users");

            migrationBuilder.DropIndex(
                name: "IX_TimeEntries_OrganizationId",
                table: "TimeEntries");

            migrationBuilder.DropIndex(
                name: "IX_TimeEntries_UserId_IsRunning",
                table: "TimeEntries");

            migrationBuilder.DropIndex(
                name: "IX_Organizations_Slug",
                table: "Organizations");

            migrationBuilder.DropIndex(
                name: "IX_AbsenceTypeDefinitions_OrganizationId_Name",
                table: "AbsenceTypeDefinitions");

            migrationBuilder.DropIndex(
                name: "IX_AbsenceDays_OrganizationId",
                table: "AbsenceDays");

            migrationBuilder.DropColumn(
                name: "GitHubId",
                table: "Users");

            migrationBuilder.DropColumn(
                name: "GitHubUsername",
                table: "Users");

            migrationBuilder.DropColumn(
                name: "RefreshToken",
                table: "Users");

            migrationBuilder.DropColumn(
                name: "RefreshTokenExpiryTime",
                table: "Users");

            migrationBuilder.CreateIndex(
                name: "IX_TimeEntries_OrganizationId_StartTime",
                table: "TimeEntries",
                columns: new[] { "OrganizationId", "StartTime" });

            migrationBuilder.CreateIndex(
                name: "IX_TimeEntries_UserId",
                table: "TimeEntries",
                column: "UserId",
                unique: true,
                filter: "\"IsRunning\"");

            migrationBuilder.CreateIndex(
                name: "IX_TimeEntries_UserId_StartTime",
                table: "TimeEntries",
                columns: new[] { "UserId", "StartTime" });

            migrationBuilder.CreateIndex(
                name: "IX_Organizations_Slug",
                table: "Organizations",
                column: "Slug",
                unique: true,
                filter: "\"IsActive\"");

            migrationBuilder.CreateIndex(
                name: "IX_Notifications_UserId_CreatedAt",
                table: "Notifications",
                columns: new[] { "UserId", "CreatedAt" });

            migrationBuilder.CreateIndex(
                name: "IX_AbsenceTypeDefinitions_OrganizationId_Name",
                table: "AbsenceTypeDefinitions",
                columns: new[] { "OrganizationId", "Name" },
                unique: true,
                filter: "\"IsActive\"");

            migrationBuilder.CreateIndex(
                name: "IX_AbsenceDays_OrganizationId_Date",
                table: "AbsenceDays",
                columns: new[] { "OrganizationId", "Date" });

            // Case-insensitive email uniqueness; also lets lower("Email") lookups use an index
            migrationBuilder.Sql(@"CREATE UNIQUE INDEX ""IX_Users_Email_Lower"" ON ""Users"" (lower(""Email""));");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql(@"DROP INDEX IF EXISTS ""IX_Users_Email_Lower"";");

            migrationBuilder.DropIndex(
                name: "IX_TimeEntries_OrganizationId_StartTime",
                table: "TimeEntries");

            migrationBuilder.DropIndex(
                name: "IX_TimeEntries_UserId",
                table: "TimeEntries");

            migrationBuilder.DropIndex(
                name: "IX_TimeEntries_UserId_StartTime",
                table: "TimeEntries");

            migrationBuilder.DropIndex(
                name: "IX_Organizations_Slug",
                table: "Organizations");

            migrationBuilder.DropIndex(
                name: "IX_Notifications_UserId_CreatedAt",
                table: "Notifications");

            migrationBuilder.DropIndex(
                name: "IX_AbsenceTypeDefinitions_OrganizationId_Name",
                table: "AbsenceTypeDefinitions");

            migrationBuilder.DropIndex(
                name: "IX_AbsenceDays_OrganizationId_Date",
                table: "AbsenceDays");

            migrationBuilder.AddColumn<string>(
                name: "GitHubId",
                table: "Users",
                type: "text",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "GitHubUsername",
                table: "Users",
                type: "text",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "RefreshToken",
                table: "Users",
                type: "text",
                nullable: true);

            migrationBuilder.AddColumn<DateTime>(
                name: "RefreshTokenExpiryTime",
                table: "Users",
                type: "timestamp with time zone",
                nullable: true);

            migrationBuilder.CreateIndex(
                name: "IX_Users_GitHubId",
                table: "Users",
                column: "GitHubId",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_TimeEntries_OrganizationId",
                table: "TimeEntries",
                column: "OrganizationId");

            migrationBuilder.CreateIndex(
                name: "IX_TimeEntries_UserId_IsRunning",
                table: "TimeEntries",
                columns: new[] { "UserId", "IsRunning" });

            migrationBuilder.CreateIndex(
                name: "IX_Organizations_Slug",
                table: "Organizations",
                column: "Slug",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_AbsenceTypeDefinitions_OrganizationId_Name",
                table: "AbsenceTypeDefinitions",
                columns: new[] { "OrganizationId", "Name" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_AbsenceDays_OrganizationId",
                table: "AbsenceDays",
                column: "OrganizationId");
        }
    }
}
