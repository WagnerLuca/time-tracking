using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace TimeTracking.Api.Migrations
{
    /// <inheritdoc />
    public partial class PauseIsManual : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<bool>(
                name: "PauseIsManual",
                table: "TimeEntries",
                type: "boolean",
                nullable: false,
                defaultValue: false);

            // Existing pauses cannot be told apart; treat them as manual so none is recomputed away
            migrationBuilder.Sql(@"UPDATE ""TimeEntries"" SET ""PauseIsManual"" = TRUE WHERE ""PauseDurationMinutes"" > 0;");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "PauseIsManual",
                table: "TimeEntries");
        }
    }
}
