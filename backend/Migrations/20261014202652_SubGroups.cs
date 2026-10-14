using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace TimeTracking.Api.Migrations
{
    /// <inheritdoc />
    public partial class SubGroups : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<int>(
                name: "ParentGroupId",
                table: "Groups",
                type: "integer",
                nullable: true);

            migrationBuilder.CreateIndex(
                name: "IX_Groups_ParentGroupId",
                table: "Groups",
                column: "ParentGroupId");

            migrationBuilder.AddForeignKey(
                name: "FK_Groups_Groups_ParentGroupId",
                table: "Groups",
                column: "ParentGroupId",
                principalTable: "Groups",
                principalColumn: "Id",
                onDelete: ReferentialAction.SetNull);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "FK_Groups_Groups_ParentGroupId",
                table: "Groups");

            migrationBuilder.DropIndex(
                name: "IX_Groups_ParentGroupId",
                table: "Groups");

            migrationBuilder.DropColumn(
                name: "ParentGroupId",
                table: "Groups");
        }
    }
}
