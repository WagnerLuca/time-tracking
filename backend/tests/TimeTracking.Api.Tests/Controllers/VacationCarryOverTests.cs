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
/// Vacation carry-over over the API (Phase 2b of docs/PERFORMANCE_PLAN.md): settings validation, the member vacation
/// endpoint's visibility, per-year edits that never change a past year, and the figures in the org detail, the
/// accounting overview and the personal export. Dates are relative to the real (org-local) current year.
/// </summary>
public class VacationCarryOverTests : IClassFixture<TimeTrackingApiFactory>
{
    private readonly TimeTrackingApiFactory _factory;
    private static readonly DateOnly Today = OrgClock.For((string?)null).Today();
    private static readonly int Cy = Today.Year;

    public VacationCarryOverTests(TimeTrackingApiFactory factory) => _factory = factory;

    // ── helpers ─────────────────────────────────────────────────────────

    private static async Task<string?> CodeOf(HttpResponseMessage response) =>
        (await response.Content.ReadFromJsonAsync<JsonElement>()).TryGetProperty("code", out var c) ? c.GetString() : null;

    private async Task WithDbAsync(Func<TimeTrackingDbContext, Task> act)
    {
        using var scope = _factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<TimeTrackingDbContext>();
        await act(db);
        await db.SaveChangesAsync();
    }

    /// <summary>Back-dates the membership and sets the allowance directly (no VacationYear rows are written).</summary>
    private Task MembershipAsync(int orgId, int userId, int joinYear, double allowance) => WithDbAsync(async db =>
    {
        var uo = await db.UserOrganizations.IgnoreQueryFilters().SingleAsync(u => u.OrganizationId == orgId && u.UserId == userId);
        uo.JoinedAt = new DateTime(joinYear, 1, 15, 10, 0, 0, DateTimeKind.Utc);
        uo.VacationDaysPerYear = allowance;
    });

    /// <summary>The first <paramref name="count"/> weekdays from <paramref name="from"/>.</summary>
    private static IEnumerable<DateOnly> Weekdays(DateOnly from, int count)
    {
        var day = from;
        for (var n = 0; n < count; day = day.AddDays(1))
        {
            if (day.DayOfWeek is DayOfWeek.Saturday or DayOfWeek.Sunday) continue;
            n++;
            yield return day;
        }
    }

    private Task VacationAsync(int orgId, int userId, IEnumerable<DateOnly> dates, bool half = false, AbsenceType type = AbsenceType.Vacation) =>
        WithDbAsync(db =>
        {
            db.AbsenceDays.AddRange(dates.Select(d => new AbsenceDay
            {
                OrganizationId = orgId, UserId = userId, Date = d, Type = type, IsHalfDay = half
            }));
            return Task.CompletedTask;
        });

    private static async Task<JsonElement> VacationOf(HttpClient client, string slug, int userId, int year)
    {
        var response = await client.GetAsync($"/api/v1/organizations/{slug}/members/{userId}/vacation?year={year}");
        response.StatusCode.Should().Be(HttpStatusCode.OK, await response.Content.ReadAsStringAsync());
        return await response.Content.ReadFromJsonAsync<JsonElement>();
    }

    /// <summary>The figures of a year that must never change for a past year.</summary>
    private static string Figures(JsonElement v) => string.Join("|", new[]
        { "entitlement", "adjustment", "carriedIn", "carriedUsed", "expired", "taken", "planned", "remaining", "available" }
        .Select(p => $"{p}={v.GetProperty(p).GetDouble()}"));

    private static async Task<Dictionary<int, string>> SnapshotAsync(HttpClient admin, string slug, int userId, int from, int to)
    {
        var result = new Dictionary<int, string>();
        for (var y = from; y <= to; y++) result[y] = Figures(await VacationOf(admin, slug, userId, y));
        return result;
    }

    private static Task<HttpResponseMessage> PutYear(HttpClient client, string slug, int userId, int year, object body) =>
        client.PutAsJsonAsync($"/api/v1/organizations/{slug}/members/{userId}/vacation/{year}", body);

    private static Task<HttpResponseMessage> PutAllowance(HttpClient client, string slug, int userId, double days) =>
        client.PutAsJsonAsync($"/api/v1/organizations/{slug}/members/{userId}/vacation-days", new { days });

    private static Task<HttpResponseMessage> Settings(HttpClient owner, string slug, object body) =>
        owner.PutAsJsonAsync($"/api/v1/Organizations/{slug}/settings", body);

    private static JsonElement MemberRow(JsonElement detail, int userId) =>
        detail.GetProperty("members").EnumerateArray().Single(m => m.GetProperty("id").GetInt32() == userId);

    // ── settings ────────────────────────────────────────────────────────

