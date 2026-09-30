using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using FluentAssertions;
using TimeTracking.Api.Tests.Infrastructure;

namespace TimeTracking.Api.Tests.Controllers;

/// <summary>
/// Approving a request applies its effects completely or not at all, exactly once, and with the
/// same validation and pause rules as doing the change directly.
/// </summary>
public class RequestApprovalTests : IClassFixture<TimeTrackingApiFactory>
{
    private const int RequiresApproval = 1, Allowed = 2;
    private const int EditPastEntry = 1, CsvImport = 5;

    private readonly TimeTrackingApiFactory _factory;

    public RequestApprovalTests(TimeTrackingApiFactory factory) => _factory = factory;

    private async Task<(HttpClient Owner, HttpClient Member, string Slug, int OrgId)> SetupAsync(string prefix, object settings)
    {
        var (owner, _) = await TestHelpers.CreateAuthenticatedUserAsync(_factory, $"{prefix}-owner@req.test");
        var org = await TestHelpers.CreateOrganizationAsync(owner, $"{prefix} Org", $"{prefix}-org");
        (await owner.PutAsJsonAsync($"/api/v1/Organizations/{org.Slug}/settings", settings)).EnsureSuccessStatusCode();
        var (member, m) = await TestHelpers.CreateAuthenticatedUserAsync(_factory, $"{prefix}-m@req.test");
        (await owner.PostAsJsonAsync($"/api/v1/Organizations/{org.Slug}/members", new { userId = m.User.Id })).EnsureSuccessStatusCode();
        return (owner, member, org.Slug, org.Id);
    }

    private static async Task<int> CreateRequestAsync(HttpClient member, string slug, int type, object data, int? relatedEntityId = null)
    {
        var response = await member.PostAsJsonAsync($"/api/v1/organizations/{slug}/requests",
            new { type, relatedEntityId, requestData = JsonSerializer.Serialize(data) });
        response.EnsureSuccessStatusCode();
        return (await response.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("id").GetInt32();
    }

    [Fact]
    public async Task InvalidEdit_IsRefused_AndRequestStaysPending()
    {
        var (owner, member, slug, orgId) = await SetupAsync("req-invalid", new { editPastEntriesMode = RequiresApproval });
        (await member.PostAsJsonAsync("/api/v1/TimeTracking/start", new { organizationId = orgId })).EnsureSuccessStatusCode();
        var entry = await (await member.PostAsJsonAsync("/api/v1/TimeTracking/stop", (object?)null)).Content.ReadFromJsonAsync<JsonElement>();
        var start = entry.GetProperty("startTime").GetDateTime();

        var id = await CreateRequestAsync(member, slug, EditPastEntry,
            new { endTime = start.AddHours(-1) }, entry.GetProperty("id").GetInt32());

        var accept = await owner.PutAsJsonAsync($"/api/v1/organizations/{slug}/requests/{id}", new { accept = true });
        accept.StatusCode.Should().Be(HttpStatusCode.BadRequest);

        var pending = await owner.GetFromJsonAsync<JsonElement>($"/api/v1/organizations/{slug}/requests");
        pending.GetProperty("items").EnumerateArray().Single(r => r.GetProperty("id").GetInt32() == id)
            .GetProperty("status").GetString().Should().Be("Pending");
    }

    [Fact]
    public async Task ApprovedEdit_AppliesPauseRules_LikeADirectEdit()
    {
        var (owner, member, slug, orgId) = await SetupAsync("req-pause", new { editPastEntriesMode = RequiresApproval, autoPauseEnabled = true });
        (await owner.PostAsJsonAsync($"/api/v1/organizations/{slug}/pause-rules", new { minHours = 6, pauseMinutes = 30 })).EnsureSuccessStatusCode();
        (await member.PostAsJsonAsync("/api/v1/TimeTracking/start", new { organizationId = orgId })).EnsureSuccessStatusCode();
        var entry = await (await member.PostAsJsonAsync("/api/v1/TimeTracking/stop", (object?)null)).Content.ReadFromJsonAsync<JsonElement>();
        var start = entry.GetProperty("startTime").GetDateTime().ToUniversalTime();

        var id = await CreateRequestAsync(member, slug, EditPastEntry, new { endTime = start.AddHours(8) }, entry.GetProperty("id").GetInt32());
        (await owner.PutAsJsonAsync($"/api/v1/organizations/{slug}/requests/{id}", new { accept = true })).EnsureSuccessStatusCode();

        var history = await member.GetFromJsonAsync<PaginatedResponseDto<TimeEntryResponseDto>>("/api/v1/TimeTracking", TestHelpers.JsonOptions);
        history!.Items.Single().PauseDurationMinutes.Should().Be(30);
    }

    [Fact]
    public async Task EditingDescription_KeepsAManuallySetPause()
    {
        var (_, member, _, orgId) = await SetupAsync("req-keep-pause", new { editPastEntriesMode = Allowed, editPauseMode = Allowed });
        (await member.PostAsJsonAsync("/api/v1/TimeTracking/start", new { organizationId = orgId })).EnsureSuccessStatusCode();
        var id = (await (await member.PostAsJsonAsync("/api/v1/TimeTracking/stop", (object?)null)).Content.ReadFromJsonAsync<JsonElement>()).GetProperty("id").GetInt32();

        (await member.PutAsJsonAsync($"/api/v1/TimeTracking/{id}", new { pauseDurationMinutes = 15 })).EnsureSuccessStatusCode();
        var edited = await (await member.PutAsJsonAsync($"/api/v1/TimeTracking/{id}", new { description = "renamed" })).Content.ReadFromJsonAsync<JsonElement>();

        edited.GetProperty("pauseDurationMinutes").GetInt32().Should().Be(15);
    }
}
