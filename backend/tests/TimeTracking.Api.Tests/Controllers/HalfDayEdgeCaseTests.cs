using System.Net;
using System.Net.Http.Json;
using FluentAssertions;
using TimeTracking.Api.Tests.Infrastructure;

namespace TimeTracking.Api.Tests.Controllers;

/// <summary>
/// Edge case tests for half-day absences combined with half-day holidays.
/// Verifies that daily report CSV exports compute correct effective targets,
/// credits, and statuses when half-day holidays and absences overlap.
/// </summary>
public class HalfDayEdgeCaseTests : IClassFixture<TimeTrackingApiFactory>
{
    private readonly TimeTrackingApiFactory _factory;

    public HalfDayEdgeCaseTests(TimeTrackingApiFactory factory) => _factory = factory;

    /// <summary>
    /// Creates an org with a work schedule (8h/day, Mon-Fri), holidays, and absences,
    /// then exports a daily report and returns the CSV lines.
    /// </summary>
    private async Task<(HttpClient client, string slug, string[] csvLines)> SetupAndExportAsync(
        string slug,
        string userEmail,
        IEnumerable<object> holidays,
        IEnumerable<object> absences,
        string dateFrom,
        string dateTo)
    {
        var (client, _) = await TestHelpers.CreateAuthenticatedUserAsync(_factory, userEmail);
        await TestHelpers.CreateOrganizationAsync(client, slug.Replace("-", " "), slug);

        // Set up 40h/week even distribution (8h Mon-Fri)
        var scheduleResp = await client.PostAsJsonAsync($"/api/v1/organizations/{slug}/work-schedules", new
        {
            validFrom = "2026-01-01",
            weeklyWorkHours = 40.0,
            distributeEvenly = true
        });
        scheduleResp.EnsureSuccessStatusCode();

        // Create holidays
        foreach (var holiday in holidays)
        {
            var resp = await client.PostAsJsonAsync($"/api/v1/organizations/{slug}/holidays", holiday);
            resp.EnsureSuccessStatusCode();
        }

        // Create absences
        foreach (var absence in absences)
        {
            var resp = await client.PostAsJsonAsync($"/api/v1/organizations/{slug}/absences", absence);
            resp.EnsureSuccessStatusCode();
        }

        // Export daily report
        var exportResp = await client.PostAsJsonAsync("/api/v1/ExportImport/export", new
        {
            type = "daily",
            organizationSlug = slug,
            from = $"{dateFrom}T00:00:00Z",
            to = $"{dateTo}T00:00:00Z"
        });
        exportResp.EnsureSuccessStatusCode();

        var csv = await exportResp.Content.ReadAsStringAsync();
        var lines = csv.Split('\n', StringSplitOptions.RemoveEmptyEntries);
        return (client, slug, lines);
    }

    private static string GetColumn(string[] headers, string csvLine, string columnName)
    {
        var cols = headers[0].Split(',');
        var idx = Array.IndexOf(cols, columnName);
        idx.Should().BeGreaterThanOrEqualTo(0, $"Column '{columnName}' should exist in CSV");
        var values = csvLine.Split(',');
        return values[idx];
    }

    // ── Half-day holiday with NO absence ──────────────────────────────

    [Fact]
    public async Task DailyReport_HalfDayHoliday_TargetIsHalved()
    {
        // 2026-07-06 is a Monday
        var (_, _, lines) = await SetupAndExportAsync(
            "hd-holiday-only",
            "hd-holiday-only@test.com",
            holidays: [new { date = "2026-07-06", name = "Half Holiday", isHalfDay = true }],
            absences: [],
            "2026-07-06", "2026-07-06");

        lines.Length.Should().Be(2); // header + 1 day
        var target = GetColumn(lines, lines[1], "Target (h)");
        // 8h day → half-day holiday → 4h effective target
        target.Should().Be("4.00");

        var status = GetColumn(lines, lines[1], "Status");
        status.Should().Be("Half Holiday");
    }

    // ── Full holiday ─────────────────────────────────────────────────