    [Fact]
    public async Task NewOrg_DefaultsToUntil31March_FromItsOwnYear_WithoutCap()
    {
        var (owner, _, slug, _, _) = await TestHelpers.OrgWithMemberAsync(_factory, "vco-def", new { }, "vco.test");
        var detail = await owner.GetFromJsonAsync<JsonElement>($"/api/v1/Organizations/{slug}");
        detail.GetProperty("vacationCarryOverMode").GetString().Should().Be("UntilDate");
        detail.GetProperty("vacationCarryOverExpiry").GetString().Should().Be("03-31");
        detail.GetProperty("vacationCarryOverMaxDays").ValueKind.Should().Be(JsonValueKind.Null);
        detail.GetProperty("vacationCarryOverSince").GetInt32().Should().Be(Cy);
    }

    [Theory]
    [InlineData("02-30")]
    [InlineData("04-31")]
    [InlineData("13-01")]
    [InlineData("00-10")]
    [InlineData("03-00")]
    [InlineData("3-31")]
    [InlineData("03/31")]
    [InlineData("abc")]
    [InlineData("")]
    public async Task Settings_RejectInvalidExpiry(string expiry)
    {
        var (owner, _, slug, _, _) = await TestHelpers.OrgWithMemberAsync(_factory, $"vco-exp{Math.Abs(expiry.GetHashCode()) % 100000}", new { }, "vco.test");
        var response = await Settings(owner, slug, new { vacationCarryOverExpiry = expiry });
        response.StatusCode.Should().Be(HttpStatusCode.BadRequest);
        (await CodeOf(response)).Should().Be("org.carryOverExpiry");
    }

    [Fact]
    public async Task Settings_ValidateCapSinceAndMode_AndTheCapCanBeCleared()
    {
        var (owner, member, slug, _, _) = await TestHelpers.OrgWithMemberAsync(_factory, "vco-set", new { }, "vco.test");

        foreach (var max in new[] { -1, 365.5, 1000 })
        {
            var r = await Settings(owner, slug, new { vacationCarryOverMaxDays = max });
            r.StatusCode.Should().Be(HttpStatusCode.BadRequest);
            (await CodeOf(r)).Should().Be("org.carryOverMaxDays");
        }
        foreach (var since in new[] { 1999, Cy + 1 })
        {
            var r = await Settings(owner, slug, new { vacationCarryOverSince = since });
            r.StatusCode.Should().Be(HttpStatusCode.BadRequest);
            (await CodeOf(r)).Should().Be("org.carryOverSince");
        }
        // Undefined modes never reach the service (strict enum converter): plain 400
        (await Settings(owner, slug, new { vacationCarryOverMode = 7 })).StatusCode.Should().Be(HttpStatusCode.BadRequest);
        (await Settings(owner, slug, new { vacationCarryOverMode = "Sometimes" })).StatusCode.Should().Be(HttpStatusCode.BadRequest);
        // Members may not change the rule
        (await Settings(member, slug, new { vacationCarryOverMode = 0 })).StatusCode.Should().Be(HttpStatusCode.Forbidden);

        var ok = await Settings(owner, slug, new
        {
            vacationCarryOverMode = 2, vacationCarryOverExpiry = "02-29", vacationCarryOverMaxDays = 10.5, vacationCarryOverSince = 2000
        });
        ok.StatusCode.Should().Be(HttpStatusCode.OK, await ok.Content.ReadAsStringAsync());
        var settings = await ok.Content.ReadFromJsonAsync<JsonElement>();
        settings.GetProperty("vacationCarryOverMode").GetString().Should().Be("Unlimited");
        settings.GetProperty("vacationCarryOverExpiry").GetString().Should().Be("02-29");
        settings.GetProperty("vacationCarryOverMaxDays").GetDouble().Should().Be(10.5);
        settings.GetProperty("vacationCarryOverSince").GetInt32().Should().Be(2000);

        // A cap of 0 is a cap; clearing removes it; names work as well as numbers
        (await Settings(owner, slug, new { vacationCarryOverMaxDays = 0, vacationCarryOverMode = "None", vacationCarryOverSince = Cy }))
            .EnsureSuccessStatusCode();
        var detail = await member.GetFromJsonAsync<JsonElement>($"/api/v1/Organizations/{slug}");
        detail.GetProperty("vacationCarryOverMaxDays").GetDouble().Should().Be(0);
        detail.GetProperty("vacationCarryOverMode").GetString().Should().Be("None");
        detail.GetProperty("vacationCarryOverSince").GetInt32().Should().Be(Cy);

        (await Settings(owner, slug, new { clearVacationCarryOverMaxDays = true })).EnsureSuccessStatusCode();
        detail = await member.GetFromJsonAsync<JsonElement>($"/api/v1/Organizations/{slug}");
        detail.GetProperty("vacationCarryOverMaxDays").ValueKind.Should().Be(JsonValueKind.Null);
        detail.GetProperty("vacationCarryOverExpiry").GetString().Should().Be("02-29");   // untouched fields stay

        // Clearing wins over a value sent alongside
        (await Settings(owner, slug, new { vacationCarryOverMaxDays = 5, clearVacationCarryOverMaxDays = true })).EnsureSuccessStatusCode();
        (await member.GetFromJsonAsync<JsonElement>($"/api/v1/Organizations/{slug}"))
            .GetProperty("vacationCarryOverMaxDays").ValueKind.Should().Be(JsonValueKind.Null);
    }

    // ── vacation/{year} validation and permissions ──────────────────────

