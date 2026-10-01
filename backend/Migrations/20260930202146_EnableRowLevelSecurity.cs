using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace TimeTracking.Api.Migrations
{
    /// <summary>
    /// Postgres row-level security for tracked time and absences: a row is visible (and
    /// writable) only to its owner and to active members of the row's organization, so a
    /// bug in a service can never leak another organization's data. The acting user comes
    /// from the session settings RlsConnectionInterceptor stamps on every connection;
    /// trusted system work (migrations, seeding, background jobs) sets app.bypass_rls.
    /// FORCE makes the policies apply to the table owner too. Superusers always bypass RLS,
    /// so the app must connect as a normal role (see docker-compose.production.yml).
    /// </summary>
    public partial class EnableRowLevelSecurity : Migration
    {
        private const string CurrentUser = "NULLIF(current_setting('app.user_id', true), '')::int";

        internal static string Policy(string table) => $@"
            ALTER TABLE ""{table}"" ENABLE ROW LEVEL SECURITY;
            ALTER TABLE ""{table}"" FORCE ROW LEVEL SECURITY;
            CREATE POLICY org_isolation ON ""{table}""
                USING (
                    current_setting('app.bypass_rls', true) = 'on'
                    OR ""UserId"" = {CurrentUser}
                    OR ""OrganizationId"" IN (
                        SELECT uo.""OrganizationId"" FROM ""UserOrganizations"" uo
                        WHERE uo.""UserId"" = {CurrentUser} AND uo.""IsActive""))
                WITH CHECK (
                    current_setting('app.bypass_rls', true) = 'on'
                    OR ""UserId"" = {CurrentUser}
                    OR ""OrganizationId"" IN (
                        SELECT uo.""OrganizationId"" FROM ""UserOrganizations"" uo
                        WHERE uo.""UserId"" = {CurrentUser} AND uo.""IsActive""));";

        internal static string DropPolicy(string table) => $@"
            DROP POLICY IF EXISTS org_isolation ON ""{table}"";
            ALTER TABLE ""{table}"" NO FORCE ROW LEVEL SECURITY;
            ALTER TABLE ""{table}"" DISABLE ROW LEVEL SECURITY;";

        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql(Policy("TimeEntries"));
            migrationBuilder.Sql(Policy("AbsenceDays"));
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql(DropPolicy("TimeEntries"));
            migrationBuilder.Sql(DropPolicy("AbsenceDays"));
        }
    }
}
