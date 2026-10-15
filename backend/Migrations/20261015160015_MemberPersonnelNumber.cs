using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace TimeTracking.Api.Migrations
{
    /// <inheritdoc />
    public partial class MemberPersonnelNumber : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "IX_UserOrganizations_OrganizationId",
                table: "UserOrganizations");

            migrationBuilder.AddColumn<string>(
                name: "PersonnelNumber",
                table: "UserOrganizations",
                type: "character varying(50)",
                maxLength: 50,
                nullable: true);

            migrationBuilder.CreateIndex(
                name: "IX_UserOrganizations_OrganizationId_PersonnelNumber",
                table: "UserOrganizations",
                columns: new[] { "OrganizationId", "PersonnelNumber" },
                unique: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "IX_UserOrganizations_OrganizationId_PersonnelNumber",
                table: "UserOrganizations");

            migrationBuilder.DropColumn(
                name: "PersonnelNumber",
                table: "UserOrganizations");

            migrationBuilder.CreateIndex(
                name: "IX_UserOrganizations_OrganizationId",
                table: "UserOrganizations",
                column: "OrganizationId");
        }
    }
}