    [Fact]
    public async Task PutYear_ValidatesRangesAndNote()
    {
        var (owner, _, slug, _, memberId) = await TestHelpers.OrgWithMemberAsync(_factory, "vco-val", new { }, "vco.test");

        async Task Expect(object body, string code, int year = 0)
        {
            var r = await PutYear(owner, slug, memberId, year == 0 ? Cy : year, body);
            r.StatusCode.Should().Be(HttpStatusCode.BadRequest, code);
            (await CodeOf(r)).Should().Be(code);
        }
        await Expect(new { entitlement = -1 }, "vacation.entitlementRange");
        await Expect(new { entitlement = 365.5 }, "vacation.entitlementRange");
        await Expect(new { adjustment = -366 }, "vacation.adjustmentRange");
        await Expect(new { adjustment = 365.5 }, "vacation.adjustmentRange");
        await Expect(new { note = new string('x', 501) }, "vacation.noteTooLong");
        await Expect(new { entitlement = 10 }, "accounting.invalidYear", 1999);
        await Expect(new { entitlement = 10 }, "accounting.invalidYear", 2101);
        (await owner.GetAsync($"/api/v1/organizations/{slug}/members/{memberId}/vacation?year=1999"))
            .StatusCode.Should().Be(HttpStatusCode.BadRequest);

        // Limits themselves are fine; a note of exactly 500 characters too (trimmed)
        (await PutYear(owner, slug, memberId, Cy, new { entitlement = 365, adjustment = -365, note = "  " + new string('n', 500) + " " }))
            .EnsureSuccessStatusCode();
        var v = await VacationOf(owner, slug, memberId, Cy);
        v.GetProperty("entitlement").GetDouble().Should().Be(365);
        v.GetProperty("adjustment").GetDouble().Should().Be(-365);
        v.GetProperty("adjustmentNote").GetString().Should().HaveLength(500);
        v.GetProperty("hasOwnEntitlement").GetBoolean().Should().BeTrue();

        // An empty note clears it; left-out fields stay
        (await PutYear(owner, slug, memberId, Cy, new { note = "" })).EnsureSuccessStatusCode();
        v = await VacationOf(owner, slug, memberId, Cy);
        v.GetProperty("adjustmentNote").ValueKind.Should().Be(JsonValueKind.Null);
        v.GetProperty("adjustment").GetDouble().Should().Be(-365);
        v.GetProperty("entitlement").GetDouble().Should().Be(365);

        // Unknown member
        (await PutYear(owner, slug, 999999, Cy, new { entitlement = 10 })).StatusCode.Should().Be(HttpStatusCode.NotFound);
    }

    [Fact]
    public async Task PutYear_OrgAdminsOnly_NotMembersOrGroupAdmins()
    {
        var (owner, member, slug, _, memberId) = await TestHelpers.OrgWithMemberAsync(_factory, "vco-perm", new { }, "vco.test");
        var (lead, leadLogin) = await TestHelpers.CreateAuthenticatedUserAsync(_factory, "vco-perm-lead@vco.test");
        (await TestHelpers.AddMemberAsync(_factory, owner, slug, leadLogin)).EnsureSuccessStatusCode();
        await TestHelpers.CreateGroupAsync(owner, slug, "Team",
            new { groupAdminCanApproveAbsences = true, groupAdminCanManageMembers = true, groupAdminCanViewAllTimeEntries = true },
            (memberId, "Member"), (leadLogin.User.Id, "Admin"));
        var (admin, adminLogin) = await TestHelpers.CreateAuthenticatedUserAsync(_factory, "vco-perm-admin@vco.test");
        (await TestHelpers.AddMemberAsync(_factory, owner, slug, adminLogin, role: 1)).EnsureSuccessStatusCode();

        (await PutYear(member, slug, memberId, Cy, new { adjustment = 1 })).StatusCode.Should().Be(HttpStatusCode.Forbidden);
        (await PutYear(lead, slug, memberId, Cy, new { adjustment = 1 })).StatusCode.Should().Be(HttpStatusCode.Forbidden);
        (await PutYear(admin, slug, memberId, Cy, new { adjustment = 1 })).StatusCode.Should().Be(HttpStatusCode.OK);
        (await PutYear(owner, slug, memberId, Cy, new { adjustment = 2 })).StatusCode.Should().Be(HttpStatusCode.OK);
        (await VacationOf(owner, slug, memberId, Cy)).GetProperty("adjustment").GetDouble().Should().Be(2);
    }

    // ── GET members/{id}/vacation visibility ────────────────────────────

