using System;
using Microsoft.EntityFrameworkCore.Migrations;
using Npgsql.EntityFrameworkCore.PostgreSQL.Metadata;

#nullable disable

namespace TimeTracking.Api.Migrations
{
    /// <inheritdoc />
    public partial class AuthHardening : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            // Refresh tokens are now stored as SHA-256 (lower-case hex, see TokenService.HashToken).
            // Hash the existing plaintext values in place so current sessions keep working.
            migrationBuilder.Sql(@"
                UPDATE ""RefreshTokens"" SET
                    ""Token"" = encode(sha256(convert_to(""Token"", 'UTF8')), 'hex'),
                    ""ReplacedByToken"" = CASE WHEN ""ReplacedByToken"" IS NULL THEN NULL
                                              ELSE encode(sha256(convert_to(""ReplacedByToken"", 'UTF8')), 'hex') END;");

            migrationBuilder.AddColumn<long>(
                name: "TotpLastStep",
                table: "Users",
                type: "bigint",
                nullable: true);

            migrationBuilder.CreateTable(
                name: "UserTokens",
                columns: table => new
                {
                    Id = table.Column<int>(type: "integer", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    UserId = table.Column<int>(type: "integer", nullable: false),
                    Purpose = table.Column<int>(type: "integer", nullable: false),
                    TokenHash = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    NewEmail = table.Column<string>(type: "character varying(256)", maxLength: 256, nullable: true),
                    ExpiresAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    UsedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    CreatedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_UserTokens", x => x.Id);
                    table.ForeignKey(
                        name: "FK_UserTokens_Users_UserId",
                        column: x => x.UserId,
                        principalTable: "Users",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "IX_UserTokens_TokenHash",
                table: "UserTokens",
                column: "TokenHash",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_UserTokens_UserId",
                table: "UserTokens",
                column: "UserId");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "UserTokens");

            migrationBuilder.DropColumn(
                name: "TotpLastStep",
                table: "Users");
        }
    }
}
