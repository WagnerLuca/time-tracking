using System.Globalization;
using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using TimeTracking.Api.Data;
using TimeTracking.Api.Models;
using TimeTracking.Api.Services;
using TimeTracking.Api.Tests.Infrastructure;

namespace TimeTracking.Api.Tests.Controllers;

/// <summary>
/// Frozen figures of closed months (issue #81): after closing, changing a holiday (dated or recurring), a custom
/// type's credit mode, the time zone, a schedule (admin with a reason re-freezes) or the initial overtime changes no figure
/// of the closed month in any consumer; open months continue from the frozen balance; reopening makes the month live
/// again, closing again freezes anew; months closed before #81 are frozen at startup.
/// Every "unchanged" test ends by reopening and showing that the change does move the live figures (so it is not vacuous).
/// </summary>
public class FrozenFiguresTests : IClassFixture<TimeTrackingApiFactory>
{
    private const int Allowed = 2;
    private const int FullCredit = 2, NoCredit = 0;
    private readonly TimeTrackingApiFactory _factory;

    public FrozenFiguresTests(TimeTrackingApiFactory factory) => _factory = factory;

    private static readonly DateOnly ThisMonth = ClosedPeriods.MonthOf(OrgClock.For((string?)null).Today());
    private static readonly DateOnly LastMonth = ThisMonth.AddMonths(-1);
    private static readonly DateOnly TwoMonthsAgo = ThisMonth.AddMonths(-2);

    private static List<DateOnly> Weekdays(DateOnly month) =>
        Enumerable.Range(0, DateTime.DaysInMonth(month.Year, month.Month)).Select(month.AddDays)
            .Where(d => d.DayOfWeek is not (DayOfWeek.Saturday or DayOfWeek.Sunday)).ToList();

    /// <summary>The days of the month the setup fills (all weekdays).</summary>
    private sealed record Days(DateOnly Worked, DateOnly Missing, DateOnly Vacation, DateOnly Custom, DateOnly Long, DateOnly Late)
    {
        public static Days Of(DateOnly month)
        {
            var wd = Weekdays(month);
            // Late: 23:15 UTC is the next day in Berlin, the same day in New York — Mon..Thu so both are working days
            var late = wd.Skip(11).First(d => d.DayOfWeek <= DayOfWeek.Thursday && d.AddDays(1).Month == month.Month);
            return new Days(wd[1], wd[3], wd[5], wd[7], wd[9], late);
        }
    }

    private sealed record Org(HttpClient Owner, HttpClient Member, string Slug, int OrgId, int MemberId, int ScheduleId, int TypeId,
        int EntryId, DateOnly Month, Days D);

    /// <summary>All figures of a month as every consumer shows them.</summary>
    private sealed record Figures(string Summary, string Payroll, string Daily, string Report, string Vacation, string Compliance, string Frozen);

    private static string Key(DateOnly d) => d.ToString("yyyy-MM-dd");
    private static DateTime Utc(DateOnly d, int hour, int minute = 0) => d.ToDateTime(new TimeOnly(hour, minute), DateTimeKind.Utc);

    private static async Task<string> OkAsync(HttpResponseMessage response)
    {
        var body = await response.Content.ReadAsStringAsync();
        response.StatusCode.Should().Be(HttpStatusCode.OK, body);
        return body;
    }

    private static HttpRequestMessage WithReason(HttpMethod method, string url, object? body, string reason)
    {
        var message = new HttpRequestMessage(method, url) { Content = body == null ? null : JsonContent.Create(body) };
        message.Headers.Add("X-Change-Reason", Uri.EscapeDataString(reason));
        return message;
    }

