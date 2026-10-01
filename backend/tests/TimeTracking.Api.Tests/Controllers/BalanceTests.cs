using System.Globalization;
using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using FluentAssertions;
using TimeTracking.Api.Services;
using TimeTracking.Api.Tests.Infrastructure;

namespace TimeTracking.Api.Tests.Controllers;

/// <summary>The balance endpoint uses the report rules, so the dashboard and the reports agree.</summary>
public class BalanceTests : IClassFixture<TimeTrackingApiFactory>
{
    private readonly TimeTrackingApiFactory _factory;

    public BalanceTests(TimeTrackingApiFactory factory) => _factory = factory;

    [Fact]
    public async Task Balance_MatchesDailyReport_AndIncludesInitialOvertime()
    {
        var (owner, login) = await TestHelpers.CreateAuthenticatedUserAsync(_factory, "balance@bal.test");
        var org = await TestHelpers.CreateOrganizationAsync(owner, "Balance Org", "balance-org");
        (await owner.PutAsJsonAsync($"/api/v1/Organizations/{org.Slug}/settings", new { csvImportMode = 2 })).EnsureSuccessStatusCode();
        (await owner.PutAsJsonAsync($"/api/v1/Organizations/{org.Slug}/members/{login.User.Id}/initial-overtime",
            new { initialOvertimeHours = 5.5 })).EnsureSuccessStatusCode();

        var today = OrgClock.For("Europe/Berlin").Today();
        var monthStart = new DateOnly(today.Year, today.Month, 1);
        (await owner.PostAsJsonAsync($"/api/v1/organizations/{org.Slug}/work-schedules",
            new { validFrom = monthStart.AddMonths(-1).ToString("yyyy-MM-dd"), weeklyWorkHours = 40, distributeEvenly = true })).EnsureSuccessStatusCode();
        var rows = new List<object> { new { date = today.ToString("yyyy-MM-dd"), startTime = "08:00", endTime = "12:00", pauseMinutes = 0, description = "today" } };
        if (monthStart < today)   // on the 1st, "today" already is the month start
            rows.Add(new { date = monthStart.ToString("yyyy-MM-dd"), startTime = "08:00", endTime = "18:00", pauseMinutes = 30, description = "long day" });
        (await owner.PostAsJsonAsync($"/api/v1/ExportImport/import/confirm?orgSlug={org.Slug}", rows)).EnsureSuccessStatusCode();

        var balance = await owner.GetFromJsonAsync<JsonElement>($"/api/v1/organizations/{org.Slug}/members/{login.User.Id}/balance");
        balance.GetProperty("today").GetProperty("workedHours").GetDouble().Should().Be(4);
        balance.GetProperty("initialOvertimeHours").GetDouble().Should().Be(5.5);

        // Same figures as the daily report for the month so far
        var csv = await (await owner.PostAsJsonAsync("/api/v1/ExportImport/export", new
        {
            type = "daily", organizationSlug = org.Slug,
            from = monthStart.ToString("yyyy-MM-dd"), to = today.ToString("yyyy-MM-dd")
        })).Content.ReadAsStringAsync();
        var lines = csv.Split('\n', StringSplitOptions.RemoveEmptyEntries).Skip(1).Select(l => l.Split(',')).ToList();
        var reportOvertime = lines.Sum(c => double.Parse(c[6], CultureInfo.InvariantCulture));
        var reportCumulative = double.Parse(lines[^1][7], CultureInfo.InvariantCulture);

        balance.GetProperty("month").GetProperty("overtimeHours").GetDouble().Should().BeApproximately(reportOvertime, 0.01);
        balance.GetProperty("cumulativeOvertimeHours").GetDouble().Should().BeApproximately(reportCumulative, 0.01);

        // Week/month breakdowns add up to the period figures
        var weekDays = balance.GetProperty("weekDays").EnumerateArray().ToList();
        weekDays.Should().HaveCount(7);
        DateOnly.Parse(weekDays[0].GetProperty("date").GetString()!).DayOfWeek.Should().Be(DayOfWeek.Monday);
        weekDays.Sum(d => d.GetProperty("overtimeHours").GetDouble())
            .Should().BeApproximately(balance.GetProperty("week").GetProperty("overtimeHours").GetDouble(), 0.01);
        var todayRow = weekDays.Single(d => d.GetProperty("date").GetString() == today.ToString("yyyy-MM-dd"));
        todayRow.GetProperty("workedHours").GetDouble().Should().Be(4);
        todayRow.GetProperty("isCounted").GetBoolean().Should().BeTrue();
        weekDays.Where(d => DateOnly.Parse(d.GetProperty("date").GetString()!) > today)
            .Should().OnlyContain(d => !d.GetProperty("isCounted").GetBoolean() && d.GetProperty("overtimeHours").GetDouble() == 0);

        var monthWeeks = balance.GetProperty("monthWeeks").EnumerateArray().ToList();
        monthWeeks.First().GetProperty("from").GetString().Should().Be(monthStart.ToString("yyyy-MM-dd"));
        monthWeeks.Last().GetProperty("to").GetString().Should().Be(monthStart.AddMonths(1).AddDays(-1).ToString("yyyy-MM-dd"));
        monthWeeks.Sum(w => w.GetProperty("overtimeHours").GetDouble())
            .Should().BeApproximately(balance.GetProperty("month").GetProperty("overtimeHours").GetDouble(), 0.01);
        monthWeeks.Sum(w => w.GetProperty("workedHours").GetDouble())
            .Should().BeApproximately(balance.GetProperty("month").GetProperty("workedHours").GetDouble(), 0.01);
    }

