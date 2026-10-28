using System;
using Microsoft.EntityFrameworkCore.Migrations;
using Npgsql.EntityFrameworkCore.PostgreSQL.Metadata;

#nullable disable

namespace TimeTracking.Api.Migrations
{
    /// <inheritdoc />
    public partial class DaysOff : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "IX_Holidays_OrganizationId_Date",
                table: "Holidays");

            migrationBuilder.DropIndex(
                name: "IX_AbsenceDays_UserId_OrganizationId_Date",
                table: "AbsenceDays");

            migrationBuilder.AddColumn<string>(
                name: "HolidayRegion",
                table: "WorkSchedules",
                type: "character varying(10)",
                maxLength: 10,
                nullable: true);

            migrationBuilder.AddColumn<double>(
                name: "VacationDaysPerYear",
                table: "WorkSchedules",
                type: "double precision",
                nullable: true);

            migrationBuilder.AddColumn<DateOnly>(
                name: "ExpiresOn",
                table: "VacationYears",
                type: "date",
                nullable: true);

            migrationBuilder.AddColumn<DateTime>(
                name: "ExpiryNoticeAt",
                table: "VacationYears",
                type: "timestamp with time zone",
                nullable: true);

            migrationBuilder.AddColumn<DateTime>(
                name: "ExpiryReminderAt",
                table: "VacationYears",
                type: "timestamp with time zone",
                nullable: true);

            migrationBuilder.AddColumn<DateOnly>(
                name: "EmploymentEnd",
                table: "UserOrganizations",
                type: "date",
                nullable: true);

            migrationBuilder.AddColumn<DateOnly>(
                name: "EmploymentStart",
                table: "UserOrganizations",
                type: "date",
                nullable: true);

            migrationBuilder.AddColumn<DateTime>(
                name: "LeftAt",
                table: "UserOrganizations",
                type: "timestamp with time zone",
                nullable: true);

            migrationBuilder.AddColumn<bool>(
                name: "GroupHolidayRegions",
                table: "Organizations",
                type: "boolean",
                nullable: false,
                defaultValue: false);

            migrationBuilder.AddColumn<string>(
                name: "HolidayRegion",
                table: "Organizations",
                type: "character varying(10)",
                maxLength: 10,
                nullable: true);

            migrationBuilder.AddColumn<DateOnly>(
                name: "VacationNoticesCheckedOn",
                table: "Organizations",
                type: "date",
                nullable: true);

            migrationBuilder.AddColumn<int>(
                name: "GroupId",
                table: "Holidays",
                type: "integer",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "HolidayRegion",
                table: "Groups",
                type: "character varying(10)",
                maxLength: 10,
                nullable: true);

            migrationBuilder.AddColumn<int>(
                name: "CompanyLeaveId",
                table: "AbsenceDays",
                type: "integer",
                nullable: true);

            migrationBuilder.AddColumn<bool>(
                name: "SecondHalf",
                table: "AbsenceDays",
                type: "boolean",
                nullable: false,
                defaultValue: false);

            migrationBuilder.CreateTable(
                name: "CompanyLeaves",
                columns: table => new
                {
                    Id = table.Column<int>(type: "integer", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    OrganizationId = table.Column<int>(type: "integer", nullable: false),
                    GroupId = table.Column<int>(type: "integer", nullable: true),
                    Name = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: false),
                    From = table.Column<DateOnly>(type: "date", nullable: false),
                    To = table.Column<DateOnly>(type: "date", nullable: false),
                    CreatedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    CreatedByUserId = table.Column<int>(type: "integer", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_CompanyLeaves", x => x.Id);
                    table.CheckConstraint("CK_CompanyLeaves_Range", "\"From\" <= \"To\"");
                    table.ForeignKey(
                        name: "FK_CompanyLeaves_Groups_GroupId",
                        column: x => x.GroupId,
                        principalTable: "Groups",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "FK_CompanyLeaves_Organizations_OrganizationId",
                        column: x => x.OrganizationId,
                        principalTable: "Organizations",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "GroupMembershipPeriods",
                columns: table => new
                {
                    Id = table.Column<int>(type: "integer", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    OrganizationId = table.Column<int>(type: "integer", nullable: false),
                    GroupId = table.Column<int>(type: "integer", nullable: false),
                    UserId = table.Column<int>(type: "integer", nullable: false),
                    From = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    Until = table.Column<DateTime>(type: "timestamp with time zone", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_GroupMembershipPeriods", x => x.Id);
                    table.ForeignKey(
                        name: "FK_GroupMembershipPeriods_Groups_GroupId",
                        column: x => x.GroupId,
                        principalTable: "Groups",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "FK_GroupMembershipPeriods_Organizations_OrganizationId",
                        column: x => x.OrganizationId,
                        principalTable: "Organizations",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "FK_GroupMembershipPeriods_Users_UserId",
                        column: x => x.UserId,
                        principalTable: "Users",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "DayOffExclusions",
                columns: table => new
                {
                    Id = table.Column<int>(type: "integer", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    OrganizationId = table.Column<int>(type: "integer", nullable: false),
                    UserId = table.Column<int>(type: "integer", nullable: false),
                    HolidayId = table.Column<int>(type: "integer", nullable: true),
                    CompanyLeaveId = table.Column<int>(type: "integer", nullable: true),
                    CreatedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_DayOffExclusions", x => x.Id);
                    table.CheckConstraint("CK_DayOffExclusions_Target", "(\"HolidayId\" IS NULL) <> (\"CompanyLeaveId\" IS NULL)");
                    table.ForeignKey(
                        name: "FK_DayOffExclusions_CompanyLeaves_CompanyLeaveId",
                        column: x => x.CompanyLeaveId,
                        principalTable: "CompanyLeaves",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "FK_DayOffExclusions_Holidays_HolidayId",
                        column: x => x.HolidayId,
                        principalTable: "Holidays",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "FK_DayOffExclusions_Organizations_OrganizationId",
                        column: x => x.OrganizationId,
                        principalTable: "Organizations",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "FK_DayOffExclusions_Users_UserId",
                        column: x => x.UserId,
                        principalTable: "Users",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "IX_Holidays_GroupId",
                table: "Holidays",
                column: "GroupId");

            migrationBuilder.CreateIndex(
                name: "IX_Holidays_OrganizationId_GroupId_Date",
                table: "Holidays",
                columns: new[] { "OrganizationId", "GroupId", "Date" },
                unique: true)
                .Annotation("Npgsql:NullsDistinct", false);

            migrationBuilder.CreateIndex(
                name: "IX_AbsenceDays_CompanyLeaveId",
                table: "AbsenceDays",
                column: "CompanyLeaveId");

            migrationBuilder.CreateIndex(
                name: "IX_AbsenceDays_UserId_OrganizationId_Date_SecondHalf",
                table: "AbsenceDays",
                columns: new[] { "UserId", "OrganizationId", "Date", "SecondHalf" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_CompanyLeaves_GroupId",
                table: "CompanyLeaves",
                column: "GroupId");

            migrationBuilder.CreateIndex(
                name: "IX_CompanyLeaves_OrganizationId_From",
                table: "CompanyLeaves",
                columns: new[] { "OrganizationId", "From" });

            migrationBuilder.CreateIndex(
                name: "IX_DayOffExclusions_CompanyLeaveId_UserId",
                table: "DayOffExclusions",
                columns: new[] { "CompanyLeaveId", "UserId" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_DayOffExclusions_HolidayId_UserId",
                table: "DayOffExclusions",
                columns: new[] { "HolidayId", "UserId" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_DayOffExclusions_OrganizationId",
                table: "DayOffExclusions",
                column: "OrganizationId");

            migrationBuilder.CreateIndex(
                name: "IX_DayOffExclusions_UserId",
                table: "DayOffExclusions",
                column: "UserId");

            migrationBuilder.CreateIndex(
                name: "IX_GroupMembershipPeriods_GroupId_UserId",
                table: "GroupMembershipPeriods",
                columns: new[] { "GroupId", "UserId" });

            migrationBuilder.CreateIndex(
                name: "IX_GroupMembershipPeriods_OrganizationId_UserId",
                table: "GroupMembershipPeriods",
                columns: new[] { "OrganizationId", "UserId" });

            migrationBuilder.CreateIndex(
                name: "IX_GroupMembershipPeriods_UserId",
                table: "GroupMembershipPeriods",
                column: "UserId");

            migrationBuilder.AddForeignKey(
                name: "FK_AbsenceDays_CompanyLeaves_CompanyLeaveId",
                table: "AbsenceDays",
                column: "CompanyLeaveId",
                principalTable: "CompanyLeaves",
                principalColumn: "Id",
                onDelete: ReferentialAction.SetNull);

            migrationBuilder.AddForeignKey(
                name: "FK_Holidays_Groups_GroupId",
                table: "Holidays",
                column: "GroupId",
                principalTable: "Groups",
                principalColumn: "Id",
                onDelete: ReferentialAction.Cascade);

            // Group membership history starts with today's memberships (earlier ones were not recorded)
            migrationBuilder.Sql("""
                INSERT INTO "GroupMembershipPeriods" ("OrganizationId", "GroupId", "UserId", "From", "Until")
                SELECT g."OrganizationId", gm."GroupId", gm."UserId", gm."JoinedAt", NULL
                FROM "GroupMembers" gm JOIN "Groups" g ON g."Id" = gm."GroupId"
                WHERE g."IsActive";
                """);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "FK_AbsenceDays_CompanyLeaves_CompanyLeaveId",
                table: "AbsenceDays");

            migrationBuilder.DropForeignKey(
                name: "FK_Holidays_Groups_GroupId",
                table: "Holidays");

            migrationBuilder.DropTable(
                name: "DayOffExclusions");

            migrationBuilder.DropTable(
                name: "GroupMembershipPeriods");

            migrationBuilder.DropTable(
                name: "CompanyLeaves");

            migrationBuilder.DropIndex(
                name: "IX_Holidays_GroupId",
                table: "Holidays");

            migrationBuilder.DropIndex(
                name: "IX_Holidays_OrganizationId_GroupId_Date",
                table: "Holidays");

            migrationBuilder.DropIndex(
                name: "IX_AbsenceDays_CompanyLeaveId",
                table: "AbsenceDays");

            migrationBuilder.DropIndex(
                name: "IX_AbsenceDays_UserId_OrganizationId_Date_SecondHalf",
                table: "AbsenceDays");

            migrationBuilder.DropColumn(
                name: "HolidayRegion",
                table: "WorkSchedules");

            migrationBuilder.DropColumn(
                name: "VacationDaysPerYear",
                table: "WorkSchedules");

            migrationBuilder.DropColumn(
                name: "ExpiresOn",
                table: "VacationYears");

            migrationBuilder.DropColumn(
                name: "ExpiryNoticeAt",
                table: "VacationYears");

            migrationBuilder.DropColumn(
                name: "ExpiryReminderAt",
                table: "VacationYears");

            migrationBuilder.DropColumn(
                name: "EmploymentEnd",
                table: "UserOrganizations");

            migrationBuilder.DropColumn(
                name: "EmploymentStart",
                table: "UserOrganizations");

            migrationBuilder.DropColumn(
                name: "LeftAt",
                table: "UserOrganizations");

            migrationBuilder.DropColumn(
                name: "GroupHolidayRegions",
                table: "Organizations");

            migrationBuilder.DropColumn(
                name: "HolidayRegion",
                table: "Organizations");

            migrationBuilder.DropColumn(
                name: "VacationNoticesCheckedOn",
                table: "Organizations");

            migrationBuilder.DropColumn(
                name: "GroupId",
                table: "Holidays");

            migrationBuilder.DropColumn(
                name: "HolidayRegion",
                table: "Groups");

            migrationBuilder.DropColumn(
                name: "CompanyLeaveId",
                table: "AbsenceDays");

            migrationBuilder.DropColumn(
                name: "SecondHalf",
                table: "AbsenceDays");

            migrationBuilder.CreateIndex(
                name: "IX_Holidays_OrganizationId_Date",
                table: "Holidays",
                columns: new[] { "OrganizationId", "Date" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_AbsenceDays_UserId_OrganizationId_Date",
                table: "AbsenceDays",
                columns: new[] { "UserId", "OrganizationId", "Date" },
                unique: true);
        }
    }
}
