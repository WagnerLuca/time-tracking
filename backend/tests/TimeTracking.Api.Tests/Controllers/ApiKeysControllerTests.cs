using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using FluentAssertions;
using TimeTracking.Api.Tests.Infrastructure;

namespace TimeTracking.Api.Tests.Controllers;

/// <summary>
/// Integration tests for organization API keys, member badges and the terminal punch endpoint.
/// </summary>
public class ApiKeysControllerTests : IClassFixture<TimeTrackingApiFactory>
{
    private readonly TimeTrackingApiFactory _factory;

    public ApiKeysControllerTests(TimeTrackingApiFactory factory) => _factory = factory;

    /// <summary>Owner with an org, one member with a badge, and a fresh API key.</summary>
    private async Task<(HttpClient Owner, HttpClient Member, string Slug, int MemberId, string Key, int KeyId)> SetupAsync(string prefix, string badge = "RFID-1")
    {
        var (owner, _) = await TestHelpers.CreateAuthenticatedUserAsync(_factory, $"{prefix}-owner@test.com");
        var org = await TestHelpers.CreateOrganizationAsync(owner, $"{prefix} Org", $"{prefix}-org");
        var (member, m) = await TestHelpers.CreateAuthenticatedUserAsync(_factory, $"{prefix}-m@test.com", "Mia", "Member");
        (await owner.PostAsJsonAsync($"/api/v1/Organizations/{org.Slug}/members", new { userId = m.User.Id })).EnsureSuccessStatusCode();
        (await owner.PutAsJsonAsync($"/api/v1/organizations/{org.Slug}/members/{m.User.Id}/badge", new { badgeId = badge })).EnsureSuccessStatusCode();

        var created = await owner.PostAsJsonAsync($"/api/v1/organizations/{org.Slug}/api-keys", new { name = "Entrance" });
        created.StatusCode.Should().Be(HttpStatusCode.Created);
        var body = await created.Content.ReadFromJsonAsync<JsonElement>();
        return (owner, member, org.Slug, m.User.Id, body.GetProperty("key").GetString()!, body.GetProperty("id").GetInt32());
    }

    private Task<HttpResponseMessage> PunchAsync(string? key, string badge)
    {
        var client = _factory.CreateClient();
        if (key != null) client.DefaultRequestHeaders.Add("X-Api-Key", key);
        return client.PostAsJsonAsync("/api/v1/terminal/punch", new { badgeId = badge });
    }

    [Fact]
    public async Task Punch_TogglesMembersTimer_StartThenStop()
    {
        var (_, member, _, _, key, _) = await SetupAsync("punch");

        var first = await PunchAsync(key, "RFID-1");
        first.StatusCode.Should().Be(HttpStatusCode.OK);
        var started = await first.Content.ReadFromJsonAsync<JsonElement>();
        started.GetProperty("action").GetString().Should().Be("Started");
        started.GetProperty("firstName").GetString().Should().Be("Mia");

        var current = await member.GetFromJsonAsync<TimeEntryResponseDto>("/api/v1/TimeTracking/current", TestHelpers.JsonOptions);
        current!.IsRunning.Should().BeTrue();

        var second = await PunchAsync(key, "RFID-1");
        (await second.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("action").GetString().Should().Be("Stopped");
    }

    [Fact]
    public async Task Punch_RejectsMissingWrongOrRevokedKey_AndUnknownBadge()
    {
        var (owner, _, slug, _, key, keyId) = await SetupAsync("punch-deny");

        (await PunchAsync(null, "RFID-1")).StatusCode.Should().Be(HttpStatusCode.Unauthorized);
        (await PunchAsync("tt_not-a-real-key", "RFID-1")).StatusCode.Should().Be(HttpStatusCode.Unauthorized);
        (await PunchAsync(key, "NOPE")).StatusCode.Should().Be(HttpStatusCode.NotFound);

        (await owner.DeleteAsync($"/api/v1/organizations/{slug}/api-keys/{keyId}")).EnsureSuccessStatusCode();
        (await PunchAsync(key, "RFID-1")).StatusCode.Should().Be(HttpStatusCode.Unauthorized);
    }

    [Fact]
    public async Task Key_OnlyReachesItsOwnOrganizationsBadges()
    {
        var (_, _, _, _, keyA, _) = await SetupAsync("iso-a", badge: "SHARED");
        await SetupAsync("iso-b", badge: "OTHER");

        (await PunchAsync(keyA, "OTHER")).StatusCode.Should().Be(HttpStatusCode.NotFound);
    }

    [Fact]
    public async Task Key_IsNotAUserCredential()
    {
        var (_, _, _, _, key, _) = await SetupAsync("not-user");
        var client = _factory.CreateClient();
        client.DefaultRequestHeaders.Add("X-Api-Key", key);

        (await client.GetAsync("/api/v1/Auth/me")).StatusCode.Should().Be(HttpStatusCode.Unauthorized);
    }

    [Fact]
    public async Task List_NeverReturnsSecret_AndMembersCannotManageKeysOrBadges()
    {
        var (owner, member, slug, memberId, key, _) = await SetupAsync("list");

        var list = await owner.GetStringAsync($"/api/v1/organizations/{slug}/api-keys");
        list.Should().Contain("Entrance").And.NotContain(key);

        (await member.GetAsync($"/api/v1/organizations/{slug}/api-keys")).StatusCode.Should().Be(HttpStatusCode.Forbidden);
        (await member.PostAsJsonAsync($"/api/v1/organizations/{slug}/api-keys", new { name = "x" })).StatusCode.Should().Be(HttpStatusCode.Forbidden);
        (await member.PutAsJsonAsync($"/api/v1/organizations/{slug}/members/{memberId}/badge", new { badgeId = "MINE" })).StatusCode.Should().Be(HttpStatusCode.Forbidden);
    }

    [Fact]
    public async Task Badge_MustBeUniquePerOrganization()
    {
        var (owner, _, slug, _, _, _) = await SetupAsync("dup-badge");
        var (_, other) = await TestHelpers.CreateAuthenticatedUserAsync(_factory, "dup-badge-2@test.com");
        (await owner.PostAsJsonAsync($"/api/v1/Organizations/{slug}/members", new { userId = other.User.Id })).EnsureSuccessStatusCode();

        var dup = await owner.PutAsJsonAsync($"/api/v1/organizations/{slug}/members/{other.User.Id}/badge", new { badgeId = "RFID-1" });
        dup.StatusCode.Should().Be(HttpStatusCode.Conflict);
    }
}
