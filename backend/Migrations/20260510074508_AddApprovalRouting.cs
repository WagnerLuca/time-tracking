using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace TimeTracking.Api.Migrations
{
    /// <inheritdoc />
    public partial class AddApprovalRouting : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<int>(
                name: "GroupId",
                table: "OrgRequests",
                type: "integer",
                nullable: true);

            migrationBuilder.CreateIndex(
                name: "IX_OrgRequests_GroupId",
                table: "OrgRequests",
                column: "GroupId");

            migrationBuilder.AddForeignKey(
                name: "FK_OrgRequests_Groups_GroupId",
                table: "OrgRequests",
                column: "GroupId",
                principalTable: "Groups",
                principalColumn: "Id",
                onDelete: ReferentialAction.SetNull);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "FK_OrgRequests_Groups_GroupId",
                table: "OrgRequests");

            migrationBuilder.DropIndex(
                name: "IX_OrgRequests_GroupId",
                table: "OrgRequests");

            migrationBuilder.DropColumn(
                name: "GroupId",
                table: "OrgRequests");
        }
    }
}
