using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using FluentAssertions;
using Microsoft.Extensions.DependencyInjection;
using TimeTracking.Api.Data;
using TimeTracking.Api.Models;
using TimeTracking.Api.Services;
using TimeTracking.Api.Tests.Infrastructure;

namespace TimeTracking.Api.Tests.Controllers;

/// <summary>
/// The dashboard's team overview (issue #61): who leads whom, balance = the balance endpoint, presence
/// (failure entries are not presence), failure count and today's core hours violation — each only where
/// the member visibility matrix shows it.
/// </summary>
public class TeamOverviewTests : IClassFixture<TimeTrackingApiFactory>
{
    private readonly TimeTrackingApiFactory _factory;

    public TeamOverviewTests(TimeTrackingApiFactory factory) => _factory = factory;

    /// <summary>Lead administers "Team" (Ana, Ben); Cleo is in no group. Ana runs a timer and missed today's core hours, Ben has a failure entry.</summary>
    private sealed record Setup(HttpClient Owner, string Slug, int OrgId, HttpClient Lead, int LeadId, HttpClient Ana, int AnaId,
        int BenId, HttpClient Cleo, int CleoId, int TeamId);

    private async Task<Setup> SetupAsync(string prefix, bool adminsSeeTimes = true, bool leadViewsEntries = true)
    {
        var (owner, _) = await TestHelpers.CreateAuthenticatedUserAsync(_factory, $"{prefix}-owner@team.test", "Olga", "Owner");
        var org = await TestHelpers.CreateOrganizationAsync(owner, $"{prefix} Org", $"{prefix}-org");
        (await owner.PutAsJsonAsync($"/api/v1/Organizations/{org.Slug}/settings", new { memberTimeEntryVisibility = adminsSeeTimes }))
            .EnsureSuccessStatusCode();
        async Task<(HttpClient, int)> Member(string name)
        {
            var (client, login) = await TestHelpers.CreateAuthenticatedUserAsync(_factory, $"{prefix}-{name.ToLowerInvariant()}@team.test", name, "Test");
            (await TestHelpers.AddMemberAsync(_factory, owner, org.Slug, login)).EnsureSuccessStatusCode();
            return (client, login.User.Id);
        }
        var (lead, leadId) = await Member("Lea");
        var (ana, anaId) = await Member("Ana");
        var (_, benId) = await Member("Ben");
        var (cleo, cleoId) = await Member("Cleo");
        var team = await TestHelpers.CreateGroupAsync(owner, org.Slug, "Team", new { groupAdminCanViewAllTimeEntries = leadViewsEntries },
            (leadId, "Admin"), (anaId, "Member"), (benId, "Member"));

        var clock = OrgClock.For("Europe/Berlin");
        var today = clock.Today();
        using (var scope = _factory.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<TimeTrackingDbContext>();
            var i = 0;
            foreach (var userId in new[] { anaId, benId, cleoId })
            {
                i++;
                db.WorkSchedules.Add(new WorkSchedule
                {
                    UserId = userId, OrganizationId = org.Id, ValidFrom = today.AddDays(-30), WeeklyWorkHours = 40,
                    TargetMon = 8, TargetTue = 8, TargetWed = 8, TargetThu = 8, TargetFri = 8
                });
                for (var d = 20; d >= 1; d -= 2)
                {
                    var start = clock.ToUtc(today.AddDays(-d).ToDateTime(new TimeOnly(8, 0)));
                    db.TimeEntries.Add(new TimeEntry
                    {
                        UserId = userId, OrganizationId = org.Id, StartTime = start, EndTime = start.AddHours(8 + i),
                        PauseDurationMinutes = 30, PauseIsManual = true, CreatedAt = start, UpdatedAt = start
                    });
                }
                db.AbsenceDays.Add(new AbsenceDay { UserId = userId, OrganizationId = org.Id, Date = today.AddDays(-3), Type = AbsenceType.Vacation });
            }
            var failure = clock.ToUtc(today.AddDays(-5).ToDateTime(new TimeOnly(9, 0)));
            db.TimeEntries.Add(new TimeEntry
            {
                UserId = benId, OrganizationId = org.Id, StartTime = failure, EndTime = failure.AddHours(15), IsFailure = true,
                CreatedAt = failure, UpdatedAt = failure
            });
            db.CoreHoursChecks.Add(new CoreHoursCheck
            {
                UserId = anaId, OrganizationId = org.Id, Date = today, WindowStart = new TimeOnly(0, 0), WindowEnd = new TimeOnly(0, 30),
                ToleranceMinutes = 0, Issues = CoreHoursIssue.Missing, MissingMinutes = 30, CheckedAt = DateTime.UtcNow
            });
            await db.SaveChangesAsync();
        }
        (await ana.PostAsJsonAsync("/api/v1/TimeTracking/start", new { organizationId = org.Id })).EnsureSuccessStatusCode();

        return new Setup(owner, org.Slug, org.Id, lead, leadId, ana, anaId, benId, cleo, cleoId, team);
    }

