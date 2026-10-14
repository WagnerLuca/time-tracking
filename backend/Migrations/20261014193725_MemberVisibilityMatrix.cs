using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace TimeTracking.Api.Migrations
{
    /// <inheritdoc />
    public partial class MemberVisibilityMatrix : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            // Issue #60: the member visibility matrix replaces VacationVisibility / SickDayVisibility
            // (org + group) and the group's TimeEntryVisibility. MemberInfo bits: Member 1, Email 2,
            // Groups 4, Presence 8, Vacation 16, SickDays 32, OtherAbsences 64, AbsenceNotes 128,
            // VacationBalance 256, WorkSchedule 512, TrackedTime 1024. Mapping keeps today's behaviour:
            // member list, email, groups (and notes on visible absences) stay visible in and outside
            // groups; AllMembers vacation / sick days become visible in both columns (vacation with the
            // balance, "other" absences needed both); a group's AllMembers time entries become
            // "tracked times in own groups"; everything else stays hidden from members.
            migrationBuilder.AddColumn<int>(name: "VisibleInGroups", table: "Organizations", type: "integer", nullable: false, defaultValue: 135);
            migrationBuilder.AddColumn<int>(name: "VisibleOutsideGroups", table: "Organizations", type: "integer", nullable: false, defaultValue: 135);
            migrationBuilder.AddColumn<int>(name: "VisibilityAllowInGroups", table: "Groups", type: "integer", nullable: false, defaultValue: 0);
            migrationBuilder.AddColumn<int>(name: "VisibilityAllowOutsideGroups", table: "Groups", type: "integer", nullable: false, defaultValue: 0);
            migrationBuilder.AddColumn<int>(name: "VisibilityDenyInGroups", table: "Groups", type: "integer", nullable: false, defaultValue: 0);
            migrationBuilder.AddColumn<int>(name: "VisibilityDenyOutsideGroups", table: "Groups", type: "integer", nullable: false, defaultValue: 0);

            migrationBuilder.Sql("""
                UPDATE "Organizations" SET "VisibleInGroups" = m, "VisibleOutsideGroups" = m
                FROM (SELECT "Id" AS id, 135
                        | CASE WHEN "VacationVisibility" = 2 THEN 272 ELSE 0 END
                        | CASE WHEN "SickDayVisibility" = 2 THEN 32 ELSE 0 END
                        | CASE WHEN "VacationVisibility" = 2 AND "SickDayVisibility" = 2 THEN 64 ELSE 0 END AS m
                      FROM "Organizations") v
                WHERE "Organizations"."Id" = v.id;

                UPDATE "Groups" g SET
                    "VisibilityAllowInGroups" = x.allow | CASE WHEN g."TimeEntryVisibility" = 2 THEN 1024 ELSE 0 END,
                    "VisibilityAllowOutsideGroups" = x.allow,
                    "VisibilityDenyInGroups" = x.deny,
                    "VisibilityDenyOutsideGroups" = x.deny
                FROM (SELECT g2."Id" AS id,
                          CASE WHEN g2."VacationVisibility" = 2 THEN 272 ELSE 0 END
                          | CASE WHEN g2."SickDayVisibility" = 2 THEN 32 ELSE 0 END
                          | CASE WHEN (g2."VacationVisibility" IS NOT NULL OR g2."SickDayVisibility" IS NOT NULL)
                                  AND COALESCE(g2."VacationVisibility", o."VacationVisibility") = 2
                                  AND COALESCE(g2."SickDayVisibility", o."SickDayVisibility") = 2 THEN 64 ELSE 0 END AS allow,
                          CASE WHEN g2."VacationVisibility" < 2 THEN 272 ELSE 0 END
                          | CASE WHEN g2."SickDayVisibility" < 2 THEN 32 ELSE 0 END
                          | CASE WHEN (g2."VacationVisibility" IS NOT NULL OR g2."SickDayVisibility" IS NOT NULL)
                                  AND NOT (COALESCE(g2."VacationVisibility", o."VacationVisibility") = 2
                                       AND COALESCE(g2."SickDayVisibility", o."SickDayVisibility") = 2) THEN 64 ELSE 0 END AS deny
                      FROM "Groups" g2 JOIN "Organizations" o ON o."Id" = g2."OrganizationId") x
                WHERE g."Id" = x.id;
                """);

            migrationBuilder.DropColumn(name: "SickDayVisibility", table: "Groups");
            migrationBuilder.DropColumn(name: "TimeEntryVisibility", table: "Groups");
            migrationBuilder.DropColumn(name: "VacationVisibility", table: "Groups");
            migrationBuilder.DropColumn(name: "SickDayVisibility", table: "Organizations");
            migrationBuilder.DropColumn(name: "VacationVisibility", table: "Organizations");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            // Best effort: a kind visible in both columns becomes AllMembers, anything else Private
            migrationBuilder.AddColumn<int>(name: "VacationVisibility", table: "Organizations", type: "integer", nullable: false, defaultValue: 0);
            migrationBuilder.AddColumn<int>(name: "SickDayVisibility", table: "Organizations", type: "integer", nullable: false, defaultValue: 0);
            migrationBuilder.AddColumn<int>(name: "SickDayVisibility", table: "Groups", type: "integer", nullable: true);
            migrationBuilder.AddColumn<int>(name: "TimeEntryVisibility", table: "Groups", type: "integer", nullable: true);
            migrationBuilder.AddColumn<int>(name: "VacationVisibility", table: "Groups", type: "integer", nullable: true);

            migrationBuilder.Sql("""
                UPDATE "Organizations" SET
                    "VacationVisibility" = CASE WHEN "VisibleInGroups" & "VisibleOutsideGroups" & 16 <> 0 THEN 2 ELSE 0 END,
                    "SickDayVisibility" = CASE WHEN "VisibleInGroups" & "VisibleOutsideGroups" & 32 <> 0 THEN 2 ELSE 0 END;
                UPDATE "Groups" SET
                    "VacationVisibility" = CASE WHEN "VisibilityAllowInGroups" & 16 <> 0 THEN 2 WHEN "VisibilityDenyInGroups" & 16 <> 0 THEN 0 END,
                    "SickDayVisibility" = CASE WHEN "VisibilityAllowInGroups" & 32 <> 0 THEN 2 WHEN "VisibilityDenyInGroups" & 32 <> 0 THEN 0 END,
                    "TimeEntryVisibility" = CASE WHEN "VisibilityAllowInGroups" & 1024 <> 0 THEN 2 END;
                """);

            migrationBuilder.DropColumn(name: "VisibilityAllowInGroups", table: "Groups");
            migrationBuilder.DropColumn(name: "VisibilityAllowOutsideGroups", table: "Groups");
            migrationBuilder.DropColumn(name: "VisibilityDenyInGroups", table: "Groups");
            migrationBuilder.DropColumn(name: "VisibilityDenyOutsideGroups", table: "Groups");
            migrationBuilder.DropColumn(name: "VisibleInGroups", table: "Organizations");
            migrationBuilder.DropColumn(name: "VisibleOutsideGroups", table: "Organizations");
        }
    }
}
