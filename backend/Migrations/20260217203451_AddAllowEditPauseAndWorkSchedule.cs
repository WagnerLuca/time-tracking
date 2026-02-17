using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace TimeTracking.Api.Migrations
{
    /// <inheritdoc />
    public partial class AddAllowEditPauseAndWorkSchedule : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<double>(
                name: "TargetFri",
                table: "UserOrganizations",
                type: "double precision",
                nullable: false,
                defaultValue: 0.0);

            migrationBuilder.AddColumn<double>(
                name: "TargetMon",
                table: "UserOrganizations",
                type: "double precision",
                nullable: false,
                defaultValue: 0.0);

            migrationBuilder.AddColumn<double>(
                name: "TargetThu",
                table: "UserOrganizations",
                type: "double precision",
                nullable: false,
                defaultValue: 0.0);

            migrationBuilder.AddColumn<double>(
                name: "TargetTue",
                table: "UserOrganizations",
                type: "double precision",
                nullable: false,
                defaultValue: 0.0);

            migrationBuilder.AddColumn<double>(
                name: "TargetWed",
                table: "UserOrganizations",
                type: "double precision",
                nullable: false,
                defaultValue: 0.0);

            migrationBuilder.AddColumn<double>(
                name: "WeeklyWorkHours",
                table: "UserOrganizations",
                type: "double precision",
                nullable: true);

            migrationBuilder.AddColumn<bool>(
                name: "AllowEditPause",
                table: "Organizations",
                type: "boolean",
                nullable: false,
                defaultValue: false);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "TargetFri",
                table: "UserOrganizations");

            migrationBuilder.DropColumn(
                name: "TargetMon",
                table: "UserOrganizations");

            migrationBuilder.DropColumn(
                name: "TargetThu",
                table: "UserOrganizations");

            migrationBuilder.DropColumn(
                name: "TargetTue",
                table: "UserOrganizations");

            migrationBuilder.DropColumn(
                name: "TargetWed",
                table: "UserOrganizations");

            migrationBuilder.DropColumn(
                name: "WeeklyWorkHours",
                table: "UserOrganizations");

            migrationBuilder.DropColumn(
                name: "AllowEditPause",
                table: "Organizations");
        }
    }
}
