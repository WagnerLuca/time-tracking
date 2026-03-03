using System.Net;
using System.Net.Http.Json;
using FluentAssertions;
using TimeTracking.Api.Tests.Infrastructure;

namespace TimeTracking.Api.Tests.Controllers;

/// <summary>
/// Integration tests for the AbsenceDay controller: user absences, admin absences,
/// filtering, and duplicate detection.
/// </summary>
public class AbsenceDayControllerTests : IClassFixture<TimeTrackingApiFactory>
{
    private readonly TimeTrackingApiFactory _factory;

    public AbsenceDayControllerTests(TimeTrackingApiFactory factory) => _factory = factory;

    // ── List absences ────────────────────────────────────────────────────

    [Fact]
    public async Task GetAbsences_ReturnsSeededAbsences()
    {
        var client = _factory.CreateClient();
        await TestHelpers.AuthenticateAsync(client, TestHelpers.SeedOwnerEmail, TestHelpers.SeedPassword);

        var response = await client.GetAsync($"/api/v1/organizations/{TestHelpers.SeedOrgSlug}/absences");
        response.EnsureSuccessStatusCode();

        var page = await response.Content.ReadFromJsonAsync<PaginatedResponseDto<AbsenceDayResponseDto>>(TestHelpers.JsonOptions);
        page.Should().NotBeNull();
    }

    [Fact]
    public async Task GetAbsences_WithDateFilter_FiltersCorrectly()
    {
        var client = _factory.CreateClient();
        await TestHelpers.AuthenticateAsync(client, TestHelpers.SeedOwnerEmail, TestHelpers.SeedPassword);

        // Far future — should be empty
        var response = await client.GetAsync(
            $"/api/v1/organizations/{TestHelpers.SeedOrgSlug}/absences?from=2099-01-01&to=2099-12-31");
        response.EnsureSuccessStatusCode();

        var page = await response.Content.ReadFromJsonAsync<PaginatedResponseDto<AbsenceDayResponseDto>>(TestHelpers.JsonOptions);
        page.Should().NotBeNull();
        page!.Items.Should().BeEmpty();
    }

    // ── Create absence (user) ────────────────────────────────────────────

    [Fact]
    public async Task CreateAbsence_AsMember_Succeeds()
    {
        var client = _factory.CreateClient();
        await TestHelpers.AuthenticateAsync(client, TestHelpers.SeedMemberEmail, TestHelpers.SeedPassword);

        var response = await client.PostAsJsonAsync(
            $"/api/v1/organizations/{TestHelpers.SeedOrgSlug}/absences", new
            {
                date = "2026-11-15",
                type = 0, // SickDay
                note = "Feeling unwell"
            });
        response.EnsureSuccessStatusCode();

        var absence = await response.Content.ReadFromJsonAsync<AbsenceDayResponseDto>(TestHelpers.JsonOptions);
        absence!.Type.Should().Be("SickDay");
        absence.Note.Should().Be("Feeling unwell");
    }

    [Fact]
    public async Task CreateAbsence_Vacation_Succeeds()
    {
        var client = _factory.CreateClient();
        await TestHelpers.AuthenticateAsync(client, TestHelpers.SeedOwnerEmail, TestHelpers.SeedPassword);

        var response = await client.PostAsJsonAsync(
            $"/api/v1/organizations/{TestHelpers.SeedOrgSlug}/absences", new
            {
                date = "2026-12-20",
                type = 1, // Vacation
                note = "Winter break"
            });
        response.EnsureSuccessStatusCode();

        var absence = await response.Content.ReadFromJsonAsync<AbsenceDayResponseDto>(TestHelpers.JsonOptions);
        absence!.Type.Should().Be("Vacation");
    }

    [Fact]
    public async Task CreateAbsence_DuplicateDate_ReturnsConflict()
    {
        var client = _factory.CreateClient();
        await TestHelpers.AuthenticateAsync(client, TestHelpers.SeedOwnerEmail, TestHelpers.SeedPassword);
        await TestHelpers.CreateOrganizationAsync(client, "Abs Dup Test", "abs-dup-test");

        await client.PostAsJsonAsync("/api/v1/organizations/abs-dup-test/absences", new
        {
            date = "2026-03-15",
            type = 0
        });

        var response = await client.PostAsJsonAsync("/api/v1/organizations/abs-dup-test/absences", new
        {
            date = "2026-03-15",
            type = 1
        });

        response.StatusCode.Should().Be(HttpStatusCode.Conflict);
    }

