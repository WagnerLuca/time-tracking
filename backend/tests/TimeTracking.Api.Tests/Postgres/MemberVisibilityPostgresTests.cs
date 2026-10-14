using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;
using Npgsql;
using TimeTracking.Api.Data;
using TimeTracking.Api.Models;
using TimeTracking.Api.Tests.Infrastructure;

namespace TimeTracking.Api.Tests.Postgres;

/// <summary>
/// The member visibility matrix on real Postgres: the migration from the old vacation / sick day /
/// time entry visibility settings keeps today's behaviour, and reading a colleague's data the
/// matrix allows still works under row-level security.
/// </summary>
[Collection(PostgresCollection.Name)]
public class MemberVisibilityPostgresTests
{
    private readonly PostgresApiFactory _factory;

    public MemberVisibilityPostgresTests(PostgresApiFactory factory) => _factory = factory;

    private const MemberInfo Base = MemberInfo.Member | MemberInfo.Email | MemberInfo.Groups | MemberInfo.AbsenceNotes;
    private const MemberInfo VacationKinds = MemberInfo.Vacation | MemberInfo.VacationBalance;

    [Fact]
    public async Task Migration_MapsOldVisibilitySettings_IntoTheMatrix()
    {
        const string database = "tt_visibility_migration";
        await using (var admin = new NpgsqlConnection(_factory.SuperuserConnectionString))
        {
            await admin.OpenAsync();
            await using var create = new NpgsqlCommand($"CREATE DATABASE {database}", admin);
            await create.ExecuteNonQueryAsync();
        }
        var connection = new NpgsqlConnectionStringBuilder(_factory.SuperuserConnectionString) { Database = database }.ConnectionString;
        await using var db = new TimeTrackingDbContext(new DbContextOptionsBuilder<TimeTrackingDbContext>().UseNpgsql(connection).Options);
        var migrator = db.GetService<IMigrator>();

        await migrator.MigrateAsync("20261014183857_CoreWorkingHours");
        // Vacation / sick day visibility: 0 Private, 1 AdminOnly, 2 AllMembers (groups: null = inherit)
        await db.Database.ExecuteSqlRawAsync("""
            INSERT INTO "Organizations" ("Id", "Name", "Slug", "AbsenceSelfServiceMode", "AutoPauseEnabled", "BusinessDayEnd", "CsvImportMode",
                "DefaultVacationDays", "EditPastEntriesMode", "EditPauseMode", "InitialOvertimeMode", "JoinPolicy", "MemberTimeEntryVisibility",
                "Require2fa", "SickDayVisibility", "UpdatedAt", "VacationVisibility", "WorkScheduleChangeMode")
            VALUES (1, 'Vacation open', 'o1', 0, true, '00:00', 0, 30, 0, 0, 0, 0, false, false, 0, now(), 2, 0),
                   (2, 'All open', 'o2', 0, true, '00:00', 0, 30, 0, 0, 0, 0, false, false, 2, now(), 2, 0),
                   (3, 'Closed', 'o3', 0, true, '00:00', 0, 30, 0, 0, 0, 0, true, false, 0, now(), 1, 0);
            INSERT INTO "Groups" ("Id", "OrganizationId", "Name", "VacationVisibility", "SickDayVisibility", "TimeEntryVisibility")
            VALUES (1, 3, 'Opens vacation + times', 2, NULL, 2),
                   (2, 2, 'Hides vacation', 1, NULL, NULL),
                   (3, 2, 'Inherits', NULL, NULL, 0),
                   (4, 1, 'Opens sick days', NULL, 2, NULL);
            """);

        await migrator.MigrateAsync();

        var orgs = await db.Organizations.IgnoreQueryFilters().AsNoTracking().OrderBy(o => o.Id).ToListAsync();
        orgs.Select(o => (o.VisibleInGroups, o.VisibleOutsideGroups)).Should().Equal(
            (Base | VacationKinds, Base | VacationKinds),
            (Base | VacationKinds | MemberInfo.SickDays | MemberInfo.OtherAbsences, Base | VacationKinds | MemberInfo.SickDays | MemberInfo.OtherAbsences),
            (Base, Base));

        var groups = await db.Groups.IgnoreQueryFilters().AsNoTracking().OrderBy(g => g.Id).ToListAsync();
        groups.Select(g => (g.VisibilityAllowInGroups, g.VisibilityAllowOutsideGroups, g.VisibilityDenyInGroups, g.VisibilityDenyOutsideGroups)).Should().Equal(
            // vacation opened; "other" would need sick days too (org: private) → denied; time entries → in own groups
            (VacationKinds | MemberInfo.TrackedTime, VacationKinds, MemberInfo.OtherAbsences, MemberInfo.OtherAbsences),
            // AdminOnly hides from members; "other" loses the vacation half
            (MemberInfo.None, MemberInfo.None, VacationKinds | MemberInfo.OtherAbsences, VacationKinds | MemberInfo.OtherAbsences),
            (MemberInfo.None, MemberInfo.None, MemberInfo.None, MemberInfo.None),
            // sick days opened, with the org's open vacation "other" is open as well
            (MemberInfo.SickDays | MemberInfo.OtherAbsences, MemberInfo.SickDays | MemberInfo.OtherAbsences, MemberInfo.None, MemberInfo.None));
    }