    [Fact]
    public async Task GetVacation_FollowsTheVacationBalanceKind_AndHidesTheNoteFromColleagues()
    {
        var (owner, member, slug, _, memberId) = await TestHelpers.OrgWithMemberAsync(_factory, "vco-vis",
            new { visibility = TestHelpers.Matrix(("VacationBalance", true, true)) }, "vco.test");
        var (colleague, colleagueLogin) = await TestHelpers.CreateAuthenticatedUserAsync(_factory, "vco-vis-c@vco.test");
        (await TestHelpers.AddMemberAsync(_factory, owner, slug, colleagueLogin)).EnsureSuccessStatusCode();
        var (accountant, accLogin) = await TestHelpers.CreateAuthenticatedUserAsync(_factory, "vco-vis-a@vco.test");
        (await TestHelpers.AddMemberAsync(_factory, owner, slug, accLogin)).EnsureSuccessStatusCode();
        (await owner.PutAsJsonAsync($"/api/v1/Organizations/{slug}/members/{accLogin.User.Id}/accountant", new { isAccountant = true }))
            .EnsureSuccessStatusCode();
        (await PutYear(owner, slug, memberId, Cy, new { adjustment = 3, note = "Altsystem" })).EnsureSuccessStatusCode();

        // The member, admins and accountants see the note
        (await VacationOf(member, slug, memberId, Cy)).GetProperty("adjustmentNote").GetString().Should().Be("Altsystem");
        var forOwner = await VacationOf(owner, slug, memberId, Cy);
        forOwner.GetProperty("adjustmentNote").GetString().Should().Be("Altsystem");
        forOwner.GetProperty("updatedByName").GetString().Should().NotBeNullOrEmpty();
        (await VacationOf(accountant, slug, memberId, Cy)).GetProperty("adjustmentNote").GetString().Should().Be("Altsystem");

        // A colleague who may see the balance gets the figures, not the note
        var forColleague = await VacationOf(colleague, slug, memberId, Cy);
        forColleague.GetProperty("adjustment").GetDouble().Should().Be(3);
        forColleague.TryGetProperty("adjustmentNote", out var note).Should().BeTrue();
        note.ValueKind.Should().Be(JsonValueKind.Null, "the note is for the member, admins and accountants only");
        forColleague.GetProperty("updatedAt").ValueKind.Should().Be(JsonValueKind.Null);
        forColleague.GetProperty("updatedByName").ValueKind.Should().Be(JsonValueKind.Null);
        (await VacationOf(member, slug, memberId, Cy)).GetProperty("updatedAt").ValueKind.Should().Be(JsonValueKind.String);
        (await VacationOf(accountant, slug, memberId, Cy)).GetProperty("updatedByName").GetString().Should().NotBeNullOrEmpty();

        // Default year = the current one
        (await member.GetFromJsonAsync<JsonElement>($"/api/v1/organizations/{slug}/members/{memberId}/vacation"))
            .GetProperty("year").GetInt32().Should().Be(Cy);

        // Kind hidden → 403 for the colleague; the member still sees themselves, the accountant everybody
        (await Settings(owner, slug, new { visibility = TestHelpers.Matrix(("VacationBalance", false, false)) })).EnsureSuccessStatusCode();
        (await colleague.GetAsync($"/api/v1/organizations/{slug}/members/{memberId}/vacation")).StatusCode.Should().Be(HttpStatusCode.Forbidden);
        (await member.GetAsync($"/api/v1/organizations/{slug}/members/{memberId}/vacation")).StatusCode.Should().Be(HttpStatusCode.OK);
        (await owner.GetAsync($"/api/v1/organizations/{slug}/members/{memberId}/vacation")).StatusCode.Should().Be(HttpStatusCode.OK);
        (await accountant.GetAsync($"/api/v1/organizations/{slug}/members/{memberId}/vacation")).StatusCode.Should().Be(HttpStatusCode.OK);

        // Person hidden → 404 (as if not there); unknown → 404; not in the org → 403
        (await Settings(owner, slug, new { visibility = TestHelpers.Matrix(("Member", false, false)) })).EnsureSuccessStatusCode();
        (await colleague.GetAsync($"/api/v1/organizations/{slug}/members/{memberId}/vacation")).StatusCode.Should().Be(HttpStatusCode.NotFound);
        (await accountant.GetAsync($"/api/v1/organizations/{slug}/members/{memberId}/vacation")).StatusCode.Should().Be(HttpStatusCode.OK);
        (await owner.GetAsync($"/api/v1/organizations/{slug}/members/999999/vacation")).StatusCode.Should().Be(HttpStatusCode.NotFound);
        (await accountant.GetAsync($"/api/v1/organizations/{slug}/members/999999/vacation")).StatusCode.Should().Be(HttpStatusCode.NotFound);
        var (outsider, _) = await TestHelpers.CreateAuthenticatedUserAsync(_factory, "vco-vis-x@vco.test");
        (await outsider.GetAsync($"/api/v1/organizations/{slug}/members/{memberId}/vacation")).StatusCode.Should().Be(HttpStatusCode.Forbidden);
    }

    // ── edits never change a past year ──────────────────────────────────

