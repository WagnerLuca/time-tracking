using System;
using Microsoft.EntityFrameworkCore.Migrations;
using Npgsql.EntityFrameworkCore.PostgreSQL.Metadata;

#nullable disable

namespace TimeTracking.Api.Migrations
{
    /// <summary>
    /// Frozen figures of closed months (issue #81): <c>FrozenMonths</c>/<c>FrozenDays</c> with RLS like TimeEntries, and
    /// <c>ClosedPeriods.FrozenAt</c>. Months closed before this migration keep FrozenAt null and are frozen once at the next
    /// startup (<c>FrozenFigures.FreezeUnfrozenAsync</c>) — from the data as it is then, which is today's state of those months.
    /// </summary>
    public partial class FreezeClosedMonths : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<DateTime>(
                name: "FrozenAt",
                table: "ClosedPeriods",
                type: "timestamp with time zone",
                nullable: true);

            migrationBuilder.CreateTable(
                name: "FrozenDays",
                columns: table => new
                {
                    Id = table.Column<long>(type: "bigint", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    OrganizationId = table.Column<int>(type: "integer", nullable: false),
                    UserId = table.Column<int>(type: "integer", nullable: false),
                    Date = table.Column<DateOnly>(type: "date", nullable: false),
                    DayTargetHours = table.Column<double>(type: "double precision", nullable: false),
                    CreditHours = table.Column<double>(type: "double precision", nullable: false),
                    TargetHours = table.Column<double>(type: "double precision", nullable: false),
                    WorkedHours = table.Column<double>(type: "double precision", nullable: false),
                    PauseMinutes = table.Column<int>(type: "integer", nullable: false),
                    NetWorkedHours = table.Column<double>(type: "double precision", nullable: false),
                    Overtime = table.Column<double>(type: "double precision", nullable: false),
                    OvertimeDelta = table.Column<double>(type: "double precision", nullable: true),
                    CumulativeOvertime = table.Column<double>(type: "double precision", nullable: false),
                    Status = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: false),
                    HolidayName = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: true),
                    AbsenceType = table.Column<int>(type: "integer", nullable: true),
                    AbsenceHalfDay = table.Column<bool>(type: "boolean", nullable: false),
                    AbsenceNote = table.Column<string>(type: "text", nullable: true),
                    CountsAsVacation = table.Column<bool>(type: "boolean", nullable: false),
                    VacationUsed = table.Column<double>(type: "double precision", nullable: false),
                    CoreHoursMissed = table.Column<bool>(type: "boolean", nullable: false),
                    FailureCount = table.Column<int>(type: "integer", nullable: false),
                    EntrySummary = table.Column<string>(type: "text", nullable: false),
                    Compliance = table.Column<string>(type: "text", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_FrozenDays", x => x.Id);
                    table.ForeignKey(
                        name: "FK_FrozenDays_Organizations_OrganizationId",
                        column: x => x.OrganizationId,
                        principalTable: "Organizations",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "FK_FrozenDays_Users_UserId",
                        column: x => x.UserId,
                        principalTable: "Users",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "FrozenMonths",
                columns: table => new
                {
                    Id = table.Column<int>(type: "integer", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    OrganizationId = table.Column<int>(type: "integer", nullable: false),
                    UserId = table.Column<int>(type: "integer", nullable: false),
                    Month = table.Column<DateOnly>(type: "date", nullable: false),
                    FirstDay = table.Column<DateOnly>(type: "date", nullable: true),
                    OvertimeDelta = table.Column<double>(type: "double precision", nullable: false),
                    InitialOvertimeHours = table.Column<double>(type: "double precision", nullable: false),
                    TargetHours = table.Column<double>(type: "double precision", nullable: false),
                    WorkedHours = table.Column<double>(type: "double precision", nullable: false),
                    OvertimeHours = table.Column<double>(type: "double precision", nullable: false),
                    Balance = table.Column<double>(type: "double precision", nullable: false),
                    VacationDays = table.Column<double>(type: "double precision", nullable: false),
                    SickDays = table.Column<double>(type: "double precision", nullable: false),
                    OtherAbsenceDays = table.Column<double>(type: "double precision", nullable: false),
                    FailureEntryCount = table.Column<int>(type: "integer", nullable: false),
                    TimeZone = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: true),
                    FrozenAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_FrozenMonths", x => x.Id);
                    table.ForeignKey(
                        name: "FK_FrozenMonths_Organizations_OrganizationId",
                        column: x => x.OrganizationId,
                        principalTable: "Organizations",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "FK_FrozenMonths_Users_UserId",
                        column: x => x.UserId,
                        principalTable: "Users",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "IX_FrozenDays_OrganizationId_UserId_Date",
                table: "FrozenDays",
                columns: new[] { "OrganizationId", "UserId", "Date" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_FrozenDays_UserId",
                table: "FrozenDays",
                column: "UserId");

            migrationBuilder.CreateIndex(
                name: "IX_FrozenMonths_OrganizationId_UserId_Month",
                table: "FrozenMonths",
                columns: new[] { "OrganizationId", "UserId", "Month" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_FrozenMonths_UserId",
                table: "FrozenMonths",
                column: "UserId");

            // Same row-level security as TimeEntries: the owner and active members of the organization
            migrationBuilder.Sql(EnableRowLevelSecurity.Policy("FrozenMonths"));
            migrationBuilder.Sql(EnableRowLevelSecurity.Policy("FrozenDays"));
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql(EnableRowLevelSecurity.DropPolicy("FrozenDays"));
            migrationBuilder.Sql(EnableRowLevelSecurity.DropPolicy("FrozenMonths"));

            migrationBuilder.DropTable(
                name: "FrozenDays");

            migrationBuilder.DropTable(
                name: "FrozenMonths");

            migrationBuilder.DropColumn(
                name: "FrozenAt",
                table: "ClosedPeriods");
        }
    }
}
