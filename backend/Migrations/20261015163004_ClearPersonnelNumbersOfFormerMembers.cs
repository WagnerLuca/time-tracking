using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace TimeTracking.Api.Migrations
{
    /// <inheritdoc />
    public partial class ClearPersonnelNumbersOfFormerMembers : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            // Removing a member now clears the number (it would block reuse via the unique index)
            migrationBuilder.Sql("UPDATE \"UserOrganizations\" SET \"PersonnelNumber\" = NULL WHERE NOT \"IsActive\";");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {

        }
    }
}
