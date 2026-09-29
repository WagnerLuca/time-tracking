using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text;
using System.Text.Json;
using FluentAssertions;
using TimeTracking.Api.Tests.Infrastructure;

namespace TimeTracking.Api.Tests.Controllers;

/// <summary>
/// Integration tests for CSV export, the two-step CSV import, and how the
/// CsvImportMode rule (org default + group overrides) gates it.
/// </summary>
public class ExportImportControllerTests : IClassFixture<TimeTrackingApiFactory>
{
    private const int Disabled = 0, RequiresApproval = 1, Allowed = 2;

    private readonly TimeTrackingApiFactory _factory;

    public ExportImportControllerTests(TimeTrackingApiFactory factory) => _factory = factory;

    private async Task<(HttpClient Owner, HttpClient Member, string Slug, int MemberId)> SetupAsync(string prefix, int csvImportMode)
    {
        var (owner, _) = await TestHelpers.CreateAuthenticatedUserAsync(_factory, $"{prefix}-owner@test.com");
        var org = await TestHelpers.CreateOrganizationAsync(owner, $"{prefix} Org", $"{prefix}-org");
        var (member, m) = await TestHelpers.CreateAuthenticatedUserAsync(_factory, $"{prefix}-m@test.com");
        (await owner.PostAsJsonAsync($"/api/v1/Organizations/{org.Slug}/members", new { userId = m.User.Id })).EnsureSuccessStatusCode();
        (await owner.PutAsJsonAsync($"/api/v1/Organizations/{org.Slug}/settings", new { csvImportMode })).EnsureSuccessStatusCode();
        return (owner, member, org.Slug, m.User.Id);
    }

    private static Task<HttpResponseMessage> PreviewAsync(HttpClient client, string slug, string csv)
    {
        var form = new MultipartFormDataContent();
        var file = new ByteArrayContent(Encoding.UTF8.GetBytes(csv));
        file.Headers.ContentType = new MediaTypeHeaderValue("text/csv");
        form.Add(file, "file", "entries.csv");
        return client.PostAsync($"/api/v1/ExportImport/import/preview?orgSlug={slug}", form);
    }

    private static Task<HttpResponseMessage> ConfirmAsync(HttpClient client, string slug) =>
        client.PostAsJsonAsync($"/api/v1/ExportImport/import/confirm?orgSlug={slug}", new[]
        {
            new { date = "2026-03-02", startTime = "08:00", endTime = "16:30", pauseMinutes = 30, description = "Imported" }
        });