    /// <summary>Joined 3 years ago, carry-over since then, 30 days a year and some vacation in every year.</summary>
    private async Task<(HttpClient Owner, string Slug, int OrgId, int MemberId)> HistoryAsync(string prefix)
    {
        var (owner, _, slug, orgId, memberId) = await TestHelpers.OrgWithMemberAsync(_factory, prefix,
            new { vacationCarryOverSince = Cy - 3 }, "vco.test");
        await MembershipAsync(orgId, memberId, Cy - 3, 30);
        await VacationAsync(orgId, memberId, Weekdays(new DateOnly(Cy - 3, 6, 1), 25));
        await VacationAsync(orgId, memberId, Weekdays(new DateOnly(Cy - 2, 2, 1), 4).Concat(Weekdays(new DateOnly(Cy - 2, 6, 1), 10)));
        await VacationAsync(orgId, memberId, Weekdays(new DateOnly(Cy - 1, 6, 1), 28));
        await VacationAsync(orgId, memberId, Weekdays(new DateOnly(Cy, 2, 2), 3));
        return (owner, slug, orgId, memberId);
    }

    private static async Task<double> EntitlementOf(HttpClient admin, string slug, int userId, int year) =>
        (await VacationOf(admin, slug, userId, year)).GetProperty("entitlement").GetDouble();

    [Fact]
    public async Task AllowanceEdits_ChangeOnlyTheCurrentYearAndLater()
    {
        var (owner, slug, _, memberId) = await HistoryAsync("vco-allow");
        var before = await SnapshotAsync(owner, slug, memberId, Cy - 3, Cy - 1);

        (await PutAllowance(owner, slug, memberId, 25)).EnsureSuccessStatusCode();
        (await SnapshotAsync(owner, slug, memberId, Cy - 3, Cy - 1)).Should().Equal(before);
        (await EntitlementOf(owner, slug, memberId, Cy)).Should().Be(25);
        (await EntitlementOf(owner, slug, memberId, Cy + 1)).Should().Be(25);

        // Years before the chain start and `since` are no carry-over years: they still fall back to the allowance
        (await EntitlementOf(owner, slug, memberId, Cy - 6)).Should().Be(25);

        (await PutAllowance(owner, slug, memberId, 27.5)).EnsureSuccessStatusCode();
        (await SnapshotAsync(owner, slug, memberId, Cy - 3, Cy - 1)).Should().Equal(before);
        (await EntitlementOf(owner, slug, memberId, Cy)).Should().Be(27.5);
        (await EntitlementOf(owner, slug, memberId, Cy + 2)).Should().Be(27.5);
    }

    [Fact]
    public async Task CurrentYearPut_DoesNotChangeAnyPastYear()
    {
        var (owner, slug, _, memberId) = await HistoryAsync("vco-curput");
        var before = await SnapshotAsync(owner, slug, memberId, Cy - 3, Cy - 1);

        (await PutYear(owner, slug, memberId, Cy, new { entitlement = 20 })).EnsureSuccessStatusCode();
        (await SnapshotAsync(owner, slug, memberId, Cy - 3, Cy - 1)).Should().Equal(before);
        var current = await VacationOf(owner, slug, memberId, Cy);
        current.GetProperty("entitlement").GetDouble().Should().Be(20);
        current.GetProperty("hasOwnEntitlement").GetBoolean().Should().BeTrue();
        current.GetProperty("vacationDaysPerYear").GetDouble().Should().Be(20, "a current-year PUT is the allowance from now on");
        (await EntitlementOf(owner, slug, memberId, Cy + 1)).Should().Be(30, "year+1 is pinned by a single-year edit");
    }

    [Fact]
    public async Task SingleYearEdit_ChangesExactlyThatYearsEntitlement_PastCurrentOrFuture()
    {
        var (owner, slug, _, memberId) = await HistoryAsync("vco-single");
        var years = Enumerable.Range(Cy - 3, 6).ToList();   // Cy−3 … Cy+2

        async Task<Dictionary<int, double>> Entitlements()
        {
            var result = new Dictionary<int, double>();
            foreach (var y in years) result[y] = await EntitlementOf(owner, slug, memberId, y);
            return result;
        }

        foreach (var (year, value) in new[] { (Cy - 2, 20.0), (Cy, 22.0), (Cy + 1, 32.0), (Cy - 3, 18.0) })
        {
            var before = await Entitlements();
            var earlier = await SnapshotAsync(owner, slug, memberId, Cy - 3, year - 1);
            (await PutYear(owner, slug, memberId, year, new { entitlement = value })).EnsureSuccessStatusCode();

            var after = await Entitlements();
            after[year].Should().Be(value, $"the edited year {year}");
            foreach (var y in years.Where(y => y != year))
                after[y].Should().Be(before[y], $"editing {year} must not change the entitlement of {y}");
            // Years before the edited one keep every figure
            (await SnapshotAsync(owner, slug, memberId, Cy - 3, year - 1)).Should().Equal(earlier);
        }
    }

