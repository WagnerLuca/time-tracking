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
/// The vacation entitlement as § 5 BUrlG says (VacationBalances.ProRata), split by contract periods; carried-over days
/// lapse only after a notice (VacationNotices); two half-day absences on one day.
/// </summary>
public class VacationProRataTests : IClassFixture<TimeTrackingApiFactory>
{
    private readonly TimeTrackingApiFactory _factory;

    public VacationProRataTests(TimeTrackingApiFactory factory) => _factory = factory;

    private static DateOnly D(string s) => DateOnly.Parse(s);
    private static WorkSchedule Period(string from, string? to, double? days) =>
        new() { ValidFrom = D(from), ValidTo = to == null ? null : D(to), VacationDaysPerYear = days };

    [Theory]
    [InlineData("2020-01-01", null, 30)]          // whole year
    [InlineData("2026-03-01", null, 30)]          // waiting period done on 31.08. → the full year
    [InlineData("2026-07-01", null, 30)]          // waiting period ends on 31.12. → still within the year
    [InlineData("2026-07-02", null, 13)]          // not within the year: 5 full months → 12.5 → rounded up (§ 5 (2))
    [InlineData("2026-10-15", null, 5)]           // 2 full months → 2/12 × 30
    [InlineData("2026-11-15", null, 3)]           // 1 full month → 2.5 → 3
    [InlineData("2020-01-01", "2026-06-30", 15)]  // last day 30.06.: left in the first half → 6/12
    [InlineData("2020-01-01", "2026-07-01", 30)]  // last day 01.07.: second half → the full year
    [InlineData("2026-02-01", "2026-04-30", 8)]   // left before the waiting period: 3 months → 7.5 → 8
    public void ProRata_FollowsParagraph5(string start, string? end, double expected) =>
        VacationBalances.ProRata(2026, 30, D(start), end == null ? null : D(end), []).Should().Be(expected);

    [Fact]
    public void ProRata_SplitsByContractPeriods_AndNeverReducesEarnedDays()
    {
        // 30 days at 5 days/week until June, 18 at 3 days/week from July: 15 + 9 = 24
        VacationBalances.ProRata(2026, 30, D("2020-01-01"), null,
            [Period("2020-01-01", "2026-06-30", null), Period("2026-07-01", null, 18)]).Should().Be(24);
        // Without own vacation days on any period the year's figure stands
        VacationBalances.ProRata(2026, 28, D("2020-01-01"), null, [Period("2020-01-01", null, null)]).Should().Be(28);
        // Not a member that year
        VacationBalances.ProRata(2026, 30, D("2027-01-01"), null, []).Should().Be(0);
        VacationBalances.ProRata(2026, 30, D("2020-01-01"), D("2025-12-31"), []).Should().Be(0);
        // No employment dates: the year's figure
        VacationBalances.ProRata(2026, 30, null, null, []).Should().Be(30);
    }

    [Fact]
    public void Expiry_NeedsANotice()
    {
        var rule = new VacationBalances.Rule(VacationCarryOverMode.UntilDate, 3, 31, null, 2025);
        var years = new List<VacationBalances.YearInput>
        {
            new(2025, 30, 0, [new(D("2025-06-01"), 20)]),
            new(2026, 30, 0, [], NoticeSent: false)
        };
        var withoutNotice = VacationBalances.Chain(rule, years, D("2026-06-01"));
        withoutNotice.Expired.Should().Be(0);
        withoutNotice.Remaining.Should().Be(40);

        years[1] = years[1] with { NoticeSent = true };
        VacationBalances.Chain(rule, years, D("2026-06-01")).Expired.Should().Be(10);
        // A member's own date (long illness) moves it
        years[1] = years[1] with { ExpiresOn = D("2026-12-31") };
        VacationBalances.Chain(rule, years, D("2026-06-01")).Expired.Should().Be(0);
    }

