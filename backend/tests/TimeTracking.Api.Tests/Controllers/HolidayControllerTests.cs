using System.Net;
using System.Net.Http.Json;
using FluentAssertions;
using TimeTracking.Api.Tests.Infrastructure;

namespace TimeTracking.Api.Tests.Controllers;

/// <summary>
/// Integration tests for the Holiday controller: CRUD, preset import, and recurring holidays.
/// </summary>
public class HolidayControllerTests : IClassFixture<TimeTrackingApiFactory>
{
    private readonly TimeTrackingApiFactory _factory;

    public HolidayControllerTests(TimeTrackingApiFactory factory) => _factory = factory;

    // ── List holidays ────────────────────────────────────────────────────

    [Fact]
    public async Task GetHolidays_SeededOrg_ReturnsList()
    {
        var client = _factory.CreateClient();
        await TestHelpers.AuthenticateAsync(client, TestHelpers.SeedOwnerEmail, TestHelpers.SeedPassword);

        var response = await client.GetAsync($"/api/v1/organizations/{TestHelpers.SeedOrgSlug}/holidays");
        response.EnsureSuccessStatusCode();

        var holidays = await response.Content.ReadFromJsonAsync<List<HolidayResponseDto>>(TestHelpers.JsonOptions);
        holidays.Should().NotBeNull();
        holidays!.Should().HaveCountGreaterThanOrEqualTo(1);
    }

    // ── Create holiday ───────────────────────────────────────────────────

    [Fact]
    public async Task CreateHoliday_AsOwner_Succeeds()
    {
        var client = _factory.CreateClient();
        await TestHelpers.AuthenticateAsync(client, TestHelpers.SeedOwnerEmail, TestHelpers.SeedPassword);
        await TestHelpers.CreateOrganizationAsync(client, "Holiday Test", "holiday-test-org");

        var response = await client.PostAsJsonAsync("/api/v1/organizations/holiday-test-org/holidays", new
        {
            date = "2026-12-25",
            name = "Christmas",
            isRecurring = true
        });
        response.EnsureSuccessStatusCode();

        var holiday = await response.Content.ReadFromJsonAsync<HolidayResponseDto>(TestHelpers.JsonOptions);
        holiday!.Name.Should().Be("Christmas");
        holiday.IsRecurring.Should().BeTrue();
    }

    [Fact]
    public async Task CreateHoliday_DuplicateDate_ReturnsConflict()
    {
        var client = _factory.CreateClient();
        await TestHelpers.AuthenticateAsync(client, TestHelpers.SeedOwnerEmail, TestHelpers.SeedPassword);
        await TestHelpers.CreateOrganizationAsync(client, "Dup Holiday", "dup-holiday-org");

        await client.PostAsJsonAsync("/api/v1/organizations/dup-holiday-org/holidays", new
        {
            date = "2026-06-15",
            name = "Holiday A",
            isRecurring = false
        });

        var response = await client.PostAsJsonAsync("/api/v1/organizations/dup-holiday-org/holidays", new
        {
            date = "2026-06-15",
            name = "Holiday B",
            isRecurring = false
        });

        response.StatusCode.Should().Be(HttpStatusCode.Conflict);
    }

    [Fact]
    public async Task CreateHoliday_AsMember_ReturnsForbidden()
    {
        var client = _factory.CreateClient();
        await TestHelpers.AuthenticateAsync(client, TestHelpers.SeedMemberEmail, TestHelpers.SeedPassword);

        var response = await client.PostAsJsonAsync($"/api/v1/organizations/{TestHelpers.SeedOrgSlug}/holidays", new
        {
            date = "2026-11-11",
            name = "Veterans Day",
            isRecurring = false
        });

        response.StatusCode.Should().Be(HttpStatusCode.Forbidden);
    }

    // ── Update holiday ───────────────────────────────────────────────────