    /// <summary>
    /// A fresh org; the member has 8 h Mon–Fri from the month before <paramref name="month"/> and in that month: 4 h on Worked,
    /// nothing on Missing, vacation on Vacation, a custom full-credit absence on Custom, 12 h on Long (compliance finding)
    /// and a short entry at 23:15 UTC on Late.
    /// </summary>
    private async Task<Org> SetupAsync(string prefix, DateOnly? forMonth = null)
    {
        var month = forMonth ?? LastMonth;
        var d = Days.Of(month);
        var (owner, member, slug, orgId, memberId) = await TestHelpers.OrgWithMemberAsync(_factory, prefix,
            new { editPastEntriesMode = Allowed, workScheduleChangeMode = Allowed, memberTimeEntryVisibility = true }, "frozen.test");

        var schedule = await owner.PostAsJsonAsync($"/api/v1/organizations/{slug}/members/{memberId}/work-schedules",
            new { validFrom = Key(month.AddMonths(-1)), weeklyWorkHours = 40.0, distributeEvenly = true });
        schedule.StatusCode.Should().BeOneOf([HttpStatusCode.OK, HttpStatusCode.Created], await schedule.Content.ReadAsStringAsync());
        var scheduleId = (await schedule.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("id").GetInt32();

        var type = await owner.PostAsJsonAsync($"/api/v1/organizations/{slug}/absence-types", new { name = "Training", creditMode = FullCredit });
        type.EnsureSuccessStatusCode();
        var typeId = (await type.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("id").GetInt32();

        var entryId = await ManualAsync(member, orgId, Utc(d.Worked, 6), Utc(d.Worked, 10));
        await ManualAsync(member, orgId, Utc(d.Long, 4), Utc(d.Long, 16));
        await ManualAsync(member, orgId, Utc(d.Late, 23, 15), Utc(d.Late, 23, 45));
        (await owner.PostAsJsonAsync($"/api/v1/organizations/{slug}/absences/admin",
            new { userId = memberId, date = Key(d.Vacation), type = 1 })).EnsureSuccessStatusCode();
        (await owner.PostAsJsonAsync($"/api/v1/organizations/{slug}/absences/admin",
            new { userId = memberId, date = Key(d.Custom), type = 2, customTypeId = typeId })).EnsureSuccessStatusCode();

        return new Org(owner, member, slug, orgId, memberId, scheduleId, typeId, entryId, month, d);
    }

    private static async Task<int> ManualAsync(HttpClient client, int orgId, DateTime start, DateTime end)
    {
        var response = await client.PostAsJsonAsync("/api/v1/TimeTracking/manual", new { startTime = start, endTime = end, organizationId = orgId });
        response.StatusCode.Should().Be(HttpStatusCode.Created, await response.Content.ReadAsStringAsync());
        return (await response.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("entry").GetProperty("id").GetInt32();
    }

    private static async Task CloseAsync(Org o, DateOnly month) =>
        await OkAsync(await o.Owner.PostAsJsonAsync($"/api/v1/organizations/{o.Slug}/closed-periods", new { from = month, to = month }));

    private static async Task ReopenAsync(Org o, DateOnly month) =>
        await OkAsync(await o.Owner.PostAsJsonAsync($"/api/v1/organizations/{o.Slug}/closed-periods/reopen", new { month, reason = "Nachtrag" }));

    private static async Task<double> BalanceAsync(Org o) =>
        (await o.Owner.GetFromJsonAsync<JsonElement>($"/api/v1/organizations/{o.Slug}/members/{o.MemberId}/balance"))
        .GetProperty("cumulativeOvertimeHours").GetDouble();

    private static async Task<string> DailyAsync(Org o, DateOnly month, string[]? columns = null) =>
        await OkAsync(await o.Owner.PostAsJsonAsync("/api/v1/ExportImport/export", new
        {
            lang = "en", type = "daily", organizationSlug = o.Slug, targetUserId = o.MemberId,
            from = Key(month), to = Key(month.AddMonths(1).AddDays(-1)), columns
        }));

    private static string Line(string csv, DateOnly day) => csv.Split('\n').Single(l => l.StartsWith(Key(day)));

    private static async Task<JsonElement> SummaryRowAsync(Org o, DateOnly month) =>
        (await o.Owner.GetFromJsonAsync<JsonElement>($"/api/v1/organizations/{o.Slug}/accounting/summary?year={month.Year}&month={month.Month}"))
        .GetProperty("members").EnumerateArray().Single(m => m.GetProperty("userId").GetInt32() == o.MemberId);

    private static async Task<List<JsonElement>> FrozenDaysAsync(HttpClient client, Org o, DateOnly from, DateOnly to) =>
        (await client.GetFromJsonAsync<List<JsonElement>>(
            $"/api/v1/organizations/{o.Slug}/members/{o.MemberId}/frozen-days?from={Key(from)}&to={Key(to)}"))!;

    /// <summary>The PDF's rows (the PDF itself is compressed): what <c>PdfReportService</c> renders for the month.</summary>
    private async Task<string> ReportRowsAsync(Org o, DateOnly month)
    {
        using var scope = _factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<TimeTrackingDbContext>();
        var initial = await db.UserOrganizations.IgnoreQueryFilters().AsNoTracking()
            .Where(uo => uo.OrganizationId == o.OrgId && uo.UserId == o.MemberId).Select(uo => uo.InitialOvertimeHours).SingleAsync();
        var (rows, _) = await DailyReportAggregator.BuildWithRunningTotalAsync(db, o.MemberId, o.OrgId, month, month.AddMonths(1).AddDays(-1), initial);
        return string.Join("\n", rows.Select(r => string.Join("|", Key(r.Date), r.TargetHours.ToString("F4", CultureInfo.InvariantCulture),
            r.NetWorkedHours.ToString("F4", CultureInfo.InvariantCulture), r.Overtime.ToString("F4", CultureInfo.InvariantCulture),
            r.CumulativeOvertime.ToString("F4", CultureInfo.InvariantCulture), r.Status, r.HolidayName, r.AbsenceType, r.EntrySummary)));
    }

    private async Task<Figures> FiguresAsync(Org o, DateOnly month)
    {
        var last = month.AddMonths(1).AddDays(-1);
        var summary = (await SummaryRowAsync(o, month)).GetRawText();
        var payroll = await OkAsync(await o.Owner.PostAsJsonAsync("/api/v1/ExportImport/payroll", new { orgSlug = o.Slug, from = month, to = last }));
        var pdf = await o.Owner.GetAsync($"/api/v1/organizations/{o.Slug}/members/{o.MemberId}/reports/monthly?year={month.Year}&month={month.Month}");
        pdf.StatusCode.Should().Be(HttpStatusCode.OK);
        pdf.Content.Headers.ContentType!.MediaType.Should().Be("application/pdf");
        var vacation = (await o.Owner.GetFromJsonAsync<JsonElement>($"/api/v1/organizations/{o.Slug}/accounting/vacation?year={month.Year}"))
            .GetProperty("members").EnumerateArray().Single(m => m.GetProperty("userId").GetInt32() == o.MemberId).GetRawText();
        var compliance = await OkAsync(await o.Owner.GetAsync(
            $"/api/v1/organizations/{o.Slug}/members/{o.MemberId}/compliance?from={Key(month)}&to={Key(last)}"));
        var frozen = await OkAsync(await o.Owner.GetAsync(
            $"/api/v1/organizations/{o.Slug}/members/{o.MemberId}/frozen-days?from={Key(month)}&to={Key(last)}"));
        return new Figures(summary, payroll, await DailyAsync(o, month), await ReportRowsAsync(o, month), vacation, compliance, frozen);
    }

    /// <summary>Closes the setup's month and checks the snapshot is meaningful (frozen rows, a compliance finding).</summary>
    private async Task<(Figures Figures, double Balance)> CloseAndSnapshotAsync(Org o)
    {
        await CloseAsync(o, o.Month);
        var figures = await FiguresAsync(o, o.Month);
        figures.Compliance.Should().Contain(Key(o.D.Long), "the 12 h day breaks the daily maximum");
        figures.Frozen.Should().Contain(Key(o.D.Missing));
        Line(figures.Daily, o.D.Missing).Should().Contain("Missing");
        return (figures, await BalanceAsync(o));
    }

    // ── Each input changed after closing ──

    [Fact]
    public async Task DatedHoliday_AddedToAClosedMonth_ChangesNoFigure_UntilReopened()
    {
        var o = await SetupAsync("ff-hol");
        var (before, balance) = await CloseAndSnapshotAsync(o);

        foreach (var day in new[] { o.D.Missing, o.D.Vacation })
            (await o.Owner.PostAsJsonAsync($"/api/v1/organizations/{o.Slug}/holidays", new { date = Key(day), name = "Nachfeiertag" }))
                .EnsureSuccessStatusCode();

        (await FiguresAsync(o, o.Month)).Should().BeEquivalentTo(before);
        (await BalanceAsync(o)).Should().BeApproximately(balance, 0.001, "the open month continues from the frozen balance");

        // Live again: the holiday credits the missing day (+8 h) and frees the vacation day
        await ReopenAsync(o, o.Month);
        var daily = await DailyAsync(o, o.Month);
        Line(daily, o.D.Missing).Should().Contain("Nachfeiertag");
        (await BalanceAsync(o)).Should().BeApproximately(balance + 8, 0.001);
        (await FiguresAsync(o, o.Month)).Vacation.Should().NotBe(before.Vacation);
    }

    [Fact]
    public async Task RecurringHoliday_FromAnEarlierYear_ChangesNoFigureOfTheClosedMonth()
    {
        var o = await SetupAsync("ff-rec");
        var (before, balance) = await CloseAndSnapshotAsync(o);

        (await o.Owner.PostAsJsonAsync($"/api/v1/organizations/{o.Slug}/holidays",
            new { date = Key(o.D.Missing.AddYears(-3)), name = "Jedes Jahr", isRecurring = true })).EnsureSuccessStatusCode();

        (await FiguresAsync(o, o.Month)).Should().BeEquivalentTo(before);
        (await BalanceAsync(o)).Should().BeApproximately(balance, 0.001);

        await ReopenAsync(o, o.Month);
        Line(await DailyAsync(o, o.Month), o.D.Missing).Should().Contain("Jedes Jahr");
        (await BalanceAsync(o)).Should().BeApproximately(balance + 8, 0.001);
    }

    [Fact]
    public async Task CustomTypeCreditMode_ChangedAfterClosing_ChangesNoFigureOfTheClosedMonth()
    {
        var o = await SetupAsync("ff-credit");
        var (before, balance) = await CloseAndSnapshotAsync(o);
        Line(before.Daily, o.D.Custom).Should().Contain(",0.00,").And.Contain("Training");

        (await o.Owner.PutAsJsonAsync($"/api/v1/organizations/{o.Slug}/absence-types/{o.TypeId}",
            new { name = "Training", creditMode = NoCredit })).EnsureSuccessStatusCode();

        (await FiguresAsync(o, o.Month)).Should().BeEquivalentTo(before);
        (await BalanceAsync(o)).Should().BeApproximately(balance, 0.001);

        // Live: no credit any more, the day's 8 h target is missing
        await ReopenAsync(o, o.Month);
        Line(await DailyAsync(o, o.Month), o.D.Custom).Should().NotBe(Line(before.Daily, o.D.Custom));
        (await BalanceAsync(o)).Should().BeApproximately(balance - 8, 0.001);
    }

    [Fact]
    public async Task TimeZone_ChangedAfterClosing_ChangesNoFigureOfTheClosedMonth()
    {
        var o = await SetupAsync("ff-tz");
        var (before, _) = await CloseAndSnapshotAsync(o);
        // In Berlin the late entry belongs to the next day
        Line(before.Daily, o.D.Late.AddDays(1)).Should().Contain("Worked");

        (await o.Owner.PutAsJsonAsync($"/api/v1/Organizations/{o.Slug}/settings", new { timeZone = "America/New_York" })).EnsureSuccessStatusCode();

        (await FiguresAsync(o, o.Month)).Should().BeEquivalentTo(before);

        // Live in New York: the entry moves to its UTC day
        await ReopenAsync(o, o.Month);
        var daily = await DailyAsync(o, o.Month);
        Line(daily, o.D.Late).Should().NotBe(Line(before.Daily, o.D.Late));
        Line(daily, o.D.Late.AddDays(1)).Should().Contain("Missing");
    }

    [Fact]
    public async Task Schedule_AdminWithoutReasonIsRefused_WithAReasonTheMonthIsReFrozen_AndAudited()
    {
        var o = await SetupAsync("ff-sched");
        var (before, balance) = await CloseAndSnapshotAsync(o);
        var url = $"/api/v1/organizations/{o.Slug}/members/{o.MemberId}/work-schedules/{o.ScheduleId}";
        var body = new { weeklyWorkHours = 20.0, distributeEvenly = true };

        var refused = await o.Owner.PutAsJsonAsync(url, body);
        refused.StatusCode.Should().Be(HttpStatusCode.Conflict);
        (await refused.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("code").GetString().Should().Be("period.reasonRequired");
        (await FiguresAsync(o, o.Month)).Should().BeEquivalentTo(before);
        (await BalanceAsync(o)).Should().BeApproximately(balance, 0.001);

        await OkAsync(await o.Owner.SendAsync(WithReason(HttpMethod.Put, url, body, "Vertrag rückwirkend")));

        // The month's frozen figures follow the change (4 h per day instead of 8)
        var after = await FiguresAsync(o, o.Month);
        after.Summary.Should().NotBe(before.Summary);
        var missing = (await FrozenDaysAsync(o.Owner, o, o.Month, o.Month.AddMonths(1).AddDays(-1)))
            .Single(d => d.GetProperty("date").GetString() == Key(o.D.Missing));
        missing.GetProperty("targetHours").GetDouble().Should().Be(4);
        Line(after.Daily, o.D.Missing).Should().Contain(",4.00,");
        var row = await SummaryRowAsync(o, o.Month);
        var beforeRow = JsonDocument.Parse(before.Summary).RootElement;
        row.GetProperty("targetHours").GetDouble().Should().BeLessThan(beforeRow.GetProperty("targetHours").GetDouble());

        var events = (await o.Owner.GetFromJsonAsync<JsonElement>($"/api/v1/organizations/{o.Slug}/closed-periods"))
            .GetProperty("events").EnumerateArray().ToList();
        events[0].GetProperty("kind").GetString().Should().Be("AdminChange");
        events[0].GetProperty("summary").GetString().Should().Contain("WorkSchedule");
        events[0].GetProperty("reason").GetString().Should().Be("Vertrag rückwirkend");
    }

    [Fact]
    public async Task InitialOvertime_ChangedAfterClosing_KeepsTheClosedMonth_BalanceIsCurrentInitialPlusFrozenDelta()
    {
        var o = await SetupAsync("ff-init");
        var (before, balance) = await CloseAndSnapshotAsync(o);

        (await o.Owner.PutAsJsonAsync($"/api/v1/Organizations/{o.Slug}/members/{o.MemberId}/initial-overtime",
            new { initialOvertimeHours = 10.0 })).EnsureSuccessStatusCode();

        (await FiguresAsync(o, o.Month)).Should().BeEquivalentTo(before);
        (await BalanceAsync(o)).Should().BeApproximately(balance + 10, 0.001);

        // Live: every cumulative figure of the month moves by the new initial overtime
        await ReopenAsync(o, o.Month);
        (await DailyAsync(o, o.Month)).Should().NotBe(before.Daily);
        (await BalanceAsync(o)).Should().BeApproximately(balance + 10, 0.001);
    }

    // ── Admin change, reopening, the endpoint, the migration ──

    [Fact]
    public async Task AdminEntryCorrection_WithAReason_ReFreezesTheMembersMonth()
    {
        var o = await SetupAsync("ff-refreeze");
        var (before, balance) = await CloseAndSnapshotAsync(o);
        var last = o.Month.AddMonths(1).AddDays(-1);
        (await FrozenDaysAsync(o.Owner, o, o.Month, last)).Single(d => d.GetProperty("date").GetString() == Key(o.D.Worked))
            .GetProperty("workedHours").GetDouble().Should().Be(4);

        await OkAsync(await o.Owner.SendAsync(WithReason(HttpMethod.Put,
            $"/api/v1/organizations/{o.Slug}/members/{o.MemberId}/entries/{o.EntryId}", new { endTime = Utc(o.D.Worked, 12) }, "Korrektur")));

        (await FrozenDaysAsync(o.Owner, o, o.Month, last)).Single(d => d.GetProperty("date").GetString() == Key(o.D.Worked))
            .GetProperty("workedHours").GetDouble().Should().Be(6);
        var row = await SummaryRowAsync(o, o.Month);
        var beforeRow = JsonDocument.Parse(before.Summary).RootElement;
        row.GetProperty("workedHours").GetDouble().Should().BeApproximately(beforeRow.GetProperty("workedHours").GetDouble() + 2, 0.001);
        row.GetProperty("overtimeBalance").GetDouble().Should().BeApproximately(beforeRow.GetProperty("overtimeBalance").GetDouble() + 2, 0.001);
        Line(await DailyAsync(o, o.Month), o.D.Worked).Should().Contain(",6.00,");
        (await BalanceAsync(o)).Should().BeApproximately(balance + 2, 0.001);

        var events = (await o.Owner.GetFromJsonAsync<JsonElement>($"/api/v1/organizations/{o.Slug}/closed-periods"))
            .GetProperty("events").EnumerateArray().ToList();
        events.Select(e => e.GetProperty("kind").GetString()).Should().Equal("AdminChange", "Closed");
        events[0].GetProperty("summary").GetString().Should().Be("TimeEntry:1");
    }

    [Fact]
    public async Task Reopen_MakesTheMonthLive_ClosingAgainFreezesAnew()
    {
        var o = await SetupAsync("ff-reopen");
        var (before, _) = await CloseAndSnapshotAsync(o);
        var last = o.Month.AddMonths(1).AddDays(-1);
        var created = await o.Owner.PostAsJsonAsync($"/api/v1/organizations/{o.Slug}/holidays", new { date = Key(o.D.Missing), name = "Betriebsfeier" });
        created.EnsureSuccessStatusCode();
        var holidayId = (await created.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("id").GetInt32();
        (await DailyAsync(o, o.Month)).Should().Be(before.Daily);

        await ReopenAsync(o, o.Month);
        (await FrozenDaysAsync(o.Owner, o, o.Month, last)).Should().BeEmpty();
        Line(await DailyAsync(o, o.Month), o.D.Missing).Should().Contain("Betriebsfeier");

        await CloseAsync(o, o.Month);
        var frozen = await FrozenDaysAsync(o.Owner, o, o.Month, last);
        frozen.Should().HaveCount(last.Day);
        frozen.Single(d => d.GetProperty("date").GetString() == Key(o.D.Missing)).GetProperty("holidayName").GetString().Should().Be("Betriebsfeier");
        var closedAgain = await FiguresAsync(o, o.Month);

        // Removing the holiday now is again no change of the closed month
        (await o.Owner.DeleteAsync($"/api/v1/organizations/{o.Slug}/holidays/{holidayId}")).EnsureSuccessStatusCode();
        (await FiguresAsync(o, o.Month)).Should().BeEquivalentTo(closedAgain);

        var events = (await o.Owner.GetFromJsonAsync<JsonElement>($"/api/v1/organizations/{o.Slug}/closed-periods"))
            .GetProperty("events").EnumerateArray().Select(e => e.GetProperty("kind").GetString());
        events.Should().Equal("Closed", "Reopened", "Closed");
    }

    [Fact]
    public async Task FrozenDaysEndpoint_ListsOnlyClosedDays_VisibilityLikeCompliance()
    {
        var o = await SetupAsync("ff-endpoint");
        await CloseAsync(o, o.Month);
        var last = o.Month.AddMonths(1).AddDays(-1);

        // A range over an open month before, the closed one and the current one: only the closed days
        var days = await FrozenDaysAsync(o.Member, o, o.Month.AddMonths(-1), ThisMonth.AddMonths(1).AddDays(-1));
        days.Select(d => DateOnly.Parse(d.GetProperty("date").GetString()!)).Should()
            .Equal(Enumerable.Range(0, last.Day).Select(o.Month.AddDays));
        JsonElement Day(DateOnly date) => days.Single(d => d.GetProperty("date").GetString() == Key(date));
        Day(o.D.Missing).GetProperty("status").GetString().Should().Be("Missing");
        Day(o.D.Missing).GetProperty("targetHours").GetDouble().Should().Be(8);
        Day(o.D.Worked).GetProperty("workedHours").GetDouble().Should().Be(4);
        Day(o.D.Vacation).GetProperty("absenceType").GetString().Should().Be("Vacation");
        Day(o.D.Vacation).GetProperty("creditHours").GetDouble().Should().Be(8);
        Day(o.D.Custom).GetProperty("status").GetString().Should().Be("Training");
        // Default range: the current month, never closed
        (await o.Member.GetFromJsonAsync<List<JsonElement>>($"/api/v1/organizations/{o.Slug}/members/{o.MemberId}/frozen-days")).Should().BeEmpty();

        // Same access as compliance: admins only while the org lets them see hours (403), unknown people 404
        var query = $"from={Key(o.Month)}&to={Key(last)}";
        async Task StatusAsync(HttpClient client, int memberId, HttpStatusCode? expected)
        {
            var compliance = (await client.GetAsync($"/api/v1/organizations/{o.Slug}/members/{memberId}/compliance?{query}")).StatusCode;
            var frozen = (await client.GetAsync($"/api/v1/organizations/{o.Slug}/members/{memberId}/frozen-days?{query}")).StatusCode;
            frozen.Should().Be(compliance);
            if (expected != null) frozen.Should().Be(expected);
            else frozen.Should().NotBe(HttpStatusCode.OK);
        }
        await StatusAsync(o.Owner, o.MemberId, HttpStatusCode.OK);
        (await o.Owner.PutAsJsonAsync($"/api/v1/Organizations/{o.Slug}/settings", new { memberTimeEntryVisibility = false })).EnsureSuccessStatusCode();
        await StatusAsync(o.Owner, o.MemberId, HttpStatusCode.Forbidden);
        await StatusAsync(o.Owner, 999999, HttpStatusCode.NotFound);
        await StatusAsync(o.Member, o.MemberId, HttpStatusCode.OK);
        var ownerId = (await o.Owner.GetFromJsonAsync<JsonElement>("/api/v1/Auth/me")).GetProperty("id").GetInt32();
        await StatusAsync(o.Member, ownerId, null);
        var (stranger, _) = await TestHelpers.CreateAuthenticatedUserAsync(_factory, "ff-endpoint-x@frozen.test");
        (await stranger.GetAsync($"/api/v1/organizations/{o.Slug}/members/{o.MemberId}/frozen-days?{query}")).IsSuccessStatusCode.Should().BeFalse();
    }

    [Fact]
    public async Task MonthsClosedBeforeFreezing_AreFrozenOnceAtStartup()
    {
        var o = await SetupAsync("ff-migrate");
        var last = o.Month.AddMonths(1).AddDays(-1);
        using (var scope = _factory.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<TimeTrackingDbContext>();
            db.ClosedPeriods.Add(new ClosedPeriod { OrganizationId = o.OrgId, Month = o.Month, ClosedAt = DateTime.UtcNow, FrozenAt = null });
            await db.SaveChangesAsync();
        }
        (await FrozenDaysAsync(o.Owner, o, o.Month, last)).Should().BeEmpty("closed, but not frozen yet");
        var live = await FiguresAsync(o, o.Month);

        using (var scope = _factory.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<TimeTrackingDbContext>();
            (await FrozenFigures.FreezeUnfrozenAsync(db)).Should().BeGreaterThanOrEqualTo(1);
        }
        using (var scope = _factory.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<TimeTrackingDbContext>();
            (await db.ClosedPeriods.AsNoTracking().SingleAsync(p => p.OrganizationId == o.OrgId && p.Month == o.Month)).FrozenAt.Should().NotBeNull();
            (await db.FrozenDays.AsNoTracking().CountAsync(d => d.OrganizationId == o.OrgId && d.UserId == o.MemberId)).Should().Be(last.Day);
            (await db.FrozenMonths.AsNoTracking().CountAsync(m => m.OrganizationId == o.OrgId && m.UserId == o.MemberId && m.Month == o.Month)).Should().Be(1);
            // Nothing left to do on the next start
            (await FrozenFigures.FreezeUnfrozenAsync(db)).Should().Be(0);
        }

        // Frozen from the data as it was: the same figures, and a later holiday changes none of them
        var frozen = await FiguresAsync(o, o.Month);
        frozen.Should().BeEquivalentTo(live with { Frozen = frozen.Frozen });
        frozen.Frozen.Should().Contain(Key(o.D.Missing));
        (await o.Owner.PostAsJsonAsync($"/api/v1/organizations/{o.Slug}/holidays", new { date = Key(o.D.Missing), name = "Später" }))
            .EnsureSuccessStatusCode();
        (await FiguresAsync(o, o.Month)).Should().BeEquivalentTo(frozen);
    }

    [Fact]
    public async Task OpenMonthAfterAClosedOne_ContinuesFromTheFrozenBalance()
    {
        // The data lies two months ago (closed); last month stays open with one 4 h day
        var o = await SetupAsync("ff-open", TwoMonthsAgo);
        var openDay = Weekdays(LastMonth)[2];
        await ManualAsync(o.Member, o.OrgId, Utc(openDay, 6), Utc(openDay, 10));
        await CloseAsync(o, TwoMonthsAgo);
        var closedRow = (await SummaryRowAsync(o, TwoMonthsAgo)).GetRawText();
        var openRow = (await SummaryRowAsync(o, LastMonth)).GetRawText();
        var openDaily = await DailyAsync(o, LastMonth);
        var balance = await BalanceAsync(o);

        // The open month's running total starts at the frozen month-end balance
        var columns = new[] { "Date", "Overtime", "CumulativeOvertime" };
        var closedEnd = double.Parse(Line(await DailyAsync(o, TwoMonthsAgo, columns), TwoMonthsAgo.AddMonths(1).AddDays(-1)).Split(',')[2], CultureInfo.InvariantCulture);
        var first = Line(await DailyAsync(o, LastMonth, columns), LastMonth).Split(',');
        double.Parse(first[2], CultureInfo.InvariantCulture).Should().BeApproximately(closedEnd + double.Parse(first[1], CultureInfo.InvariantCulture), 0.011);

        // A holiday in the closed month moves nothing — neither the closed month nor the open one nor today's balance
        (await o.Owner.PostAsJsonAsync($"/api/v1/organizations/{o.Slug}/holidays", new { date = Key(o.D.Missing), name = "Rückwirkend" }))
            .EnsureSuccessStatusCode();
        (await SummaryRowAsync(o, TwoMonthsAgo)).GetRawText().Should().Be(closedRow);
        (await SummaryRowAsync(o, LastMonth)).GetRawText().Should().Be(openRow);
        (await DailyAsync(o, LastMonth)).Should().Be(openDaily);
        (await BalanceAsync(o)).Should().BeApproximately(balance, 0.001);

        // A new initial overtime: the closed month keeps its balance, open months and today = new initial + frozen delta + live
        (await o.Owner.PutAsJsonAsync($"/api/v1/Organizations/{o.Slug}/members/{o.MemberId}/initial-overtime",
            new { initialOvertimeHours = 5.0 })).EnsureSuccessStatusCode();
        (await SummaryRowAsync(o, TwoMonthsAgo)).GetRawText().Should().Be(closedRow);
        var open = await SummaryRowAsync(o, LastMonth);
        open.GetProperty("overtimeBalance").GetDouble().Should()
            .BeApproximately(JsonDocument.Parse(openRow).RootElement.GetProperty("overtimeBalance").GetDouble() + 5, 0.001);
        (await BalanceAsync(o)).Should().BeApproximately(balance + 5, 0.001);
    }
}
