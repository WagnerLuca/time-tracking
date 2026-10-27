using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using FluentAssertions;
using TimeTracking.Api.Tests.Infrastructure;

namespace TimeTracking.Api.Tests.Controllers;

/// <summary>Who may change which work schedule and initial overtime, under each rule mode.</summary>
public class WorkScheduleRuleTests : IClassFixture<TimeTrackingApiFactory>
{
    private const int Disabled = 0, RequiresApproval = 1, Allowed = 2;

    private readonly TimeTrackingApiFactory _factory;

    public WorkScheduleRuleTests(TimeTrackingApiFactory factory) => _factory = factory;

    private async Task<(HttpClient Owner, HttpClient Member, string Slug, int MemberId)> SetupAsync(string prefix, object settings)
    {
        var (owner, _) = await TestHelpers.CreateAuthenticatedUserAsync(_factory, $"{prefix}-owner@ws.test");
        var org = await TestHelpers.CreateOrganizationAsync(owner, $"{prefix} Org", $"{prefix}-org");
        (await owner.PutAsJsonAsync($"/api/v1/Organizations/{org.Slug}/settings", settings)).EnsureSuccessStatusCode();
        var (member, m) = await TestHelpers.CreateAuthenticatedUserAsync(_factory, $"{prefix}-m@ws.test");
        (await TestHelpers.AddMemberAsync(_factory, owner, org.Slug, m)).EnsureSuccessStatusCode();
        return (owner, member, org.Slug, m.User.Id);
    }

    /// <summary>A change from a future day (members start today at the earliest).</summary>
    private static object Schedule() =>
        new { validFrom = DateOnly.FromDateTime(DateTime.UtcNow).AddDays(7).ToString("yyyy-MM-dd"), weeklyWorkHours = 32, distributeEvenly = true };

    [Fact]
    public async Task RequiresApproval_RejectsDirectChanges_WithAHintToRequest()
    {
        var (owner, member, slug, memberId) = await SetupAsync("ws-approval", new { workScheduleChangeMode = RequiresApproval });

        var create = await member.PostAsJsonAsync($"/api/v1/organizations/{slug}/work-schedule/change", Schedule());
        create.StatusCode.Should().Be(HttpStatusCode.BadRequest);
        (await create.Content.ReadAsStringAsync()).Should().Contain("request");

        // An admin can still set it for the member
        var byAdmin = await owner.PostAsJsonAsync($"/api/v1/organizations/{slug}/members/{memberId}/work-schedule/change", Schedule());
        byAdmin.StatusCode.Should().Be(HttpStatusCode.OK);
        (await byAdmin.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("future").GetProperty("weeklyWorkHours").GetDouble().Should().Be(32);

        // Cancelling that future one is a change too
        (await member.DeleteAsync($"/api/v1/organizations/{slug}/work-schedule/future"))
            .StatusCode.Should().Be(HttpStatusCode.BadRequest);
    }

    [Fact]
    public async Task GroupOverride_AllowsScheduleChanges_WhenOrgDisablesThem()
    {
        var (owner, member, slug, memberId) = await SetupAsync("ws-group", new { workScheduleChangeMode = Disabled });
        (await member.PostAsJsonAsync($"/api/v1/organizations/{slug}/work-schedule/change", Schedule()))
            .StatusCode.Should().Be(HttpStatusCode.Forbidden);

        var groupId = (await (await owner.PostAsJsonAsync($"/api/v1/Organizations/{slug}/groups", new { name = "Flex" }))
            .Content.ReadFromJsonAsync<JsonElement>()).GetProperty("id").GetInt32();
        (await owner.PostAsJsonAsync($"/api/v1/Organizations/{slug}/groups/{groupId}/members", new { userId = memberId })).EnsureSuccessStatusCode();
        (await owner.PutAsJsonAsync($"/api/v1/Organizations/{slug}/groups/{groupId}/permissions", new { workScheduleChangeMode = Allowed })).EnsureSuccessStatusCode();

        (await member.PostAsJsonAsync($"/api/v1/organizations/{slug}/work-schedule/change", Schedule())).IsSuccessStatusCode.Should().BeTrue();
    }

    [Theory]
    [InlineData(Disabled, HttpStatusCode.BadRequest)]
    [InlineData(RequiresApproval, HttpStatusCode.BadRequest)]
    [InlineData(Allowed, HttpStatusCode.OK)]
    public async Task SelfSetInitialOvertime_FollowsRule(int mode, HttpStatusCode expected)
    {
        var (_, member, slug, _) = await SetupAsync($"ws-ot{mode}", new { initialOvertimeMode = mode });
        (await member.PutAsJsonAsync($"/api/v1/organizations/{slug}/initial-overtime", new { initialOvertimeHours = 12.5 }))
            .StatusCode.Should().Be(expected);
    }

    [Fact]
    public async Task MemberSchedulesOfOthers_AreAdminOnly()
    {
        var (owner, member, slug, memberId) = await SetupAsync("ws-others", new { workScheduleChangeMode = Allowed });
        var (_, other) = await TestHelpers.CreateAuthenticatedUserAsync(_factory, "ws-others-x@ws.test");

        (await member.PostAsJsonAsync($"/api/v1/organizations/{slug}/members/{memberId}/work-schedule/change", Schedule()))
            .StatusCode.Should().Be(HttpStatusCode.Forbidden);
        (await member.DeleteAsync($"/api/v1/organizations/{slug}/members/{memberId}/work-schedule/future"))
            .StatusCode.Should().Be(HttpStatusCode.Forbidden);
        (await owner.PostAsJsonAsync($"/api/v1/organizations/{slug}/members/{other.User.Id}/work-schedule/change", Schedule()))
            .StatusCode.Should().Be(HttpStatusCode.NotFound);
    }
}