    [Fact]
    public async Task DailyCsv_CumulativeOvertime_IsTheTrueRunningTotal_NotRestartedAtTheRangeStart()
    {
        var (owner, login) = await TestHelpers.CreateAuthenticatedUserAsync(_factory, "balance-csv@bal.test");
        var org = await TestHelpers.CreateOrganizationAsync(owner, "Balance Org CSV", "balance-org-csv");
        (await owner.PutAsJsonAsync($"/api/v1/Organizations/{org.Slug}/settings", new { csvImportMode = 2 })).EnsureSuccessStatusCode();
        (await owner.PutAsJsonAsync($"/api/v1/Organizations/{org.Slug}/members/{login.User.Id}/initial-overtime",
            new { initialOvertimeHours = 2 })).EnsureSuccessStatusCode();
        var today = OrgClock.For("Europe/Berlin").Today();
        (await owner.PostAsJsonAsync($"/api/v1/organizations/{org.Slug}/work-schedules",
            new { validFrom = today.AddMonths(-3).ToString("yyyy-MM-dd"), weeklyWorkHours = 40, distributeEvenly = true })).EnsureSuccessStatusCode();
        // Tracking started six weeks ago with a long day; the export only asks for today
        (await owner.PostAsJsonAsync($"/api/v1/ExportImport/import/confirm?orgSlug={org.Slug}", new[]
        {
            new { date = today.AddDays(-42).ToString("yyyy-MM-dd"), startTime = "06:00", endTime = "20:00", pauseMinutes = 60, description = "start" },
            new { date = today.ToString("yyyy-MM-dd"), startTime = "08:00", endTime = "09:00", pauseMinutes = 0, description = "today" }
        })).EnsureSuccessStatusCode();

        var balance = await owner.GetFromJsonAsync<JsonElement>($"/api/v1/organizations/{org.Slug}/members/{login.User.Id}/balance");
        var csv = await (await owner.PostAsJsonAsync("/api/v1/ExportImport/export", new
        {
            type = "daily", organizationSlug = org.Slug, columns = new[] { "Date", "CumulativeOvertime" },
            from = today.ToString("yyyy-MM-dd"), to = today.ToString("yyyy-MM-dd")
        })).Content.ReadAsStringAsync();
        var lines = csv.Split('\n', StringSplitOptions.RemoveEmptyEntries);
        lines.Should().HaveCount(2);
        var cumulative = double.Parse(lines[1].Split(',')[1], CultureInfo.InvariantCulture);

        cumulative.Should().BeApproximately(balance.GetProperty("cumulativeOvertimeHours").GetDouble(), 0.01);
        // A restarted total would be the initial 2 h plus today's −7 h
        cumulative.Should().NotBeApproximately(2 - 7, 0.01);
    }

