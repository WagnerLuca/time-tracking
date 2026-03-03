using System.Net;
using System.Net.Http.Json;
using FluentAssertions;
using TimeTracking.Api.Tests.Infrastructure;

namespace TimeTracking.Api.Tests.Controllers;

/// <summary>
/// Integration tests for the PauseRules controller: CRUD and validation.
/// </summary>
public class PauseRulesControllerTests : IClassFixture<TimeTrackingApiFactory>
{
    private readonly TimeTrackingApiFactory _factory;

    public PauseRulesControllerTests(TimeTrackingApiFactory factory) => _factory = factory;

    // ── List pause rules ─────────────────────────────────────────────────

    [Fact]
    public async Task GetPauseRules_SeededOrg_ReturnsRules()
    {
        var client = _factory.CreateClient();

        var response = await client.GetAsync($"/api/organizations/{TestHelpers.SeedOrgSlug}/pause-rules");
        response.EnsureSuccessStatusCode();

        var rules = await response.Content.ReadFromJsonAsync<List<PauseRuleResponseDto>>(TestHelpers.JsonOptions);
        rules.Should().NotBeNull();
        rules!.Should().HaveCountGreaterThanOrEqualTo(1);
    }

    // ── Create pause rule ────────────────────────────────────────────────

    [Fact]
    public async Task CreatePauseRule_AsOwner_Returns201()
    {
        var client = _factory.CreateClient();
        await TestHelpers.AuthenticateAsync(client, TestHelpers.SeedOwnerEmail, TestHelpers.SeedPassword);
        await TestHelpers.CreateOrganizationAsync(client, "Pause Rule Test", "pause-rule-test");

        var response = await client.PostAsJsonAsync("/api/organizations/pause-rule-test/pause-rules", new
        {
            minHours = 6.0,
            pauseMinutes = 30
        });
        response.StatusCode.Should().Be(HttpStatusCode.Created);

        var rule = await response.Content.ReadFromJsonAsync<PauseRuleResponseDto>(TestHelpers.JsonOptions);
        rule!.MinHours.Should().Be(6.0);
        rule.PauseMinutes.Should().Be(30);
    }

    [Fact]
    public async Task CreatePauseRule_DuplicateMinHours_ReturnsConflict()
    {
        var client = _factory.CreateClient();
        await TestHelpers.AuthenticateAsync(client, TestHelpers.SeedOwnerEmail, TestHelpers.SeedPassword);
        await TestHelpers.CreateOrganizationAsync(client, "Dup Pause", "dup-pause-org");

        await client.PostAsJsonAsync("/api/organizations/dup-pause-org/pause-rules", new
        {
            minHours = 8.0,
            pauseMinutes = 45
        });

        var response = await client.PostAsJsonAsync("/api/organizations/dup-pause-org/pause-rules", new
        {
            minHours = 8.0,
            pauseMinutes = 30
        });

        response.StatusCode.Should().Be(HttpStatusCode.Conflict);
    }

    [Theory]
    [InlineData(0, 30)]    // minHours below range
    [InlineData(25, 30)]   // minHours above 24h
    [InlineData(6.0, 0)]   // pauseMinutes below range
    [InlineData(6.0, 500)] // pauseMinutes above 480
    public async Task CreatePauseRule_InvalidInput_Returns400(double minHours, int pauseMinutes)
    {
        var client = _factory.CreateClient();
        await TestHelpers.AuthenticateAsync(client, TestHelpers.SeedOwnerEmail, TestHelpers.SeedPassword);

        var response = await client.PostAsJsonAsync(
            $"/api/organizations/{TestHelpers.SeedOrgSlug}/pause-rules", new
            {
                minHours,
                pauseMinutes
            });

        response.StatusCode.Should().Be(HttpStatusCode.BadRequest);
    }

    [Fact]
    public async Task CreatePauseRule_AsMember_ReturnsForbidden()
    {
        var client = _factory.CreateClient();
        await TestHelpers.AuthenticateAsync(client, TestHelpers.SeedMemberEmail, TestHelpers.SeedPassword);

        var response = await client.PostAsJsonAsync(
            $"/api/organizations/{TestHelpers.SeedOrgSlug}/pause-rules", new
            {
                minHours = 10.0,
                pauseMinutes = 60
            });

        response.StatusCode.Should().Be(HttpStatusCode.Forbidden);
    }

    // ── Update pause rule ────────────────────────────────────────────────

    [Fact]
    public async Task UpdatePauseRule_AsOwner_Succeeds()
    {
        var client = _factory.CreateClient();
        await TestHelpers.AuthenticateAsync(client, TestHelpers.SeedOwnerEmail, TestHelpers.SeedPassword);
        await TestHelpers.CreateOrganizationAsync(client, "Update Pause", "update-pause-org");

        var createResp = await client.PostAsJsonAsync("/api/organizations/update-pause-org/pause-rules", new
        {
            minHours = 4.0,
            pauseMinutes = 15
        });
        var created = await createResp.Content.ReadFromJsonAsync<PauseRuleResponseDto>(TestHelpers.JsonOptions);

        var updateResp = await client.PutAsJsonAsync(
            $"/api/organizations/update-pause-org/pause-rules/{created!.Id}", new
            {
                minHours = 5.0,
                pauseMinutes = 20
            });
        updateResp.EnsureSuccessStatusCode();

        var updated = await updateResp.Content.ReadFromJsonAsync<PauseRuleResponseDto>(TestHelpers.JsonOptions);
        updated!.MinHours.Should().Be(5.0);
        updated.PauseMinutes.Should().Be(20);
    }

    // ── Delete pause rule ────────────────────────────────────────────────

    [Fact]
    public async Task DeletePauseRule_AsOwner_Returns204()
    {
        var client = _factory.CreateClient();
        await TestHelpers.AuthenticateAsync(client, TestHelpers.SeedOwnerEmail, TestHelpers.SeedPassword);
        await TestHelpers.CreateOrganizationAsync(client, "Delete Pause", "delete-pause-org");

        var createResp = await client.PostAsJsonAsync("/api/organizations/delete-pause-org/pause-rules", new
        {
            minHours = 3.0,
            pauseMinutes = 10
        });
        var created = await createResp.Content.ReadFromJsonAsync<PauseRuleResponseDto>(TestHelpers.JsonOptions);

        var deleteResp = await client.DeleteAsync(
            $"/api/organizations/delete-pause-org/pause-rules/{created!.Id}");
        deleteResp.StatusCode.Should().Be(HttpStatusCode.NoContent);
    }

    // ── Auth for write operations ────────────────────────────────────────

    [Fact]
    public async Task CreatePauseRule_Unauthenticated_Returns401()
    {
        var client = _factory.CreateClient();
        var response = await client.PostAsJsonAsync(
            $"/api/organizations/{TestHelpers.SeedOrgSlug}/pause-rules", new
            {
                minHours = 6.0,
                pauseMinutes = 30
            });

        response.StatusCode.Should().Be(HttpStatusCode.Unauthorized);
    }

    // ── Read is public (no auth required for GET) ────────────────────────

    [Fact]
    public async Task GetPauseRules_Unauthenticated_ReturnsOk()
    {
        var client = _factory.CreateClient();
        var response = await client.GetAsync($"/api/organizations/{TestHelpers.SeedOrgSlug}/pause-rules");
        response.EnsureSuccessStatusCode();
    }
}