    [Fact]
    public async Task EditSequence_PastPut_Allowance_FuturePut_Allowance_NeverChangesAPastYear()
    {
        var (owner, slug, _, memberId) = await HistoryAsync("vco-seq");

        // 1. A single past year: earlier years keep everything, later ones keep their entitlement
        var s0 = await SnapshotAsync(owner, slug, memberId, Cy - 3, Cy - 1);
        (await PutYear(owner, slug, memberId, Cy - 2, new { entitlement = 20 })).EnsureSuccessStatusCode();
        (await SnapshotAsync(owner, slug, memberId, Cy - 3, Cy - 3)).Should().Equal(s0.Where(kv => kv.Key == Cy - 3));
        (await EntitlementOf(owner, slug, memberId, Cy - 2)).Should().Be(20);
        (await EntitlementOf(owner, slug, memberId, Cy - 1)).Should().Be(30);
        (await EntitlementOf(owner, slug, memberId, Cy)).Should().Be(30);

        // 2. The allowance: no past year changes (also the one before the single-year row)
        var s1 = await SnapshotAsync(owner, slug, memberId, Cy - 3, Cy - 1);
        (await PutAllowance(owner, slug, memberId, 25)).EnsureSuccessStatusCode();
        (await SnapshotAsync(owner, slug, memberId, Cy - 3, Cy - 1)).Should().Equal(s1);
        (await EntitlementOf(owner, slug, memberId, Cy)).Should().Be(25);

        // 3. A future year: nothing up to the current year changes
        var s2 = await SnapshotAsync(owner, slug, memberId, Cy - 3, Cy);
        (await PutYear(owner, slug, memberId, Cy + 1, new { entitlement = 32 })).EnsureSuccessStatusCode();
        (await SnapshotAsync(owner, slug, memberId, Cy - 3, Cy)).Should().Equal(s2);
        (await EntitlementOf(owner, slug, memberId, Cy + 1)).Should().Be(32);
        (await EntitlementOf(owner, slug, memberId, Cy + 2)).Should().Be(25);

        // 4. The allowance again: past years still as they were; it is the value from now on, so it also replaces the
        //    future year's own entitlement (and the year after, pinned in step 3)
        (await PutAllowance(owner, slug, memberId, 27)).EnsureSuccessStatusCode();
        (await SnapshotAsync(owner, slug, memberId, Cy - 3, Cy - 1)).Should().Equal(s1);
        (await EntitlementOf(owner, slug, memberId, Cy)).Should().Be(27);
        (await EntitlementOf(owner, slug, memberId, Cy + 1)).Should().Be(27);
        (await EntitlementOf(owner, slug, memberId, Cy + 2)).Should().Be(27);
        (await VacationOf(owner, slug, memberId, Cy + 1)).GetProperty("hasOwnEntitlement").GetBoolean().Should().BeFalse();

        // 5. Another single past year after all that: only that year's entitlement changes
        var s3 = await SnapshotAsync(owner, slug, memberId, Cy - 3, Cy - 2);
        (await PutYear(owner, slug, memberId, Cy - 1, new { entitlement = 29 })).EnsureSuccessStatusCode();
        (await SnapshotAsync(owner, slug, memberId, Cy - 3, Cy - 2)).Should().Equal(s3);
        (await EntitlementOf(owner, slug, memberId, Cy - 1)).Should().Be(29);
        (await EntitlementOf(owner, slug, memberId, Cy)).Should().Be(27);
    }

    [Fact]
    public async Task ClearEntitlement_ReturnsAYearToInherited()
    {
        var (owner, slug, _, memberId) = await HistoryAsync("vco-clear");
        (await PutYear(owner, slug, memberId, Cy - 2, new { entitlement = 20, adjustment = 1 })).EnsureSuccessStatusCode();
        var cy3 = await SnapshotAsync(owner, slug, memberId, Cy - 3, Cy - 3);
        (await EntitlementOf(owner, slug, memberId, Cy - 2)).Should().Be(20);

        (await PutYear(owner, slug, memberId, Cy - 2, new { clearEntitlement = true })).EnsureSuccessStatusCode();
        var v = await VacationOf(owner, slug, memberId, Cy - 2);
        v.GetProperty("hasOwnEntitlement").GetBoolean().Should().BeFalse();
        v.GetProperty("entitlement").GetDouble().Should().Be(30);   // the earlier year's / the allowance
        v.GetProperty("adjustment").GetDouble().Should().Be(1, "clearing the entitlement keeps the adjustment");
        (await SnapshotAsync(owner, slug, memberId, Cy - 3, Cy - 3)).Should().Equal(cy3);
        (await EntitlementOf(owner, slug, memberId, Cy - 1)).Should().Be(30);
    }

    // ── counted days ────────────────────────────────────────────────────

    [Fact]
    public async Task UsedDays_SkipHolidaysAndDaysOff_CountHalfDaysAndVacationTypesOnly()
    {
        var (owner, _, slug, orgId, memberId) = await TestHelpers.OrgWithMemberAsync(_factory, "vco-used", new { }, "vco.test");
        await MembershipAsync(orgId, memberId, Cy - 1, 30);
        var year = Cy - 1;
        var monday = Weekdays(new DateOnly(year, 6, 1), 1).Single();
        while (monday.DayOfWeek != DayOfWeek.Monday) monday = monday.AddDays(1);
        (await owner.PostAsJsonAsync($"/api/v1/organizations/{slug}/holidays",
            new { date = monday.AddDays(1).ToString("yyyy-MM-dd"), name = "Feiertag", isRecurring = false })).EnsureSuccessStatusCode();

        await VacationAsync(orgId, memberId, [monday]);                               // counts 1
        await VacationAsync(orgId, memberId, [monday.AddDays(1)]);                    // holiday: 0
        await VacationAsync(orgId, memberId, [monday.AddDays(2)], half: true);       // 0.5
        await VacationAsync(orgId, memberId, [monday.AddDays(5), monday.AddDays(6)]); // weekend: 0
        await VacationAsync(orgId, memberId, [monday.AddDays(3)], type: AbsenceType.SickDay);   // not vacation

        var v = await VacationOf(owner, slug, memberId, year);
        v.GetProperty("taken").GetDouble().Should().Be(1.5);
        v.GetProperty("planned").GetDouble().Should().Be(0);
        v.GetProperty("remaining").GetDouble().Should().Be(28.5);
    }