    [Fact]
    public async Task Balance_OfAnotherMember_IsForbiddenForPlainMembers()
    {
        var (owner, ownerLogin) = await TestHelpers.CreateAuthenticatedUserAsync(_factory, "balance-owner@bal.test");
        var org = await TestHelpers.CreateOrganizationAsync(owner, "Balance Org 2", "balance-org-2");
        var (member, m) = await TestHelpers.CreateAuthenticatedUserAsync(_factory, "balance-m@bal.test");
        (await TestHelpers.AddMemberAsync(_factory, owner, org.Slug, m)).EnsureSuccessStatusCode();

        (await member.GetAsync($"/api/v1/organizations/{org.Slug}/members/{ownerLogin.User.Id}/balance"))
            .StatusCode.Should().Be(HttpStatusCode.Forbidden);
        (await member.GetAsync($"/api/v1/organizations/{org.Slug}/members/{m.User.Id}/balance")).EnsureSuccessStatusCode();
        // The org decides whether admins may see members' hours
        (await owner.GetAsync($"/api/v1/organizations/{org.Slug}/members/{m.User.Id}/balance")).StatusCode.Should().Be(HttpStatusCode.Forbidden);
        (await owner.PutAsJsonAsync($"/api/v1/Organizations/{org.Slug}/settings", new { memberTimeEntryVisibility = true })).EnsureSuccessStatusCode();
        (await owner.GetAsync($"/api/v1/organizations/{org.Slug}/members/{m.User.Id}/balance")).EnsureSuccessStatusCode();
    }

    [Fact]
    public async Task Balance_StartsAtTheFirstTrackedEntry_NotAtTheStartOfTheWeek()
    {
        var (owner, login) = await TestHelpers.CreateAuthenticatedUserAsync(_factory, "balance-start@bal.test");
        var org = await TestHelpers.CreateOrganizationAsync(owner, "Balance Org 3", "balance-org-3");
        (await owner.PutAsJsonAsync($"/api/v1/Organizations/{org.Slug}/settings", new { csvImportMode = 2 })).EnsureSuccessStatusCode();
        var today = OrgClock.For("Europe/Berlin").Today();
        (await owner.PostAsJsonAsync($"/api/v1/organizations/{org.Slug}/work-schedules",
            new { validFrom = today.AddMonths(-2).ToString("yyyy-MM-dd"), weeklyWorkHours = 40, distributeEvenly = true })).EnsureSuccessStatusCode();
        (await owner.PostAsJsonAsync($"/api/v1/ExportImport/import/confirm?orgSlug={org.Slug}",
            new[] { new { date = today.ToString("yyyy-MM-dd"), startTime = "08:00", endTime = "10:00", pauseMinutes = 0, description = "first ever" } })).EnsureSuccessStatusCode();

        var balance = await owner.GetFromJsonAsync<JsonElement>($"/api/v1/organizations/{org.Slug}/members/{login.User.Id}/balance");

        // Nothing before today was tracked, so earlier weekdays of this week are not missing work
        balance.GetProperty("cumulativeOvertimeHours").GetDouble()
            .Should().BeApproximately(balance.GetProperty("today").GetProperty("overtimeHours").GetDouble(), 0.01);
        balance.GetProperty("week").GetProperty("overtimeHours").GetDouble()
            .Should().BeApproximately(balance.GetProperty("today").GetProperty("overtimeHours").GetDouble(), 0.01);
    }
}