    [Fact]
    public async Task DailyReport_FullHoliday_TargetIsZero()
    {
        var (_, _, lines) = await SetupAndExportAsync(
            "hd-full-holiday",
            "hd-full-holiday@test.com",
            holidays: [new { date = "2026-07-06", name = "Full Holiday", isHalfDay = false }],
            absences: [],
            "2026-07-06", "2026-07-06");

        lines.Length.Should().Be(2);
        var target = GetColumn(lines, lines[1], "Target (h)");
        target.Should().Be("0.00");
    }

    // ── Half-day absence, no holiday ─────────────────────────────────

    [Fact]
    public async Task DailyReport_HalfDayAbsence_TargetIsHalved()
    {
        var (_, _, lines) = await SetupAndExportAsync(
            "hd-absence-only",
            "hd-absence-only@test.com",
            holidays: [],
            absences: [new { date = "2026-07-06", type = 1, isHalfDay = true, note = "Half vacation" }],
            "2026-07-06", "2026-07-06");

        lines.Length.Should().Be(2);
        var target = GetColumn(lines, lines[1], "Target (h)");
        // 8h day → half-day vacation → 4h effective target (remaining work)
        target.Should().Be("4.00");

        var status = GetColumn(lines, lines[1], "Status");
        status.Should().Be("Vacation");
    }

    // ── Full absence ─────────────────────────────────────────────────

    [Fact]
    public async Task DailyReport_FullAbsence_TargetIsZero()
    {
        var (_, _, lines) = await SetupAndExportAsync(
            "hd-full-absence",
            "hd-full-absence@test.com",
            holidays: [],
            absences: [new { date = "2026-07-06", type = 1, isHalfDay = false, note = "Full vacation" }],
            "2026-07-06", "2026-07-06");

        lines.Length.Should().Be(2);
        var target = GetColumn(lines, lines[1], "Target (h)");
        target.Should().Be("0.00");
    }

    // ── CRITICAL: Half-day holiday + half-day vacation ────────────────

    [Fact]
    public async Task DailyReport_HalfHoliday_PlusHalfVacation_TargetIsZero()
    {
        // Half-day holiday covers half the day, half-day vacation covers the other half.
        // Effective target should be 0 (user doesn't need to work at all).
        var (_, _, lines) = await SetupAndExportAsync(
            "hd-hol-vac",
            "hd-hol-vac@test.com",
            holidays: [new { date = "2026-07-06", name = "Half Holiday", isHalfDay = true }],
            absences: [new { date = "2026-07-06", type = 1, isHalfDay = true, note = "Half vacation" }],
            "2026-07-06", "2026-07-06");

        lines.Length.Should().Be(2);
        var target = GetColumn(lines, lines[1], "Target (h)");
        target.Should().Be("0.00");

        // Overtime should be 0 (no work needed, no work done)
        var overtime = GetColumn(lines, lines[1], "Overtime (h)");
        overtime.Should().Be("0.00");

        // Status should reflect the absence (vacation), not holiday
        var status = GetColumn(lines, lines[1], "Status");
        status.Should().Be("Vacation");
    }

    // ── Half-day holiday + full vacation ──────────────────────────────

    [Fact]
    public async Task DailyReport_HalfHoliday_PlusFullVacation_TargetIsZero()
    {
        var (_, _, lines) = await SetupAndExportAsync(
            "hd-hol-fvac",
            "hd-hol-fvac@test.com",
            holidays: [new { date = "2026-07-06", name = "Half Holiday", isHalfDay = true }],
            absences: [new { date = "2026-07-06", type = 1, isHalfDay = false, note = "Full vacation" }],
            "2026-07-06", "2026-07-06");

        lines.Length.Should().Be(2);
        var target = GetColumn(lines, lines[1], "Target (h)");
        target.Should().Be("0.00");
    }

    // ── Half-day holiday + half-day sick ──────────────────────────────

    [Fact]
    public async Task DailyReport_HalfHoliday_PlusHalfSickDay_TargetIsZero()
    {
        var (_, _, lines) = await SetupAndExportAsync(
            "hd-hol-sick",
            "hd-hol-sick@test.com",
            holidays: [new { date = "2026-07-06", name = "Half Holiday", isHalfDay = true }],
            absences: [new { date = "2026-07-06", type = 0, isHalfDay = true, note = "Half sick" }],
            "2026-07-06", "2026-07-06");

        lines.Length.Should().Be(2);
        var target = GetColumn(lines, lines[1], "Target (h)");
        target.Should().Be("0.00");

        var status = GetColumn(lines, lines[1], "Status");
        status.Should().Be("Sick");
    }