    // ── figures in org detail, accounting, export ───────────────────────

    [Fact]
    public async Task OrgDetail_AccountingOverview_AndExport_CarryTheCarryOverFigures()
    {
        var (owner, member, slug, orgId, memberId) = await TestHelpers.OrgWithMemberAsync(_factory, "vco-fig",
            new { vacationCarryOverSince = Cy - 1 }, "vco.test");
        await MembershipAsync(orgId, memberId, Cy - 1, 30);
        await VacationAsync(orgId, memberId, Weekdays(new DateOnly(Cy - 1, 6, 1), 25));
        // Two days before 31 March use the carry; one in July; the other 3 carried days lapsed (today is past March)
        await VacationAsync(orgId, memberId, Weekdays(new DateOnly(Cy, 2, 2), 2));
        await VacationAsync(orgId, memberId, Weekdays(new DateOnly(Cy, 7, 6), 1));
        (await PutYear(owner, slug, memberId, Cy, new { adjustment = 2, note = "Altsystem" })).EnsureSuccessStatusCode();
        var expiresOn = $"{Cy}-03-31";

        var row = MemberRow(await owner.GetFromJsonAsync<JsonElement>($"/api/v1/Organizations/{slug}"), memberId);
        row.GetProperty("vacationEntitlement").GetDouble().Should().Be(30);
        row.GetProperty("vacationAdjustment").GetDouble().Should().Be(2);
        row.GetProperty("vacationCarriedIn").GetDouble().Should().Be(5);
        row.GetProperty("vacationCarriedUsed").GetDouble().Should().Be(2);
        row.GetProperty("vacationExpired").GetDouble().Should().Be(3);
        row.GetProperty("vacationExpiresOn").GetString().Should().Be(expiresOn);
        row.GetProperty("vacationAvailable").GetDouble().Should().Be(34);
        row.GetProperty("vacationDaysUsed").GetDouble().Should().Be(3);
        row.GetProperty("vacationDaysTaken").GetDouble().Should().Be(3);
        row.GetProperty("vacationDaysRemaining").GetDouble().Should().Be(31);
        row.GetProperty("vacationVisible").GetBoolean().Should().BeTrue();

        // The member sees their own figures; the owner's row is visible to them? Not by default (kind hidden): zeros
        var own = MemberRow(await member.GetFromJsonAsync<JsonElement>($"/api/v1/Organizations/{slug}"), memberId);
        own.GetProperty("vacationDaysRemaining").GetDouble().Should().Be(31);

        var v = await VacationOf(owner, slug, memberId, Cy);
        v.GetProperty("carriedIn").GetDouble().Should().Be(5);
        v.GetProperty("carriedUsed").GetDouble().Should().Be(2);
        v.GetProperty("expired").GetDouble().Should().Be(3);
        v.GetProperty("expiresOn").GetString().Should().Be(expiresOn);
        v.GetProperty("available").GetDouble().Should().Be(34);
        v.GetProperty("remaining").GetDouble().Should().Be(31);
        v.GetProperty("hasOwnEntitlement").GetBoolean().Should().BeFalse();
        v.GetProperty("vacationDaysPerYear").GetDouble().Should().Be(30);

        var overview = await owner.GetFromJsonAsync<JsonElement>($"/api/v1/organizations/{slug}/accounting/vacation?year={Cy}");
        var acc = overview.GetProperty("members").EnumerateArray().Single(m => m.GetProperty("userId").GetInt32() == memberId);
        acc.GetProperty("entitlement").GetDouble().Should().Be(30);
        acc.GetProperty("adjustment").GetDouble().Should().Be(2);
        acc.GetProperty("carriedIn").GetDouble().Should().Be(5);
        acc.GetProperty("expired").GetDouble().Should().Be(3);
        acc.GetProperty("expiresOn").GetString().Should().Be(expiresOn);
        acc.GetProperty("taken").GetDouble().Should().Be(3);
        acc.GetProperty("remaining").GetDouble().Should().Be(31);

        // Last year: no carry-in (first chain year), all 25 taken
        var last = (await owner.GetFromJsonAsync<JsonElement>($"/api/v1/organizations/{slug}/accounting/vacation?year={Cy - 1}"))
            .GetProperty("members").EnumerateArray().Single(m => m.GetProperty("userId").GetInt32() == memberId);
        last.GetProperty("carriedIn").GetDouble().Should().Be(0);
        last.GetProperty("expiresOn").ValueKind.Should().Be(JsonValueKind.Null);
        last.GetProperty("remaining").GetDouble().Should().Be(5);

        // Personal export: the member's VacationYear rows
        var export = JsonDocument.Parse(await member.GetStringAsync("/api/v1/Auth/me/export")).RootElement;
        var years = export.GetProperty("vacationYears").EnumerateArray().ToList();
        years.Should().ContainSingle(y => y.GetProperty("year").GetInt32() == Cy);
        var cur = years.Single(y => y.GetProperty("year").GetInt32() == Cy);
        cur.GetProperty("adjustment").GetDouble().Should().Be(2);
        cur.GetProperty("adjustmentNote").GetString().Should().Be("Altsystem");
        cur.GetProperty("organization").GetString().Should().Be("vco-fig Org");
        // The owner has no rows
        JsonDocument.Parse(await owner.GetStringAsync("/api/v1/Auth/me/export")).RootElement
            .GetProperty("vacationYears").GetArrayLength().Should().Be(0);
    }

