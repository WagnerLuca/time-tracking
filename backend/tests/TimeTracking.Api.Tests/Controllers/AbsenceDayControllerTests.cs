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
    public async Task CreateAbsence_AsMember_WhenRequiresApproval_IsForbidden()
    {
        // The seed org has AbsenceSelfServiceMode = RequiresApproval:
        // regular members must submit an absence request instead.
        var client = _factory.CreateClient();
        await TestHelpers.AuthenticateAsync(client, TestHelpers.SeedMemberEmail, TestHelpers.SeedPassword);

        var response = await client.PostAsJsonAsync(
            $"/api/v1/organizations/{TestHelpers.SeedOrgSlug}/absences", new
            {
                date = "2026-11-15",
                type = 0, // SickDay
                note = "Feeling unwell"
            });
        response.StatusCode.Should().Be(HttpStatusCode.Forbidden);
    }

    [Fact]
    public async Task CreateAbsence_AsMember_WhenAllowed_Succeeds()
    {
        // Orgs default to AbsenceSelfServiceMode = Allowed: members add absences directly
        var ownerClient = _factory.CreateClient();
        await TestHelpers.AuthenticateAsync(ownerClient, TestHelpers.SeedOwnerEmail, TestHelpers.SeedPassword);
        await TestHelpers.CreateOrganizationAsync(ownerClient, "Self Service Org", "self-service-org");

        var memberClient = _factory.CreateClient();
        var member = await TestHelpers.RegisterAsync(memberClient, "selfservice@test.com", "Password123!", "Self", "Service");
        TestHelpers.SetBearerToken(memberClient, member.AccessToken);
        // Owner adds the new user as a regular member
        await TestHelpers.AddMemberAsync(_factory, ownerClient, "self-service-org", member);

        var response = await memberClient.PostAsJsonAsync(
            "/api/v1/organizations/self-service-org/absences", new
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

    // ── Half-day absences ────────────────────────────────────────────────

    [Fact]
    public async Task CreateAbsence_HalfDay_Succeeds()
    {
        var client = _factory.CreateClient();
        await TestHelpers.AuthenticateAsync(client, TestHelpers.SeedOwnerEmail, TestHelpers.SeedPassword);
        await TestHelpers.CreateOrganizationAsync(client, "Half Abs Test", "half-abs-test");

        var response = await client.PostAsJsonAsync("/api/v1/organizations/half-abs-test/absences", new
        {
            date = "2026-07-15",
            type = 1, // Vacation
            isHalfDay = true,
            note = "Afternoon off"
        });
        response.EnsureSuccessStatusCode();

        var absence = await response.Content.ReadFromJsonAsync<AbsenceDayResponseDto>(TestHelpers.JsonOptions);
        absence!.Type.Should().Be("Vacation");
        absence.IsHalfDay.Should().BeTrue();
        absence.Note.Should().Be("Afternoon off");
    }

    [Fact]
    public async Task CreateAbsence_FullDay_DefaultsIsHalfDayToFalse()
    {
        var client = _factory.CreateClient();
        await TestHelpers.AuthenticateAsync(client, TestHelpers.SeedOwnerEmail, TestHelpers.SeedPassword);
        await TestHelpers.CreateOrganizationAsync(client, "Full Abs Def", "full-abs-def");

        var response = await client.PostAsJsonAsync("/api/v1/organizations/full-abs-def/absences", new
        {
            date = "2026-09-10",
            type = 0
        });
        response.EnsureSuccessStatusCode();

        var absence = await response.Content.ReadFromJsonAsync<AbsenceDayResponseDto>(TestHelpers.JsonOptions);
        absence!.IsHalfDay.Should().BeFalse();
    }

    [Fact]
    public async Task AdminCreateAbsence_HalfDay_Succeeds()
    {
        var client = _factory.CreateClient();
        await TestHelpers.AuthenticateAsync(client, TestHelpers.SeedOwnerEmail, TestHelpers.SeedPassword);

        var detail = await client.GetFromJsonAsync<OrganizationDetailResponseDto>(
            $"/api/v1/Organizations/{TestHelpers.SeedOrgSlug}", TestHelpers.JsonOptions);
        var tom = detail!.Members.First(m => m.Email == TestHelpers.SeedMemberEmail);

        var response = await client.PostAsJsonAsync(
            $"/api/v1/organizations/{TestHelpers.SeedOrgSlug}/absences/admin", new
            {
                userId = tom.Id,
                date = "2026-11-20",
                type = 1, // Vacation
                isHalfDay = true,
                note = "Half day vacation"
            });
        response.EnsureSuccessStatusCode();

        var absence = await response.Content.ReadFromJsonAsync<AbsenceDayResponseDto>(TestHelpers.JsonOptions);
        absence!.IsHalfDay.Should().BeTrue();
        absence.Type.Should().Be("Vacation");
    }

    // ── Custom absence types ─────────────────────────────────────────────

    [Fact]
    public async Task CustomAbsenceType_CrudAndUsage_Works()
    {
        var client = _factory.CreateClient();
        await TestHelpers.AuthenticateAsync(client, TestHelpers.SeedOwnerEmail, TestHelpers.SeedPassword);
        await TestHelpers.CreateOrganizationAsync(client, "Custom Type Org", "custom-type-org");

        // Create a custom type
        var createTypeResp = await client.PostAsJsonAsync("/api/v1/organizations/custom-type-org/absence-types", new
        {
            name = "Business Trip",
            color = "#0ea5e9",
            creditMode = 1 // FillToTarget
        });
        createTypeResp.EnsureSuccessStatusCode();
        var type = await createTypeResp.Content.ReadFromJsonAsync<AbsenceTypeDefinitionResponseDto>(TestHelpers.JsonOptions);
        type!.Name.Should().Be("Business Trip");
        type.CreditMode.Should().Be("FillToTarget");

        // Duplicate name → conflict
        (await client.PostAsJsonAsync("/api/v1/organizations/custom-type-org/absence-types", new
        {
            name = "Business Trip",
            creditMode = 0
        })).StatusCode.Should().Be(HttpStatusCode.Conflict);

        // List includes it
        var listResp = await client.GetAsync("/api/v1/organizations/custom-type-org/absence-types");
        listResp.EnsureSuccessStatusCode();
        var types = await listResp.Content.ReadFromJsonAsync<List<AbsenceTypeDefinitionResponseDto>>(TestHelpers.JsonOptions);
        types!.Should().ContainSingle(t => t.Name == "Business Trip");

        // Create an absence using the custom type
        var absResp = await client.PostAsJsonAsync("/api/v1/organizations/custom-type-org/absences", new
        {
            date = "2026-09-22",
            type = 2, // Other
            customTypeId = type.Id,
            note = "Client visit in Berlin"
        });
        absResp.EnsureSuccessStatusCode();
        var absence = await absResp.Content.ReadFromJsonAsync<AbsenceDayResponseDto>(TestHelpers.JsonOptions);
        absence!.Type.Should().Be("Other");
        absence.CustomTypeId.Should().Be(type.Id);
        absence.CustomTypeName.Should().Be("Business Trip");
        absence.CreditMode.Should().Be("FillToTarget");

        // Unknown custom type → 400
        (await client.PostAsJsonAsync("/api/v1/organizations/custom-type-org/absences", new
        {
            date = "2026-09-23",
            type = 2,
            customTypeId = 999999
        })).StatusCode.Should().Be(HttpStatusCode.BadRequest);

        // Deactivate the type — existing absences keep it, list no longer shows it
        (await client.DeleteAsync($"/api/v1/organizations/custom-type-org/absence-types/{type.Id}"))
            .StatusCode.Should().Be(HttpStatusCode.NoContent);
        var listAfter = await client.GetFromJsonAsync<List<AbsenceTypeDefinitionResponseDto>>(
            "/api/v1/organizations/custom-type-org/absence-types", TestHelpers.JsonOptions);
        listAfter!.Should().BeEmpty();
    }

    [Fact]
    public async Task GetAbsences_IncludesIsHalfDay()
    {
        var client = _factory.CreateClient();
        await TestHelpers.AuthenticateAsync(client, TestHelpers.SeedOwnerEmail, TestHelpers.SeedPassword);
        await TestHelpers.CreateOrganizationAsync(client, "List Half Abs", "list-half-abs");

        await client.PostAsJsonAsync("/api/v1/organizations/list-half-abs/absences", new
        {
            date = "2026-06-01",
            type = 0,
            isHalfDay = true,
            note = "Morning sick"
        });

        var response = await client.GetAsync("/api/v1/organizations/list-half-abs/absences");
        response.EnsureSuccessStatusCode();

        var page = await response.Content.ReadFromJsonAsync<PaginatedResponseDto<AbsenceDayResponseDto>>(TestHelpers.JsonOptions);
        page!.Items.Should().ContainSingle(a => a.Date == "2026-06-01" && a.IsHalfDay);
    }
}