    [Fact]
    public async Task Notices_InTheLastQuarter_AndBeforeTheDate_AreTheProofForTheExpiry()
    {
        var (owner, _, slug, orgId, memberId) = await TestHelpers.OrgWithMemberAsync(_factory, "vn-notice",
            new { vacationCarryOverMode = 1, vacationCarryOverExpiry = "03-31", vacationCarryOverSince = 2025 });
        (await owner.PutAsJsonAsync($"/api/v1/Organizations/{slug}/members/{memberId}/vacation-days", new { days = 30 })).EnsureSuccessStatusCode();
        (await owner.PutAsJsonAsync($"/api/v1/Organizations/{slug}/members/{memberId}/vacation/2025", new { entitlement = 30 })).EnsureSuccessStatusCode();

        await using var scope = _factory.Services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<TimeTrackingDbContext>();
        var membership = await db.UserOrganizations.SingleAsync(uo => uo.OrganizationId == orgId && uo.UserId == memberId);
        membership.JoinedAt = new DateTime(2024, 1, 1, 0, 0, 0, DateTimeKind.Utc);
        await db.SaveChangesAsync();

        // 15 October 2025: 30 days left → the notice for 2026
        (await VacationNotices.SendDueAsync(db, new DateTime(2025, 10, 15, 8, 0, 0, DateTimeKind.Utc))).Should().BeGreaterThan(0);
        var notice = await db.Notifications.Where(n => n.UserId == memberId && n.Type == VacationNotices.NotificationType).ToListAsync();
        notice.Should().ContainSingle().Which.Message.Should().StartWith("30 vacation days left, 30 lapse after 31.03.2026");
        (await db.VacationYears.SingleAsync(v => v.UserId == memberId && v.Year == 2026)).ExpiryNoticeAt.Should().NotBeNull();
        // Once a day per organization, once per year per member
        (await VacationNotices.SendDueAsync(db, new DateTime(2025, 10, 15, 9, 0, 0, DateTimeKind.Utc))).Should().Be(0);
        (await VacationNotices.SendDueAsync(db, new DateTime(2025, 10, 16, 9, 0, 0, DateTimeKind.Utc))).Should().Be(0);

        // 10 March 2026: the reminder, carried-in days still unused
        (await VacationNotices.SendDueAsync(db, new DateTime(2026, 3, 10, 8, 0, 0, DateTimeKind.Utc))).Should().Be(1);
        (await db.Notifications.CountAsync(n => n.UserId == memberId && n.Type == VacationNotices.NotificationType)).Should().Be(2);

        // The push names the lapsing days
        PushTexts.From(notice[0]).BodyDe.Should().Contain("30 Urlaubstage verfallen nach dem 31.03.2026");
    }

    [Fact]
    public async Task Entitlement_IsProRata_ByTheEmploymentDates_AndAdminsSeeTheFullYearFigure()
    {
        var (owner, member, slug, _, memberId) = await TestHelpers.OrgWithMemberAsync(_factory, "vn-prorata", new { });
        (await owner.PutAsJsonAsync($"/api/v1/Organizations/{slug}/members/{memberId}/vacation-days", new { days = 30 })).EnsureSuccessStatusCode();
        var year = DateTime.UtcNow.Year;
        string Vacation() => $"/api/v1/Organizations/{slug}/members/{memberId}/vacation?year={year}";

        // Joining the app is no employment start: the full year
        (await owner.GetFromJsonAsync<JsonElement>(Vacation())).GetProperty("entitlement").GetDouble().Should().Be(30);

        (await owner.PutAsJsonAsync($"/api/v1/Organizations/{slug}/members/{memberId}/employment", new { start = $"{year}-10-15" }))
            .EnsureSuccessStatusCode();
        var vacation = await owner.GetFromJsonAsync<JsonElement>(Vacation());
        vacation.GetProperty("entitlement").GetDouble().Should().Be(5);
        vacation.GetProperty("annualEntitlement").GetDouble().Should().Be(30);
        vacation.GetProperty("employmentStart").GetString().Should().Be($"{year}-10-15");

        // An adjustment overrides the outcome; an end before the start is refused; members cannot set the dates
        (await owner.PutAsJsonAsync($"/api/v1/Organizations/{slug}/members/{memberId}/vacation/{year}", new { adjustment = 2.5, note = "Vertrag" }))
            .EnsureSuccessStatusCode();
        (await owner.GetFromJsonAsync<JsonElement>(Vacation())).GetProperty("available").GetDouble().Should().Be(7.5);
        (await owner.PutAsJsonAsync($"/api/v1/Organizations/{slug}/members/{memberId}/employment", new { end = $"{year}-01-01" }))
            .StatusCode.Should().Be(HttpStatusCode.BadRequest);
        (await member.PutAsJsonAsync($"/api/v1/Organizations/{slug}/members/{memberId}/employment", new { clearStart = true }))
            .StatusCode.Should().Be(HttpStatusCode.Forbidden);
        (await owner.PutAsJsonAsync($"/api/v1/Organizations/{slug}/members/{memberId}/employment", new { clearStart = true }))
            .EnsureSuccessStatusCode();
        (await owner.GetFromJsonAsync<JsonElement>(Vacation())).GetProperty("entitlement").GetDouble().Should().Be(30);
    }