    [Fact]
    public async Task OrgSinceThisYear_NoCarryIntoThisYear_LastYearUnchanged()
    {
        // A new org's `since` is its creation year (= a migrated org's 2026): last year's leftover does not carry in
        var (owner, _, slug, orgId, memberId) = await TestHelpers.OrgWithMemberAsync(_factory, "vco-since", new { }, "vco.test");
        await MembershipAsync(orgId, memberId, Cy - 2, 30);
        await VacationAsync(orgId, memberId, Weekdays(new DateOnly(Cy - 1, 6, 1), 5));
        await VacationAsync(orgId, memberId, Weekdays(new DateOnly(Cy, 6, 1), 4));

        var current = await VacationOf(owner, slug, memberId, Cy);
        current.GetProperty("carriedIn").GetDouble().Should().Be(0);
        current.GetProperty("expiresOn").ValueKind.Should().Be(JsonValueKind.Null);
        current.GetProperty("remaining").GetDouble().Should().Be(26);
        var last = await VacationOf(owner, slug, memberId, Cy - 1);
        last.GetProperty("carriedIn").GetDouble().Should().Be(0);
        last.GetProperty("remaining").GetDouble().Should().Be(25);

        // This year's leftover moves into next year
        var next = await VacationOf(owner, slug, memberId, Cy + 1);
        next.GetProperty("carriedIn").GetDouble().Should().Be(26);
        next.GetProperty("expiresOn").GetString().Should().Be($"{Cy + 1}-03-31");
    }

    [Fact]
    public async Task ChainStartsAtTheJoinYear_WhenTheMemberJoinedAfterSince()
    {
        var (owner, _, slug, orgId, memberId) = await TestHelpers.OrgWithMemberAsync(_factory, "vco-join",
            new { vacationCarryOverSince = Cy - 3 }, "vco.test");
        await MembershipAsync(orgId, memberId, Cy, 30);   // joined this year

        var current = await VacationOf(owner, slug, memberId, Cy);
        current.GetProperty("carriedIn").GetDouble().Should().Be(0, "the join year gets no carry-in");
        current.GetProperty("remaining").GetDouble().Should().Be(30);
        (await VacationOf(owner, slug, memberId, Cy + 1)).GetProperty("carriedIn").GetDouble().Should().Be(30);
    }

    [Fact]
    public async Task ModeNone_And_Cap_AreAppliedToEveryYearAtOnce()
    {
        var (owner, _, slug, orgId, memberId) = await TestHelpers.OrgWithMemberAsync(_factory, "vco-mode",
            new { vacationCarryOverSince = Cy - 1 }, "vco.test");
        await MembershipAsync(orgId, memberId, Cy - 1, 30);
        await VacationAsync(orgId, memberId, Weekdays(new DateOnly(Cy - 1, 6, 1), 20));

        (await VacationOf(owner, slug, memberId, Cy)).GetProperty("carriedIn").GetDouble().Should().Be(10);
        (await Settings(owner, slug, new { vacationCarryOverMaxDays = 4 })).EnsureSuccessStatusCode();
        (await VacationOf(owner, slug, memberId, Cy)).GetProperty("carriedIn").GetDouble().Should().Be(4);
        (await Settings(owner, slug, new { vacationCarryOverMode = 0 })).EnsureSuccessStatusCode();
        var none = await VacationOf(owner, slug, memberId, Cy);
        none.GetProperty("carriedIn").GetDouble().Should().Be(0);
        none.GetProperty("remaining").GetDouble().Should().Be(30);
        (await Settings(owner, slug, new { vacationCarryOverMode = 2, clearVacationCarryOverMaxDays = true })).EnsureSuccessStatusCode();
        var unlimited = await VacationOf(owner, slug, memberId, Cy);
        unlimited.GetProperty("carriedIn").GetDouble().Should().Be(10);
        unlimited.GetProperty("expired").GetDouble().Should().Be(0);
        unlimited.GetProperty("expiresOn").ValueKind.Should().Be(JsonValueKind.Null);
        unlimited.GetProperty("remaining").GetDouble().Should().Be(40);
    }
}
