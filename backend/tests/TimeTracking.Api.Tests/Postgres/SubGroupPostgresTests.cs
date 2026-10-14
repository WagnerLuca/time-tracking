using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using FluentAssertions;
using TimeTracking.Api.Tests.Infrastructure;

namespace TimeTracking.Api.Tests.Postgres;

/// <summary>
/// Sub-groups on real Postgres: the ParentGroupId migration and foreign key, and a department admin
/// reading and correcting a team member's entries under row-level security.
/// </summary>
[Collection(PostgresCollection.Name)]
public class SubGroupPostgresTests
{
    private readonly PostgresApiFactory _factory;

    public SubGroupPostgresTests(PostgresApiFactory factory) => _factory = factory;

    [Fact]
    public async Task DepartmentAdmin_ReadsAndCorrectsTeamMemberEntries_UnderRls()
    {
        var (owner, _) = await TestHelpers.CreateAuthenticatedUserAsync(_factory, "pg-sub-owner@pg.test");
        var org = await TestHelpers.CreateOrganizationAsync(owner, "Sub Org", "pg-sub-org");
        var (lead, l) = await TestHelpers.CreateAuthenticatedUserAsync(_factory, "pg-sub-l@pg.test");
        var (member, m) = await TestHelpers.CreateAuthenticatedUserAsync(_factory, "pg-sub-m@pg.test");
        var (sibling, s) = await TestHelpers.CreateAuthenticatedUserAsync(_factory, "pg-sub-s@pg.test");
        foreach (var login in new[] { l, m, s })
            (await TestHelpers.AddMemberAsync(_factory, owner, org.Slug, login)).EnsureSuccessStatusCode();
        var dept = await TestHelpers.CreateGroupAsync(owner, org.Slug, "Dept",
            new { groupAdminCanViewAllTimeEntries = true, groupAdminCanApproveTimeEdits = true }, (l.User.Id, "Admin"));
        var team = (await (await owner.PostAsJsonAsync($"/api/v1/Organizations/{org.Slug}/groups", new { name = "Team", parentGroupId = dept }))
            .Content.ReadFromJsonAsync<JsonElement>()).GetProperty("id").GetInt32();
        (await owner.PostAsJsonAsync($"/api/v1/Organizations/{org.Slug}/groups/{team}/members", new { userId = m.User.Id })).EnsureSuccessStatusCode();
        await TestHelpers.CreateGroupAsync(owner, org.Slug, "Other", new { groupAdminCanViewAllTimeEntries = true }, (s.User.Id, "Admin"));

        var created = await (await member.PostAsJsonAsync("/api/v1/TimeTracking/manual", new
        {
            organizationId = org.Id, startTime = new DateTime(2026, 3, 2, 7, 0, 0, DateTimeKind.Utc), endTime = new DateTime(2026, 3, 2, 12, 0, 0, DateTimeKind.Utc)
        })).Content.ReadFromJsonAsync<JsonElement>();
        var entryId = created.GetProperty("entry").GetProperty("id").GetInt32();

        var url = $"/api/v1/Organizations/{org.Slug}/member-entries/{m.User.Id}?from=2026-03-01T00:00:00Z&to=2026-03-31T00:00:00Z";
        (await lead.GetFromJsonAsync<JsonElement>(url)).GetProperty("items").GetArrayLength().Should().Be(1);
        (await lead.PutAsJsonAsync($"/api/v1/organizations/{org.Slug}/members/{m.User.Id}/entries/{entryId}", new { description = "by the department" }))
            .EnsureSuccessStatusCode();
        (await sibling.GetAsync(url)).StatusCode.Should().NotBe(HttpStatusCode.OK);   // another branch's admin

        // Moving the team out of the department ends the lead's reach (the FK accepts a top-level group)
        (await owner.PutAsJsonAsync($"/api/v1/Organizations/{org.Slug}/groups/{team}", new { name = "Team" })).EnsureSuccessStatusCode();
        (await lead.GetAsync(url)).StatusCode.Should().NotBe(HttpStatusCode.OK);
    }
}
