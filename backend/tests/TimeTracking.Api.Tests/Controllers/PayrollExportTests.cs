using System.Net;
using System.Net.Http.Json;
using System.Text;
using System.Text.Json;
using FluentAssertions;
using TimeTracking.Api.Tests.Infrastructure;

namespace TimeTracking.Api.Tests.Controllers;

/// <summary>The payroll CSV: German format, hours like the balance, absences, corrections, access.</summary>
public class PayrollExportTests : IClassFixture<TimeTrackingApiFactory>
{
    private readonly TimeTrackingApiFactory _factory;

    public PayrollExportTests(TimeTrackingApiFactory factory) => _factory = factory;

    [Fact]
    public async Task Payroll_IsGermanCsv_WithHoursAbsencesAndCorrections()
    {
        var (owner, _) = await TestHelpers.CreateAuthenticatedUserAsync(_factory, "payroll-owner@test.com", "Anna", "Admin");
        var org = await TestHelpers.CreateOrganizationAsync(owner, "Payroll Org", "payroll-org");
        (await owner.PutAsJsonAsync($"/api/v1/Organizations/{org.Slug}/settings", new { memberTimeEntryVisibility = true })).EnsureSuccessStatusCode();
        var (member, m) = await TestHelpers.CreateAuthenticatedUserAsync(_factory, "payroll-m@test.com", "=Evil", "Zed");
        (await TestHelpers.AddMemberAsync(_factory, owner, org.Slug, m)).EnsureSuccessStatusCode();

        (await owner.PostAsJsonAsync($"/api/v1/organizations/{org.Slug}/members/{m.User.Id}/work-schedule/change",
            new { validFrom = "2026-01-01", weeklyWorkHours = 40 })).EnsureSuccessStatusCode();
        // Mon 14 Sep 2026: 8 h gross − 30 min pause = 7,5 h; Tue vacation, Wed sick
        (await member.PostAsJsonAsync("/api/v1/TimeTracking/manual", new
        {
            startTime = new DateTime(2026, 9, 14, 6, 0, 0, DateTimeKind.Utc), endTime = new DateTime(2026, 9, 14, 14, 0, 0, DateTimeKind.Utc),
            organizationId = org.Id
        })).StatusCode.Should().Be(HttpStatusCode.Created);
        (await owner.PostAsJsonAsync($"/api/v1/organizations/{org.Slug}/absences/admin", new { userId = m.User.Id, date = "2026-09-15", type = 1 })).EnsureSuccessStatusCode();
        (await owner.PostAsJsonAsync($"/api/v1/organizations/{org.Slug}/absences/admin", new { userId = m.User.Id, date = "2026-09-16", type = 0 })).EnsureSuccessStatusCode();

        // Personnel number: admins set it (trimmed), it is unique per organization and shown to admins only
        var pnUrl = $"/api/v1/organizations/{org.Slug}/members/{m.User.Id}/personnel-number";
        (await owner.PutAsJsonAsync(pnUrl, new { personnelNumber = " P-0042 " })).EnsureSuccessStatusCode();
        var ownerId = (await owner.GetFromJsonAsync<JsonElement>("/api/v1/Auth/me")).GetProperty("id").GetInt32();
        var taken = await owner.PutAsJsonAsync($"/api/v1/organizations/{org.Slug}/members/{ownerId}/personnel-number", new { personnelNumber = "P-0042" });
        taken.StatusCode.Should().Be(HttpStatusCode.Conflict);
        (await taken.Content.ReadAsStringAsync()).Should().Contain("org.personnelNumberTaken");
        (await owner.PutAsJsonAsync($"/api/v1/organizations/{org.Slug}/members/{ownerId}/personnel-number", new { personnelNumber = new string('9', 51) }))
            .StatusCode.Should().Be(HttpStatusCode.BadRequest);
        (await member.PutAsJsonAsync(pnUrl, new { personnelNumber = "P-1" })).StatusCode.Should().Be(HttpStatusCode.Forbidden);
        Member(await owner.GetFromJsonAsync<JsonElement>($"/api/v1/Organizations/{org.Slug}"), m.User.Id).GetProperty("personnelNumber").GetString().Should().Be("P-0042");
        Member(await member.GetFromJsonAsync<JsonElement>($"/api/v1/Organizations/{org.Slug}"), m.User.Id).GetProperty("personnelNumber").ValueKind.Should().Be(JsonValueKind.Null);
        (await member.GetStringAsync("/api/v1/Auth/me/export")).Should().Contain("\"personnelNumber\": \"P-0042\"");

        var response = await owner.PostAsJsonAsync("/api/v1/ExportImport/payroll", new { orgSlug = org.Slug, from = "2026-09-14", to = "2026-09-18" });
        response.EnsureSuccessStatusCode();
        var bytes = await response.Content.ReadAsByteArrayAsync();
        bytes.Take(3).Should().Equal(0xEF, 0xBB, 0xBF);
        var lines = Encoding.UTF8.GetString(bytes[3..]).Split("\r\n", StringSplitOptions.RemoveEmptyEntries)
            .SelectMany(l => l.Split('\n', StringSplitOptions.RemoveEmptyEntries)).ToList();

        lines[0].Should().Be("Personalnummer;Name;E-Mail;Soll (h);Ist (h);Überstunden Zeitraum (h);Überstundensaldo (h);Urlaubstage;Krankheitstage;Sonstige Abwesenheiten;Korrektur nötig (Anzahl)");
        lines.Should().HaveCount(3);
        lines[1].Should().Be(";Anna Admin;payroll-owner@test.com;0,00;0,00;0,00;0,00;0,00;0,00;0,00;0");
        // Target Mon/Thu/Fri 8 h each (Tue/Wed credited by the absences) = 24 h; formula-leading name defused
        lines[2].Should().Be("P-0042;'=Evil Zed;payroll-m@test.com;24,00;7,50;-16,50;-16,50;1,00;1,00;0,00;0");

        var english = await owner.PostAsJsonAsync("/api/v1/ExportImport/payroll", new { orgSlug = org.Slug, from = "2026-09-14", to = "2026-09-18", lang = "en" });
        Encoding.UTF8.GetString(await english.Content.ReadAsByteArrayAsync()).Should().Contain("Personnel number;Name;Email;Target (h)");

        (await member.PostAsJsonAsync("/api/v1/ExportImport/payroll", new { orgSlug = org.Slug, from = "2026-09-14", to = "2026-09-18" }))
            .StatusCode.Should().Be(HttpStatusCode.Forbidden);
        (await owner.PostAsJsonAsync("/api/v1/ExportImport/payroll", new { orgSlug = org.Slug, from = "2024-01-01", to = "2026-09-18" }))
            .StatusCode.Should().Be(HttpStatusCode.BadRequest);
        (await owner.PutAsJsonAsync($"/api/v1/Organizations/{org.Slug}/settings", new { memberTimeEntryVisibility = false })).EnsureSuccessStatusCode();
        (await owner.PostAsJsonAsync("/api/v1/ExportImport/payroll", new { orgSlug = org.Slug, from = "2026-09-14", to = "2026-09-18" }))
            .StatusCode.Should().Be(HttpStatusCode.Forbidden);

        // Clearing it empties the column again
        (await owner.PutAsJsonAsync(pnUrl, new { personnelNumber = "" })).EnsureSuccessStatusCode();
        Member(await owner.GetFromJsonAsync<JsonElement>($"/api/v1/Organizations/{org.Slug}"), m.User.Id).GetProperty("personnelNumber").ValueKind.Should().Be(JsonValueKind.Null);
    }

    private static JsonElement Member(JsonElement org, int id) =>
        org.GetProperty("members").EnumerateArray().Single(x => x.GetProperty("id").GetInt32() == id);
}