    [Fact]
    public async Task ColleagueData_TheMatrixAllows_IsReadableUnderRls()
    {
        var (owner, _) = await TestHelpers.CreateAuthenticatedUserAsync(_factory, "pg-vis-owner@pg.test");
        var org = await TestHelpers.CreateOrganizationAsync(owner, "Visibility Org", "pg-vis-org");
        var (viewer, v) = await TestHelpers.CreateAuthenticatedUserAsync(_factory, "pg-vis-v@pg.test");
        var (colleague, c) = await TestHelpers.CreateAuthenticatedUserAsync(_factory, "pg-vis-c@pg.test");
        var (_, x) = await TestHelpers.CreateAuthenticatedUserAsync(_factory, "pg-vis-x@pg.test");
        foreach (var login in new[] { v, c, x })
            (await TestHelpers.AddMemberAsync(_factory, owner, org.Slug, login)).EnsureSuccessStatusCode();
        await TestHelpers.CreateGroupAsync(owner, org.Slug, "Team", new { }, (v.User.Id, "Member"), (c.User.Id, "Member"));
        (await owner.PutAsJsonAsync($"/api/v1/Organizations/{org.Slug}/settings", new
        {
            visibility = TestHelpers.Matrix(("Member", true, false), ("TrackedTime", true, false), ("Vacation", true, false))
        })).EnsureSuccessStatusCode();

        (await colleague.PostAsJsonAsync("/api/v1/TimeTracking/manual", new
        {
            organizationId = org.Id, startTime = new DateTime(2026, 3, 2, 7, 0, 0, DateTimeKind.Utc), endTime = new DateTime(2026, 3, 2, 12, 0, 0, DateTimeKind.Utc)
        })).EnsureSuccessStatusCode();
        (await owner.PostAsJsonAsync($"/api/v1/organizations/{org.Slug}/absences/admin", new { userId = c.User.Id, date = "2026-03-03", type = 1 })).EnsureSuccessStatusCode();

        var entries = await viewer.GetFromJsonAsync<JsonElement>(
            $"/api/v1/Organizations/{org.Slug}/member-entries/{c.User.Id}?from=2026-03-01T00:00:00Z&to=2026-03-31T00:00:00Z");
        entries.GetProperty("items").GetArrayLength().Should().Be(1);
        var absences = await viewer.GetFromJsonAsync<JsonElement>($"/api/v1/organizations/{org.Slug}/absences?userId={c.User.Id}");
        absences.GetProperty("items").GetArrayLength().Should().Be(1);
        (await viewer.GetAsync($"/api/v1/Organizations/{org.Slug}/member-entries/{x.User.Id}")).StatusCode.Should().Be(HttpStatusCode.NotFound);
    }
}