    private static async Task<(JsonElement Groups, Dictionary<int, JsonElement> Members)> OverviewAsync(HttpClient client, string slug)
    {
        var response = await client.GetAsync($"/api/v1/organizations/{slug}/team-overview");
        response.StatusCode.Should().Be(HttpStatusCode.OK);
        var body = await response.Content.ReadFromJsonAsync<JsonElement>();
        var members = body.GetProperty("members").EnumerateArray().ToList();
        return (body.GetProperty("groups"), members.ToDictionary(m => m.GetProperty("userId").GetInt32()));
    }

    private static bool IsNull(JsonElement member, string property) => member.GetProperty(property).ValueKind == JsonValueKind.Null;

    [Fact]
    public async Task Owner_SeesEveryoneButThemselves_BalanceEqualsBalanceEndpoint_PresenceFailuresCoreHours()
    {
        var s = await SetupAsync("tov-owner");
        var (groups, members) = await OverviewAsync(s.Owner, s.Slug);

        members.Keys.Should().BeEquivalentTo([s.LeadId, s.AnaId, s.BenId, s.CleoId]);
        groups.EnumerateArray().Select(g => g.GetProperty("name").GetString()).Should().Equal("Team");
        members[s.AnaId].GetProperty("groupIds").EnumerateArray().Select(g => g.GetInt32()).Should().Equal(s.TeamId);
        members[s.CleoId].GetProperty("groupIds").GetArrayLength().Should().Be(0);

        foreach (var id in new[] { s.AnaId, s.BenId, s.CleoId, s.LeadId })
        {
            var balance = await s.Owner.GetFromJsonAsync<JsonElement>($"/api/v1/organizations/{s.Slug}/members/{id}/balance");
            members[id].GetProperty("balanceHours").GetDouble().Should().Be(balance.GetProperty("cumulativeOvertimeHours").GetDouble());
            members[id].GetProperty("failureCount").GetInt32().Should().Be(balance.GetProperty("failureEntryCount").GetInt32());
        }
        members[s.BenId].GetProperty("balanceHours").GetDouble().Should().NotBe(0);

        members[s.AnaId].GetProperty("present").GetBoolean().Should().BeTrue();
        members[s.AnaId].GetProperty("presentSince").GetDateTime().Should().BeCloseTo(DateTime.UtcNow, TimeSpan.FromMinutes(1));
        members[s.AnaId].GetProperty("coreHoursViolationToday").GetBoolean().Should().BeTrue();
        // Ben's forgotten timer was closed as a failure entry: not present, but flagged
        members[s.BenId].GetProperty("present").GetBoolean().Should().BeFalse();
        IsNull(members[s.BenId], "presentSince").Should().BeTrue();
        members[s.BenId].GetProperty("failureCount").GetInt32().Should().Be(1);
        members[s.BenId].GetProperty("coreHoursViolationToday").GetBoolean().Should().BeFalse();
    }

    [Fact]
    public async Task CorrectedCoreHoursDay_LosesTheMarker()
    {
        var s = await SetupAsync("tov-core");
        using (var scope = _factory.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<TimeTrackingDbContext>();
            var check = db.CoreHoursChecks.Single(c => c.UserId == s.AnaId);
            var clock = OrgClock.For("Europe/Berlin");
            var start = clock.ToUtc(check.Date.ToDateTime(new TimeOnly(0, 0)));
            db.TimeEntries.Add(new TimeEntry
            {
                UserId = s.AnaId, OrganizationId = s.OrgId, StartTime = start, EndTime = start.AddMinutes(30), CreatedAt = start, UpdatedAt = start
            });
            await db.SaveChangesAsync();
        }
        var (_, members) = await OverviewAsync(s.Owner, s.Slug);
        members[s.AnaId].GetProperty("coreHoursViolationToday").GetBoolean().Should().BeFalse();
    }