    // ── Weekend — no target regardless ──────────────────────────────

    [Fact]
    public async Task DailyReport_Weekend_TargetIsZero()
    {
        // 2026-07-04 is a Saturday
        var (_, _, lines) = await SetupAndExportAsync(
            "hd-weekend",
            "hd-weekend@test.com",
            holidays: [],
            absences: [],
            "2026-07-04", "2026-07-04");

        lines.Length.Should().Be(2);
        var target = GetColumn(lines, lines[1], "Target (h)");
        target.Should().Be("0.00");

        var status = GetColumn(lines, lines[1], "Status");
        status.Should().Be("Weekend");
    }

    // ── Normal workday — full target ────────────────────────────────

    [Fact]
    public async Task DailyReport_NormalWorkday_FullTarget()
    {
        // 2026-07-06 is a Monday
        var (_, _, lines) = await SetupAndExportAsync(
            "hd-normal-day",
            "hd-normal-day@test.com",
            holidays: [],
            absences: [],
            "2026-07-06", "2026-07-06");

        lines.Length.Should().Be(2);
        var target = GetColumn(lines, lines[1], "Target (h)");
        target.Should().Be("8.00");
    }

    // ── Multi-day: verify cumulative overtime with mixed half-days ───

    [Fact]
    public async Task DailyReport_MultiDay_MixedHalfDays_CorrectCumulativeOvertime()
    {
        // Mon 2026-07-06: normal workday, 8h target
        // Tue 2026-07-07: half-day holiday, 4h target
        // Wed 2026-07-08: half-day vacation, 4h target
        // Thu 2026-07-09: half holiday + half vacation, 0h target
        // Fri 2026-07-10: full holiday, 0h target
        var (_, _, lines) = await SetupAndExportAsync(
            "hd-multi-mix",
            "hd-multi-mix@test.com",
            holidays: [
                new { date = "2026-07-07", name = "Tue Half Hol", isHalfDay = true },
                new { date = "2026-07-09", name = "Thu Half Hol", isHalfDay = true },
                new { date = "2026-07-10", name = "Fri Full Hol", isHalfDay = false }
            ],
            absences: [
                new { date = "2026-07-08", type = 1, isHalfDay = true, note = "Wed half vac" },
                new { date = "2026-07-09", type = 1, isHalfDay = true, note = "Thu half vac" }
            ],
            "2026-07-06", "2026-07-10");

        lines.Length.Should().Be(6); // header + 5 days

        // Mon: target 8h, no work → overtime -8
        GetColumn(lines, lines[1], "Target (h)").Should().Be("8.00");

        // Tue: half holiday → target 4h
        GetColumn(lines, lines[2], "Target (h)").Should().Be("4.00");

        // Wed: half vacation → target 4h
        GetColumn(lines, lines[3], "Target (h)").Should().Be("4.00");

        // Thu: half holiday + half vacation → target 0h
        GetColumn(lines, lines[4], "Target (h)").Should().Be("0.00");

        // Fri: full holiday → target 0h
        GetColumn(lines, lines[5], "Target (h)").Should().Be("0.00");
    }

    // ── Holiday and absence info columns still show correctly ─────────

    [Fact]
    public async Task DailyReport_HalfHolidayPlusAbsence_ShowsBothInfoColumns()
    {
        var (_, _, lines) = await SetupAndExportAsync(
            "hd-info-cols",
            "hd-info-cols@test.com",
            holidays: [new { date = "2026-07-06", name = "Test Holiday", isHalfDay = true }],
            absences: [new { date = "2026-07-06", type = 1, isHalfDay = true, note = "Test vacation note" }],
            "2026-07-06", "2026-07-06");

        lines.Length.Should().Be(2);

        // Holiday name column should still show
        GetColumn(lines, lines[1], "Holiday").Should().Be("Test Holiday");

        // Absence type and note should show
        GetColumn(lines, lines[1], "Absence Type").Should().Be("Vacation");
        GetColumn(lines, lines[1], "Absence Note").Should().Be("Test vacation note");
    }
}