    [Fact]
    public async Task Import_PreviewThenConfirm_ThenExportContainsEntry()
    {
        var (_, member, slug, _) = await SetupAsync("csv-ok", Allowed);

        var preview = await PreviewAsync(member, slug, "Date,Start Time,End Time,Pause,Description\n2026-03-02,08:00,16:30,30,Imported\n2026-03-03,nonsense,17:00,0,Bad\n");
        preview.EnsureSuccessStatusCode();
        var rows = (await preview.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("rows");
        rows.GetArrayLength().Should().Be(2);
        rows[0].GetProperty("pauseMinutes").GetInt32().Should().Be(30);
        rows[1].GetProperty("warning").GetString().Should().NotBeNullOrEmpty();

        var confirm = await ConfirmAsync(member, slug);
        confirm.EnsureSuccessStatusCode();
        (await confirm.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("importedCount").GetInt32().Should().Be(1);

        // Same row again is now flagged as a duplicate
        var again = await PreviewAsync(member, slug, "Date,Start Time,End Time\n2026-03-02,08:00,16:30\n");
        (await again.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("duplicateCount").GetInt32().Should().Be(1);

        var export = await member.PostAsJsonAsync("/api/v1/ExportImport/export", new { type = "entries", organizationSlug = slug });
        export.EnsureSuccessStatusCode();
        var csv = await export.Content.ReadAsStringAsync();
        csv.Should().Contain("2026-03-02").And.Contain("Imported");
    }

    [Fact]
    public async Task Import_MissingRequiredColumns_Returns400()
    {
        var (_, member, slug, _) = await SetupAsync("csv-cols", Allowed);
        var preview = await PreviewAsync(member, slug, "Foo,Bar\n1,2\n");
        preview.StatusCode.Should().Be(HttpStatusCode.BadRequest);
    }

    [Theory]
    [InlineData(Disabled)]
    [InlineData(RequiresApproval)]
    public async Task Confirm_IsForbidden_UnlessAllowed(int mode)
    {
        var (_, member, slug, _) = await SetupAsync($"csv-deny{mode}", mode);
        (await ConfirmAsync(member, slug)).StatusCode.Should().Be(HttpStatusCode.Forbidden);
    }

    [Fact]
    public async Task GroupOverride_AllowsImport_WhenOrgDefaultIsDisabled()
    {
        var (owner, member, slug, memberId) = await SetupAsync("csv-group", Disabled);

        var created = await owner.PostAsJsonAsync($"/api/v1/Organizations/{slug}/groups", new { name = "Importers" });
        var groupId = (await created.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("id").GetInt32();
        (await owner.PostAsJsonAsync($"/api/v1/Organizations/{slug}/groups/{groupId}/members", new { userId = memberId })).EnsureSuccessStatusCode();
        (await owner.PutAsJsonAsync($"/api/v1/Organizations/{slug}/groups/{groupId}/permissions", new { csvImportMode = Allowed })).EnsureSuccessStatusCode();

        (await PreviewAsync(member, slug, "Date,Start Time,End Time\n2026-03-02,08:00,16:30\n")).EnsureSuccessStatusCode();
        (await ConfirmAsync(member, slug)).EnsureSuccessStatusCode();
    }

    [Fact]
    public async Task Export_OtherMembersData_ForbiddenForPlainMember()
    {
        var (_, member, slug, _) = await SetupAsync("csv-other", Allowed);
        var (_, stranger) = await TestHelpers.CreateAuthenticatedUserAsync(_factory, "csv-other-x@test.com");

        var export = await member.PostAsJsonAsync("/api/v1/ExportImport/export",
            new { type = "entries", organizationSlug = slug, targetUserId = stranger.User.Id });
        export.IsSuccessStatusCode.Should().BeFalse();
    }

    // ── CSV export ────────────────────────────────────────────────────────

    // ── CSV Export ───────────────────────────────────────────────────────

    [Fact]
    public async Task ExportCsv_ReturnsValidCsvFile()
    {
        var client = _factory.CreateClient();
        await TestHelpers.AuthenticateAsync(client, TestHelpers.SeedOwnerEmail, TestHelpers.SeedPassword);

        var response = await client.PostAsJsonAsync("/api/v1/ExportImport/export", new
        {
            type = "entries"
        });
        response.StatusCode.Should().Be(HttpStatusCode.OK);
        response.Content.Headers.ContentType!.MediaType.Should().Be("text/csv");
        response.Content.Headers.ContentDisposition.Should().NotBeNull();
        response.Content.Headers.ContentDisposition!.FileName.Should().EndWith(".csv");

        var csv = await response.Content.ReadAsStringAsync();
        csv.Should().Contain("Date,Day,Start Time,End Time,Duration (h),Pause (min),Net Duration (h),Description,Organization");
        // Seed data should produce at least one data row
        var lines = csv.Split('\n', StringSplitOptions.RemoveEmptyEntries);
        lines.Length.Should().BeGreaterThanOrEqualTo(2); // header + at least one row
    }

    [Fact]
    public async Task ExportCsv_WithDateFilter_FiltersCorrectly()
    {
        var client = _factory.CreateClient();
        await TestHelpers.AuthenticateAsync(client, TestHelpers.SeedOwnerEmail, TestHelpers.SeedPassword);

        // Far future date — should return only header
        var response = await client.PostAsJsonAsync("/api/v1/ExportImport/export", new
        {
            type = "entries",
            from = "2099-01-01T00:00:00Z",
            to = "2099-12-31T00:00:00Z"
        });
        response.StatusCode.Should().Be(HttpStatusCode.OK);

        var csv = await response.Content.ReadAsStringAsync();
        var lines = csv.Split('\n', StringSplitOptions.RemoveEmptyEntries);
        lines.Length.Should().Be(1); // Only header
    }

    [Fact]
    public async Task ExportCsv_WithOrgFilter_FiltersCorrectly()
    {
        var client = _factory.CreateClient();
        await TestHelpers.AuthenticateAsync(client, TestHelpers.SeedOwnerEmail, TestHelpers.SeedPassword);

        // Non-existent org slug — should return only header (no entries match)
        var response = await client.PostAsJsonAsync("/api/v1/ExportImport/export", new
        {
            type = "entries",
            organizationSlug = "non-existent-slug-999"
        });
        // Org not found → 404, since the service returns NotFound
        response.StatusCode.Should().Be(HttpStatusCode.NotFound);
    }

    [Fact]
    public async Task ExportCsv_ExcludesRunningEntries()
    {
        var (client, _) = await TestHelpers.CreateAuthenticatedUserAsync(_factory, "csvrun@test.com");

        // Start an entry (running, no end time)
        await client.PostAsJsonAsync("/api/v1/TimeTracking/start", new { description = "Still running" });

        var response = await client.PostAsJsonAsync("/api/v1/ExportImport/export", new
        {
            type = "entries"
        });
        response.StatusCode.Should().Be(HttpStatusCode.OK);

        var csv = await response.Content.ReadAsStringAsync();
        var lines = csv.Split('\n', StringSplitOptions.RemoveEmptyEntries);
        // The running entry should not appear in export
        lines.Length.Should().Be(1); // Only header
        csv.Should().NotContain("Still running");

        // Clean up
        await client.PostAsJsonAsync("/api/v1/TimeTracking/stop", (object?)null);
    }

    [Fact]
    public async Task ExportCsv_Unauthenticated_Returns401()
    {
        var client = _factory.CreateClient();

        var response = await client.PostAsJsonAsync("/api/v1/ExportImport/export", new
        {
            type = "entries"
        });
        response.StatusCode.Should().Be(HttpStatusCode.Unauthorized);
    }

    // ── Daily Report Export ──────────────────────────────────────────────

    [Fact]
    public async Task ExportDailyReport_ReturnsValidCsvWithHeaders()
    {
        var client = _factory.CreateClient();
        await TestHelpers.AuthenticateAsync(client, TestHelpers.SeedOwnerEmail, TestHelpers.SeedPassword);

        var response = await client.PostAsJsonAsync("/api/v1/ExportImport/export", new
        {
            type = "daily",
            organizationSlug = TestHelpers.SeedOrgSlug
        });
        response.StatusCode.Should().Be(HttpStatusCode.OK);
        response.Content.Headers.ContentType!.MediaType.Should().Be("text/csv");

        var csv = await response.Content.ReadAsStringAsync();
        csv.Should().Contain("Date,Day,Target (h),Worked (h),Pause (min),Net Worked (h),Overtime (h),Cumulative Overtime (h),Status,Holiday,Absence Type,Absence Note,Description,Organization");

        // Should have header + at least one day row (defaults to current month)
        var lines = csv.Split('\n', StringSplitOptions.RemoveEmptyEntries);
        lines.Length.Should().BeGreaterThanOrEqualTo(2);
    }

    [Fact]
    public async Task ExportDailyReport_WithDateRange_ReturnsCorrectDays()
    {
        var client = _factory.CreateClient();
        await TestHelpers.AuthenticateAsync(client, TestHelpers.SeedOwnerEmail, TestHelpers.SeedPassword);

        // Request exactly 7 days
        var response = await client.PostAsJsonAsync("/api/v1/ExportImport/export", new
        {
            type = "daily",
            organizationSlug = TestHelpers.SeedOrgSlug,
            from = "2026-01-05T00:00:00Z",
            to = "2026-01-11T00:00:00Z"
        });
        response.EnsureSuccessStatusCode();

        var csv = await response.Content.ReadAsStringAsync();
        var lines = csv.Split('\n', StringSplitOptions.RemoveEmptyEntries);
        lines.Length.Should().Be(8); // header + 7 days

        // Should contain the correct dates
        csv.Should().Contain("2026-01-05");
        csv.Should().Contain("2026-01-11");
    }

    [Fact]
    public async Task ExportDailyReport_NonMember_ReturnsForbidden()
    {
        var (client, _) = await TestHelpers.CreateAuthenticatedUserAsync(_factory, "nonmember-export@test.com");

        var response = await client.PostAsJsonAsync("/api/v1/ExportImport/export", new
        {
            type = "daily",
            organizationSlug = TestHelpers.SeedOrgSlug
        });
        response.StatusCode.Should().Be(HttpStatusCode.Forbidden);
    }

    [Fact]
    public async Task ExportDailyReport_NonExistentOrg_ReturnsNotFound()
    {
        var client = _factory.CreateClient();
        await TestHelpers.AuthenticateAsync(client, TestHelpers.SeedOwnerEmail, TestHelpers.SeedPassword);

        var response = await client.PostAsJsonAsync("/api/v1/ExportImport/export", new
        {
            type = "daily",
            organizationSlug = "does-not-exist-org"
        });
        response.StatusCode.Should().Be(HttpStatusCode.NotFound);
    }

    [Fact]
    public async Task ExportDailyReport_ContainsWeekendStatus()
    {
        var client = _factory.CreateClient();
        await TestHelpers.AuthenticateAsync(client, TestHelpers.SeedOwnerEmail, TestHelpers.SeedPassword);

        // 2026-01-10 is a Saturday, 2026-01-11 is a Sunday
        var response = await client.PostAsJsonAsync("/api/v1/ExportImport/export", new
        {
            type = "daily",
            organizationSlug = TestHelpers.SeedOrgSlug,
            from = "2026-01-10T00:00:00Z",
            to = "2026-01-11T00:00:00Z"
        });
        response.EnsureSuccessStatusCode();

        var csv = await response.Content.ReadAsStringAsync();
        csv.Should().Contain("Weekend");
        csv.Should().Contain("Sat");
        csv.Should().Contain("Sun");
    }

    [Fact]
    public async Task ExportDailyReport_Unauthenticated_Returns401()
    {
        var client = _factory.CreateClient();

        var response = await client.PostAsJsonAsync("/api/v1/ExportImport/export", new
        {
            type = "daily",
            organizationSlug = TestHelpers.SeedOrgSlug
        });
        response.StatusCode.Should().Be(HttpStatusCode.Unauthorized);
    }

    [Fact]
    public async Task ExportDailyReport_RangeExtendingIntoFuture_ExcludesFutureDays()
    {
        var client = _factory.CreateClient();
        await TestHelpers.AuthenticateAsync(client, TestHelpers.SeedOwnerEmail, TestHelpers.SeedPassword);

        var today = DateOnly.FromDateTime(DateTime.UtcNow);
        var rangeFrom = today.AddDays(-3);
        var rangeTo = today.AddDays(10); // extends well past today

        var response = await client.PostAsJsonAsync("/api/v1/ExportImport/export", new
        {
            type = "daily",
            organizationSlug = TestHelpers.SeedOrgSlug,
            from = $"{rangeFrom:yyyy-MM-dd}T00:00:00Z",
            to = $"{rangeTo:yyyy-MM-dd}T00:00:00Z"
        });
        response.EnsureSuccessStatusCode();

        var csv = await response.Content.ReadAsStringAsync();
        var lines = csv.Split('\n', StringSplitOptions.RemoveEmptyEntries);

        // Only rows from rangeFrom through today (inclusive) should be present —
        // none of the future days up to rangeTo.
        var expectedRows = today.DayNumber - rangeFrom.DayNumber + 1;
        lines.Length.Should().Be(1 + expectedRows); // header + expected rows

        csv.Should().Contain(today.ToString("yyyy-MM-dd"));
        var firstFutureDay = today.AddDays(1);
        csv.Should().NotContain(firstFutureDay.ToString("yyyy-MM-dd"));
        csv.Should().NotContain(rangeTo.ToString("yyyy-MM-dd"));
    }

    [Fact]
    public async Task ExportDailyReport_RangeEntirelyInFuture_ReturnsOnlyHeader()
    {
        var client = _factory.CreateClient();
        await TestHelpers.AuthenticateAsync(client, TestHelpers.SeedOwnerEmail, TestHelpers.SeedPassword);

        var today = DateOnly.FromDateTime(DateTime.UtcNow);
        var rangeFrom = today.AddDays(5);
        var rangeTo = today.AddDays(15);

        var response = await client.PostAsJsonAsync("/api/v1/ExportImport/export", new
        {
            type = "daily",
            organizationSlug = TestHelpers.SeedOrgSlug,
            from = $"{rangeFrom:yyyy-MM-dd}T00:00:00Z",
            to = $"{rangeTo:yyyy-MM-dd}T00:00:00Z"
        });
        response.EnsureSuccessStatusCode();

        var csv = await response.Content.ReadAsStringAsync();
        var lines = csv.Split('\n', StringSplitOptions.RemoveEmptyEntries);
        lines.Length.Should().Be(1); // header only — nothing to report for a fully future period
    }

    // ── Export for another member (GroupAdminCanExportData / org admin) ─────

    [Fact]
    public async Task ExportEntries_ForOtherMember_AsOrgAdmin_Succeeds()
    {
        var client = _factory.CreateClient();
        await TestHelpers.AuthenticateAsync(client, TestHelpers.SeedOwnerEmail, TestHelpers.SeedPassword);

        var detail = await client.GetFromJsonAsync<OrganizationDetailResponseDto>(
            $"/api/v1/Organizations/{TestHelpers.SeedOrgSlug}", TestHelpers.JsonOptions);
        var tom = detail!.Members.First(m => m.Email == TestHelpers.SeedMemberEmail);

        var response = await client.PostAsJsonAsync("/api/v1/ExportImport/export", new
        {
            type = "entries",
            organizationSlug = TestHelpers.SeedOrgSlug,
            targetUserId = tom.Id
        });
        response.EnsureSuccessStatusCode();
        response.Content.Headers.ContentType!.MediaType.Should().Be("text/csv");
    }

    [Fact]
    public async Task ExportEntries_ForOtherMember_WithoutOrgSlug_ReturnsBadRequest()
    {
        var client = _factory.CreateClient();
        await TestHelpers.AuthenticateAsync(client, TestHelpers.SeedOwnerEmail, TestHelpers.SeedPassword);

        var detail = await client.GetFromJsonAsync<OrganizationDetailResponseDto>(
            $"/api/v1/Organizations/{TestHelpers.SeedOrgSlug}", TestHelpers.JsonOptions);
        var tom = detail!.Members.First(m => m.Email == TestHelpers.SeedMemberEmail);

        var response = await client.PostAsJsonAsync("/api/v1/ExportImport/export", new
        {
            type = "entries",
            targetUserId = tom.Id
            // organizationSlug intentionally omitted — permission can't be resolved without an org
        });
        response.StatusCode.Should().Be(HttpStatusCode.BadRequest);
    }

    [Fact]
    public async Task ExportEntries_ForOtherMember_AsRegularMember_ReturnsForbidden()
    {
        var ownerClient = _factory.CreateClient();
        await TestHelpers.AuthenticateAsync(ownerClient, TestHelpers.SeedOwnerEmail, TestHelpers.SeedPassword);
        var detail = await ownerClient.GetFromJsonAsync<OrganizationDetailResponseDto>(
            $"/api/v1/Organizations/{TestHelpers.SeedOrgSlug}", TestHelpers.JsonOptions);
        var owner = detail!.Members.First(m => m.Email == TestHelpers.SeedOwnerEmail);

        var memberClient = _factory.CreateClient();
        await TestHelpers.AuthenticateAsync(memberClient, TestHelpers.SeedMemberEmail, TestHelpers.SeedPassword);

        var response = await memberClient.PostAsJsonAsync("/api/v1/ExportImport/export", new
        {
            type = "entries",
            organizationSlug = TestHelpers.SeedOrgSlug,
            targetUserId = owner.Id
        });
        response.StatusCode.Should().Be(HttpStatusCode.Forbidden);
    }

    [Fact]
    public async Task ExportDailyReport_ForOtherMember_AsOrgAdmin_Succeeds()
    {
        var client = _factory.CreateClient();
        await TestHelpers.AuthenticateAsync(client, TestHelpers.SeedOwnerEmail, TestHelpers.SeedPassword);

        var detail = await client.GetFromJsonAsync<OrganizationDetailResponseDto>(
            $"/api/v1/Organizations/{TestHelpers.SeedOrgSlug}", TestHelpers.JsonOptions);
        var tom = detail!.Members.First(m => m.Email == TestHelpers.SeedMemberEmail);

        var response = await client.PostAsJsonAsync("/api/v1/ExportImport/export", new
        {
            type = "daily",
            organizationSlug = TestHelpers.SeedOrgSlug,
            targetUserId = tom.Id
        });
        response.EnsureSuccessStatusCode();
        response.Content.Headers.ContentType!.MediaType.Should().Be("text/csv");
    }

    [Fact]
    public async Task ExportDailyReport_ForOtherMember_AsRegularMember_ReturnsForbidden()
    {
        var ownerClient = _factory.CreateClient();
        await TestHelpers.AuthenticateAsync(ownerClient, TestHelpers.SeedOwnerEmail, TestHelpers.SeedPassword);
        var detail = await ownerClient.GetFromJsonAsync<OrganizationDetailResponseDto>(
            $"/api/v1/Organizations/{TestHelpers.SeedOrgSlug}", TestHelpers.JsonOptions);
        var owner = detail!.Members.First(m => m.Email == TestHelpers.SeedOwnerEmail);

        var memberClient = _factory.CreateClient();
        await TestHelpers.AuthenticateAsync(memberClient, TestHelpers.SeedMemberEmail, TestHelpers.SeedPassword);

        var response = await memberClient.PostAsJsonAsync("/api/v1/ExportImport/export", new
        {
            type = "daily",
            organizationSlug = TestHelpers.SeedOrgSlug,
            targetUserId = owner.Id
        });
        response.StatusCode.Should().Be(HttpStatusCode.Forbidden);
    }

    [Fact]
    public async Task ExportEntries_TargetUserIdEqualsCaller_TreatedAsSelf()
    {
        var client = _factory.CreateClient();
        await TestHelpers.AuthenticateAsync(client, TestHelpers.SeedOwnerEmail, TestHelpers.SeedPassword);
        var ownId = await GetOwnUserIdAsync(client);

        var response = await client.PostAsJsonAsync("/api/v1/ExportImport/export", new
        {
            type = "entries",
            targetUserId = ownId
            // no organizationSlug — allowed because target == caller, no permission check needed
        });
        response.EnsureSuccessStatusCode();
    }

    private static async Task<int> GetOwnUserIdAsync(HttpClient client)
    {
        var me = await client.GetFromJsonAsync<UserInfoDto>("/api/v1/Auth/me", TestHelpers.JsonOptions);
        return me!.Id;
    }
}