    [Fact]
    public async Task NewOrganizations_CarryOverWithoutLimit()
    {
        var (owner, _) = await TestHelpers.CreateAuthenticatedUserAsync(_factory, "vn-default@t.test");
        var org = await TestHelpers.CreateOrganizationAsync(owner, "Default Carry", "vn-default");
        (await owner.GetFromJsonAsync<JsonElement>($"/api/v1/Organizations/{org.Slug}"))
            .GetProperty("vacationCarryOverMode").GetString().Should().Be("Unlimited");
    }

    [Fact]
    public async Task HalfVacation_AndHalfSickDay_ShareOneDay()
    {
        var (owner, member, slug, _, memberId) = await TestHelpers.OrgWithMemberAsync(_factory, "vn-halves", new { });
        var year = DateTime.UtcNow.Year + 1;
        (await owner.PostAsJsonAsync($"/api/v1/organizations/{slug}/members/{memberId}/work-schedules",
            new { validFrom = $"{year}-01-01", weeklyWorkHours = 40, distributeEvenly = true })).EnsureSuccessStatusCode();
        var day = new DateOnly(year, 3, 2);
        while (day.DayOfWeek != DayOfWeek.Tuesday) day = day.AddDays(1);
        var date = day.ToString("yyyy-MM-dd");

        (await member.PostAsJsonAsync($"/api/v1/organizations/{slug}/absences", new { date, type = 1, isHalfDay = true })).EnsureSuccessStatusCode();
        (await member.PostAsJsonAsync($"/api/v1/organizations/{slug}/absences", new { date, type = 0, isHalfDay = true })).EnsureSuccessStatusCode();
        // The day is full now
        (await member.PostAsJsonAsync($"/api/v1/organizations/{slug}/absences", new { date, type = 0, isHalfDay = true }))
            .StatusCode.Should().Be(HttpStatusCode.Conflict);
        var both = (await owner.GetFromJsonAsync<JsonElement>($"/api/v1/organizations/{slug}/absences?userId={memberId}&from={date}&to={date}"))
            .GetProperty("items").EnumerateArray().Select(a => a.GetProperty("type").GetString()).Order().ToList();
        both.Should().Equal("SickDay", "Vacation");

        // A full day next to a half one is refused
        var other = day.AddDays(1).ToString("yyyy-MM-dd");
        (await member.PostAsJsonAsync($"/api/v1/organizations/{slug}/absences", new { date = other, type = 1, isHalfDay = true })).EnsureSuccessStatusCode();
        (await member.PostAsJsonAsync($"/api/v1/organizations/{slug}/absences", new { date = other, type = 0 }))
            .StatusCode.Should().Be(HttpStatusCode.Conflict);

        // Half vacation counts 0.5 against the year
        (await owner.GetFromJsonAsync<JsonElement>($"/api/v1/Organizations/{slug}/members/{memberId}/vacation?year={year}"))
            .GetProperty("planned").GetDouble().Should().Be(1);
    }
}