    [Fact]
    public async Task UpdateHoliday_AsOwner_Succeeds()
    {
        var client = _factory.CreateClient();
        await TestHelpers.AuthenticateAsync(client, TestHelpers.SeedOwnerEmail, TestHelpers.SeedPassword);
        await TestHelpers.CreateOrganizationAsync(client, "Holiday Update", "holiday-update-org");

        var createResp = await client.PostAsJsonAsync("/api/v1/organizations/holiday-update-org/holidays", new
        {
            date = "2026-01-01",
            name = "New Year",
            isRecurring = true
        });
        var created = await createResp.Content.ReadFromJsonAsync<HolidayResponseDto>(TestHelpers.JsonOptions);

        var updateResp = await client.PutAsJsonAsync(
            $"/api/v1/organizations/holiday-update-org/holidays/{created!.Id}", new
            {
                name = "New Year's Day"
            });
        updateResp.EnsureSuccessStatusCode();

        var updated = await updateResp.Content.ReadFromJsonAsync<HolidayResponseDto>(TestHelpers.JsonOptions);
        updated!.Name.Should().Be("New Year's Day");
    }

    // ── Delete holiday ───────────────────────────────────────────────────

    [Fact]
    public async Task DeleteHoliday_AsOwner_Returns204()
    {
        var client = _factory.CreateClient();
        await TestHelpers.AuthenticateAsync(client, TestHelpers.SeedOwnerEmail, TestHelpers.SeedPassword);
        await TestHelpers.CreateOrganizationAsync(client, "Holiday Delete", "holiday-delete-org");

        var createResp = await client.PostAsJsonAsync("/api/v1/organizations/holiday-delete-org/holidays", new
        {
            date = "2026-07-04",
            name = "Independence Day",
            isRecurring = false
        });
        var created = await createResp.Content.ReadFromJsonAsync<HolidayResponseDto>(TestHelpers.JsonOptions);

        var deleteResp = await client.DeleteAsync(
            $"/api/v1/organizations/holiday-delete-org/holidays/{created!.Id}");
        deleteResp.StatusCode.Should().Be(HttpStatusCode.NoContent);
    }

    // ── Preset import ────────────────────────────────────────────────────

    [Fact]
    public async Task GetAvailablePresets_ReturnsList()
    {
        var client = _factory.CreateClient();
        await TestHelpers.AuthenticateAsync(client, TestHelpers.SeedOwnerEmail, TestHelpers.SeedPassword);

        var response = await client.GetAsync("/api/v1/organizations/holiday-presets");
        response.EnsureSuccessStatusCode();
    }

    [Fact]
    public async Task ImportPresetHolidays_Germany_ImportsHolidays()
    {
        var client = _factory.CreateClient();
        await TestHelpers.AuthenticateAsync(client, TestHelpers.SeedOwnerEmail, TestHelpers.SeedPassword);
        await TestHelpers.CreateOrganizationAsync(client, "Preset Test", "preset-test-org");

        var response = await client.PostAsync(
            "/api/v1/organizations/preset-test-org/holidays/import-preset?preset=de&year=2026", null);
        response.EnsureSuccessStatusCode();

        var holidays = await response.Content.ReadFromJsonAsync<List<HolidayResponseDto>>(TestHelpers.JsonOptions);
        holidays.Should().NotBeNull();
        holidays!.Should().HaveCountGreaterThan(0);
        holidays.Should().Contain(h => h.Name.Contains("Neujahr") || h.Name.Contains("Tag"));
    }

    // ── Auth required ────────────────────────────────────────────────────

    [Fact]
    public async Task CreateHoliday_Unauthenticated_Returns401()
    {
        var client = _factory.CreateClient();
        var response = await client.PostAsJsonAsync(
            $"/api/v1/organizations/{TestHelpers.SeedOrgSlug}/holidays", new
            {
                date = "2026-10-03",
                name = "Test",
                isRecurring = false
            });

        response.StatusCode.Should().Be(HttpStatusCode.Unauthorized);
    }
}