    // ── Create absence (admin) ───────────────────────────────────────────

    [Fact]
    public async Task AdminCreateAbsence_AsOwner_Succeeds()
    {
        var client = _factory.CreateClient();
        await TestHelpers.AuthenticateAsync(client, TestHelpers.SeedOwnerEmail, TestHelpers.SeedPassword);

        // Get Tom's user ID
        var detail = await client.GetFromJsonAsync<OrganizationDetailResponseDto>(
            $"/api/v1/Organizations/{TestHelpers.SeedOrgSlug}", TestHelpers.JsonOptions);
        var tom = detail!.Members.First(m => m.Email == TestHelpers.SeedMemberEmail);

        var response = await client.PostAsJsonAsync(
            $"/api/v1/organizations/{TestHelpers.SeedOrgSlug}/absences/admin", new
            {
                userId = tom.Id,
                date = "2026-10-20",
                type = 2, // Other
                note = "Company event"
            });
        response.EnsureSuccessStatusCode();

        var absence = await response.Content.ReadFromJsonAsync<AbsenceDayResponseDto>(TestHelpers.JsonOptions);
        absence!.Type.Should().Be("Other");
    }

    [Fact]
    public async Task AdminCreateAbsence_AsMember_ReturnsForbidden()
    {
        var client = _factory.CreateClient();
        await TestHelpers.AuthenticateAsync(client, TestHelpers.SeedMemberEmail, TestHelpers.SeedPassword);

        var response = await client.PostAsJsonAsync(
            $"/api/v1/organizations/{TestHelpers.SeedOrgSlug}/absences/admin", new
            {
                userId = 1,
                date = "2026-10-21",
                type = 0
            });

        response.StatusCode.Should().Be(HttpStatusCode.Forbidden);
    }

    // ── Delete absence ───────────────────────────────────────────────────

    [Fact]
    public async Task DeleteAbsence_OwnAbsence_Returns204()
    {
        var client = _factory.CreateClient();
        await TestHelpers.AuthenticateAsync(client, TestHelpers.SeedOwnerEmail, TestHelpers.SeedPassword);
        await TestHelpers.CreateOrganizationAsync(client, "Abs Del Test", "abs-del-test");

        var createResp = await client.PostAsJsonAsync("/api/v1/organizations/abs-del-test/absences", new
        {
            date = "2026-08-01",
            type = 0
        });
        var created = await createResp.Content.ReadFromJsonAsync<AbsenceDayResponseDto>(TestHelpers.JsonOptions);

        var deleteResp = await client.DeleteAsync($"/api/v1/organizations/abs-del-test/absences/{created!.Id}");
        deleteResp.StatusCode.Should().Be(HttpStatusCode.NoContent);
    }

    [Fact]
    public async Task DeleteAbsence_NonExistent_Returns404()
    {
        var client = _factory.CreateClient();
        await TestHelpers.AuthenticateAsync(client, TestHelpers.SeedOwnerEmail, TestHelpers.SeedPassword);

        var response = await client.DeleteAsync(
            $"/api/v1/organizations/{TestHelpers.SeedOrgSlug}/absences/999999");
        response.StatusCode.Should().Be(HttpStatusCode.NotFound);
    }

    // ── Auth required ────────────────────────────────────────────────────

    [Fact]
    public async Task AllEndpoints_Unauthenticated_Return401()
    {
        var client = _factory.CreateClient();

        (await client.GetAsync($"/api/v1/organizations/{TestHelpers.SeedOrgSlug}/absences"))
            .StatusCode.Should().Be(HttpStatusCode.Unauthorized);
        (await client.PostAsJsonAsync($"/api/v1/organizations/{TestHelpers.SeedOrgSlug}/absences", new { date = "2026-01-01", type = 0 }))
            .StatusCode.Should().Be(HttpStatusCode.Unauthorized);
        (await client.DeleteAsync($"/api/v1/organizations/{TestHelpers.SeedOrgSlug}/absences/1"))
            .StatusCode.Should().Be(HttpStatusCode.Unauthorized);
    }
}