    [Fact]
    public async Task AdminWithoutMemberTimeEntryVisibility_SeesPresence_NotTimes()
    {
        var s = await SetupAsync("tov-noview", adminsSeeTimes: false);
        var (_, members) = await OverviewAsync(s.Owner, s.Slug);

        members.Should().HaveCount(4);
        members[s.AnaId].GetProperty("present").GetBoolean().Should().BeTrue();
        foreach (var m in members.Values)
        {
            IsNull(m, "balanceHours").Should().BeTrue();
            IsNull(m, "failureCount").Should().BeTrue();
            IsNull(m, "coreHoursViolationToday").Should().BeTrue();
        }
    }

    [Fact]
    public async Task GroupAdminWithViewEntries_SeesOnlyTheirMembers_WithBalance()
    {
        var s = await SetupAsync("tov-lead");
        var (groups, members) = await OverviewAsync(s.Lead, s.Slug);

        members.Keys.Should().BeEquivalentTo([s.AnaId, s.BenId]);
        groups.EnumerateArray().Select(g => g.GetProperty("id").GetInt32()).Should().Equal(s.TeamId);
        var balance = await s.Lead.GetFromJsonAsync<JsonElement>($"/api/v1/organizations/{s.Slug}/members/{s.BenId}/balance");
        members[s.BenId].GetProperty("balanceHours").GetDouble().Should().Be(balance.GetProperty("cumulativeOvertimeHours").GetDouble());
        members[s.AnaId].GetProperty("present").GetBoolean().Should().BeTrue();
        members[s.BenId].GetProperty("failureCount").GetInt32().Should().Be(1);
    }

    [Fact]
    public async Task GroupAdminWithoutViewEntries_GetsPresenceOnly_AndOnlyWhereTheMatrixShowsIt()
    {
        var s = await SetupAsync("tov-lead-noview", leadViewsEntries: false);
        var (_, members) = await OverviewAsync(s.Lead, s.Slug);

        members.Keys.Should().BeEquivalentTo([s.AnaId, s.BenId]);
        foreach (var m in members.Values)
        {
            IsNull(m, "balanceHours").Should().BeTrue();
            IsNull(m, "failureCount").Should().BeTrue();
            IsNull(m, "present").Should().BeTrue("the default matrix hides presence");
        }

        (await s.Owner.PutAsJsonAsync($"/api/v1/Organizations/{s.Slug}/settings",
            new { visibility = TestHelpers.Matrix(("Presence", true, false)) })).EnsureSuccessStatusCode();
        (_, members) = await OverviewAsync(s.Lead, s.Slug);
        members[s.AnaId].GetProperty("present").GetBoolean().Should().BeTrue();
        members[s.BenId].GetProperty("present").GetBoolean().Should().BeFalse();
        IsNull(members[s.AnaId], "balanceHours").Should().BeTrue();
    }

    [Fact]
    public async Task LeadOfSeveralGroups_GetsThemAsFilters()
    {
        var s = await SetupAsync("tov-groups");
        var other = await TestHelpers.CreateGroupAsync(s.Owner, s.Slug, "Other", new { }, (s.LeadId, "Admin"), (s.CleoId, "Member"));
        var (groups, members) = await OverviewAsync(s.Lead, s.Slug);

        groups.EnumerateArray().Select(g => g.GetProperty("name").GetString()).Should().Equal("Other", "Team");
        members.Keys.Should().BeEquivalentTo([s.AnaId, s.BenId, s.CleoId]);
        members[s.CleoId].GetProperty("groupIds").EnumerateArray().Select(g => g.GetInt32()).Should().Equal(other);
        IsNull(members[s.CleoId], "balanceHours").Should().BeTrue("Other's admins may not view entries");
        members[s.AnaId].GetProperty("balanceHours").ValueKind.Should().Be(JsonValueKind.Number);
    }

    [Fact]
    public async Task PlainMember_AndOutsider_AreForbidden()
    {
        var s = await SetupAsync("tov-forbidden");
        (await s.Cleo.GetAsync($"/api/v1/organizations/{s.Slug}/team-overview")).StatusCode.Should().Be(HttpStatusCode.Forbidden);
        (await s.Ana.GetAsync($"/api/v1/organizations/{s.Slug}/team-overview")).StatusCode.Should().Be(HttpStatusCode.Forbidden);
        var (outsider, _) = await TestHelpers.CreateAuthenticatedUserAsync(_factory, "tov-outsider@team.test");
        (await outsider.GetAsync($"/api/v1/organizations/{s.Slug}/team-overview")).StatusCode.Should().Be(HttpStatusCode.Forbidden);
    }
}
