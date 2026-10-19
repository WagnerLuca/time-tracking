using System.Net;
using System.Net.Http.Json;
using System.Text;
using System.Text.Json;
using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using TimeTracking.Api.Data;
using TimeTracking.Api.Services;
using TimeTracking.Api.Tests.Infrastructure;

namespace TimeTracking.Api.Tests.Controllers;

/// <summary>
/// The accounting role (docs/ACCOUNTING_ROLE_PLAN.md): a membership flag granted in member management,
/// the accounting panel endpoints (monthly summary + CSV/PDF, month list with warnings, vacation
/// overview), closing/reopening months as an accountant, and that the flag grants nothing else.
/// </summary>
public class AccountingTests : IClassFixture<TimeTrackingApiFactory>
{
    private const int Allowed = 2;
    private readonly TimeTrackingApiFactory _factory;

    public AccountingTests(TimeTrackingApiFactory factory) => _factory = factory;

    private static readonly DateOnly LastMonth = ClosedPeriods.MonthOf(OrgClock.For((string?)null).Today()).AddMonths(-1);

    /// <summary>The first Monday..Friday of last month at or after the given day.</summary>
    private static DateOnly Weekday(DateOnly month, int day)
    {
        var d = month.AddDays(day - 1);
        while (d.DayOfWeek is DayOfWeek.Saturday or DayOfWeek.Sunday) d = d.AddDays(1);
        return d;
    }

    private static DateTime At(DateOnly day, int hourUtc) => day.ToDateTime(new TimeOnly(hourUtc, 0), DateTimeKind.Utc);

    /// <summary>Org whose admins may NOT see members' hours — accounting sees them anyway.</summary>
    private Task<(HttpClient Owner, HttpClient Member, string Slug, int OrgId, int MemberId)> SetupAsync(string prefix) =>
        TestHelpers.OrgWithMemberAsync(_factory, prefix,
            new { editPastEntriesMode = Allowed, memberTimeEntryVisibility = false }, "acct.test");

    private static Task<HttpResponseMessage> SetAccountantAsync(HttpClient caller, string slug, int memberId, bool value) =>
        caller.PutAsJsonAsync($"/api/v1/Organizations/{slug}/members/{memberId}/accountant", new { isAccountant = value });

    private static async Task<string?> CodeOf(HttpResponseMessage response) =>
        (await response.Content.ReadFromJsonAsync<JsonElement>()).TryGetProperty("code", out var c) ? c.GetString() : null;

    private static JsonElement Row(JsonElement members, int userId) =>
        members.EnumerateArray().Single(m => m.GetProperty("userId").GetInt32() == userId);

    [Fact]
    public async Task Flag_GrantedByOwnerOrAdmin_OnlyAccountantsAndOwnerUseThePanel_NothingElseIsGranted()
    {
        var (owner, member, slug, _, memberId) = await SetupAsync("acct-auth");
        var (admin, adminLogin) = await TestHelpers.CreateAuthenticatedUserAsync(_factory, "acct-auth-a@acct.test");
        (await TestHelpers.AddMemberAsync(_factory, owner, slug, adminLogin, role: 1)).EnsureSuccessStatusCode();
        var adminId = adminLogin.User.Id;
        var summary = $"/api/v1/organizations/{slug}/accounting/summary?year={LastMonth.Year}&month={LastMonth.Month}";

        // Without the flag: members are refused; admins and the owner reach the panel but not the hours
        // while the org hides members' hours from admins
        var refused = await member.GetAsync(summary);
        refused.StatusCode.Should().Be(HttpStatusCode.Forbidden);
        (await CodeOf(refused)).Should().Be("org.accountantRequired");
        (await member.GetAsync($"/api/v1/organizations/{slug}/accounting/vacation?year={LastMonth.Year}")).StatusCode.Should().Be(HttpStatusCode.Forbidden);
        var hidden = await admin.GetAsync(summary);
        hidden.StatusCode.Should().Be(HttpStatusCode.Forbidden);
        (await CodeOf(hidden)).Should().Be("export.hoursHiddenFromAdmins");
        (await admin.PostAsJsonAsync("/api/v1/ExportImport/payroll", new { orgSlug = slug, from = LastMonth, to = LastMonth.AddMonths(1).AddDays(-1) }))
            .StatusCode.Should().Be(HttpStatusCode.Forbidden);
        (await admin.GetAsync($"/api/v1/organizations/{slug}/accounting/vacation?year={LastMonth.Year}")).StatusCode.Should().Be(HttpStatusCode.OK);
        (await admin.GetAsync($"/api/v1/organizations/{slug}/accounting/months?year={LastMonth.Year}")).StatusCode.Should().Be(HttpStatusCode.OK);
        (await owner.GetAsync(summary)).StatusCode.Should().Be(HttpStatusCode.Forbidden);

        // Granting: members cannot, admins only for members (not themselves), the owner for anyone
        (await SetAccountantAsync(member, slug, memberId, true)).StatusCode.Should().Be(HttpStatusCode.Forbidden);
        var self = await SetAccountantAsync(admin, slug, adminId, true);
        self.StatusCode.Should().Be(HttpStatusCode.Forbidden);
        (await CodeOf(self)).Should().Be("accounting.ownerOnly");
        var granted = await SetAccountantAsync(admin, slug, memberId, true);
        granted.StatusCode.Should().Be(HttpStatusCode.OK, await granted.Content.ReadAsStringAsync());
        (await granted.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("isAccountant").GetBoolean().Should().BeTrue();
        (await SetAccountantAsync(owner, slug, adminId, true)).EnsureSuccessStatusCode();
        var ownerId = (await owner.GetFromJsonAsync<JsonElement>("/api/v1/Auth/me")).GetProperty("id").GetInt32();
        (await SetAccountantAsync(owner, slug, ownerId, true)).EnsureSuccessStatusCode();
        (await owner.GetAsync(summary)).StatusCode.Should().Be(HttpStatusCode.OK);
        (await SetAccountantAsync(owner, slug, 999999, true)).StatusCode.Should().Be(HttpStatusCode.NotFound);

        (await member.GetAsync(summary)).StatusCode.Should().Be(HttpStatusCode.OK);
        (await admin.GetAsync(summary)).StatusCode.Should().Be(HttpStatusCode.OK);
        // Accountants also get the payroll CSV, whatever the admin visibility setting
        (await member.PostAsJsonAsync("/api/v1/ExportImport/payroll", new { orgSlug = slug, from = LastMonth, to = LastMonth.AddMonths(1).AddDays(-1) }))
            .StatusCode.Should().Be(HttpStatusCode.OK);
        (await member.GetAsync($"/api/v1/organizations/{slug}/accounting/vacation?year={LastMonth.Year}")).StatusCode.Should().Be(HttpStatusCode.OK);
        (await member.GetAsync($"/api/v1/organizations/{slug}/accounting/months?year={LastMonth.Year}")).StatusCode.Should().Be(HttpStatusCode.OK);

        // Shown in "my organizations" and the member list
        var mine = await member.GetFromJsonAsync<JsonElement>("/api/v1/Organizations/mine");
        mine.EnumerateArray().Single(o => o.GetProperty("slug").GetString() == slug).GetProperty("isAccountant").GetBoolean().Should().BeTrue();
        var detail = await owner.GetFromJsonAsync<JsonElement>($"/api/v1/Organizations/{slug}");
        detail.GetProperty("members").EnumerateArray().Single(m => m.GetProperty("id").GetInt32() == memberId)
            .GetProperty("isAccountant").GetBoolean().Should().BeTrue();

        // An accountant who is a Member stays a Member everywhere else: no rules, roles, groups, terminals,
        // absence notes, presence or approvals
        (await member.PutAsJsonAsync($"/api/v1/Organizations/{slug}/settings", new { autoPauseEnabled = false }))
            .StatusCode.Should().Be(HttpStatusCode.Forbidden);
        (await member.PutAsJsonAsync($"/api/v1/Organizations/{slug}/members/{adminId}", new { role = 0 }))
            .StatusCode.Should().Be(HttpStatusCode.Forbidden);
        (await member.PostAsJsonAsync($"/api/v1/Organizations/{slug}/groups", new { name = "Accounting" }))
            .StatusCode.Should().Be(HttpStatusCode.Forbidden);
        (await member.GetAsync($"/api/v1/organizations/{slug}/api-keys")).StatusCode.Should().Be(HttpStatusCode.Forbidden);
        var asSeen = (await member.GetFromJsonAsync<JsonElement>($"/api/v1/Organizations/{slug}")).GetProperty("members").EnumerateArray()
            .Single(m => m.GetProperty("id").GetInt32() == adminId);
        asSeen.GetProperty("isPresent").ValueKind.Should().Be(JsonValueKind.Null);
        asSeen.GetProperty("visibleInfo").EnumerateArray().Select(k => k.GetString()).Should().NotContain(["TrackedTime", "Presence"]);
        (await member.PutAsJsonAsync($"/api/v1/organizations/{slug}/members/{adminId}/vacation-days", new { days = 10 }))
            .StatusCode.Should().Be(HttpStatusCode.Forbidden);
        (await SetAccountantAsync(member, slug, adminId, false)).StatusCode.Should().Be(HttpStatusCode.Forbidden);

        // Admins cannot take the flag from other admins; validation of month/year
        (await SetAccountantAsync(admin, slug, adminId, false)).StatusCode.Should().Be(HttpStatusCode.Forbidden);
        (await member.GetAsync($"/api/v1/organizations/{slug}/accounting/summary?year={LastMonth.Year}&month=13"))
            .StatusCode.Should().Be(HttpStatusCode.BadRequest);
        (await member.GetAsync($"/api/v1/organizations/{slug}/accounting/vacation?year=1999"))
            .StatusCode.Should().Be(HttpStatusCode.BadRequest);

        // Revoking applies at once; leaving clears it (a rejoin starts without it)
        (await SetAccountantAsync(owner, slug, memberId, false)).EnsureSuccessStatusCode();
        (await member.GetAsync(summary)).StatusCode.Should().Be(HttpStatusCode.Forbidden);
        (await SetAccountantAsync(owner, slug, memberId, true)).EnsureSuccessStatusCode();
        (await owner.DeleteAsync($"/api/v1/Organizations/{slug}/members/{memberId}")).EnsureSuccessStatusCode();
        using var scope = _factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<TimeTrackingDbContext>();
        (await db.UserOrganizations.IgnoreQueryFilters().SingleAsync(uo => uo.UserId == memberId && uo.Organization.Slug == slug))
            .IsAccountant.Should().BeFalse();
    }

    [Fact]
    public async Task Summary_ShowsEveryMembersMonth_RegardlessOfVisibility_AsJsonCsvAndPdf()
    {
        var (owner, member, slug, orgId, memberId) = await SetupAsync("acct-sum");
        var (accountant, accLogin) = await TestHelpers.CreateAuthenticatedUserAsync(_factory, "acct-sum-b@acct.test", "Lisa", "Buchhaltung");
        (await TestHelpers.AddMemberAsync(_factory, owner, slug, accLogin)).EnsureSuccessStatusCode();
        (await SetAccountantAsync(owner, slug, accLogin.User.Id, true)).EnsureSuccessStatusCode();

        (await owner.PostAsJsonAsync($"/api/v1/organizations/{slug}/members/{memberId}/work-schedules",
            new { validFrom = LastMonth.AddMonths(-1).ToString("yyyy-MM-dd"), weeklyWorkHours = 40 })).EnsureSuccessStatusCode();
        var workDay = Weekday(LastMonth, 3);
        var created = await member.PostAsJsonAsync("/api/v1/TimeTracking/manual",
            new { startTime = At(workDay, 6), endTime = At(workDay, 14), organizationId = orgId });
        created.StatusCode.Should().Be(HttpStatusCode.Created, await created.Content.ReadAsStringAsync());
        var failureDay = Weekday(LastMonth, 10);
        var failure = await member.PostAsJsonAsync("/api/v1/TimeTracking/manual",
            new { startTime = At(failureDay, 6), endTime = At(failureDay, 7), organizationId = orgId });
        var failureId = (await failure.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("entry").GetProperty("id").GetInt32();
        using (var scope = _factory.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<TimeTrackingDbContext>();
            var entry = await db.TimeEntries.SingleAsync(e => e.Id == failureId);
            entry.IsFailure = true;
            await db.SaveChangesAsync();
        }
        (await owner.PostAsJsonAsync($"/api/v1/organizations/{slug}/absences/admin",
            new { userId = memberId, date = Weekday(LastMonth, 17).ToString("yyyy-MM-dd"), type = 0 })).EnsureSuccessStatusCode();
        (await owner.PostAsJsonAsync($"/api/v1/organizations/{slug}/absences/admin",
            new { userId = memberId, date = Weekday(LastMonth, 20).ToString("yyyy-MM-dd"), type = 1 })).EnsureSuccessStatusCode();

        // The org hides members' hours from admins; accounting still sees them
        var query = $"year={LastMonth.Year}&month={LastMonth.Month}";
        var json = await accountant.GetFromJsonAsync<JsonElement>($"/api/v1/organizations/{slug}/accounting/summary?{query}");
        json.GetProperty("year").GetInt32().Should().Be(LastMonth.Year);
        json.GetProperty("month").GetInt32().Should().Be(LastMonth.Month);
        json.GetProperty("isClosed").GetBoolean().Should().BeFalse();
        var members = json.GetProperty("members");
        members.GetArrayLength().Should().Be(3);
        var row = Row(members, memberId);
        row.GetProperty("workedHours").GetDouble().Should().Be(7.5);   // 8 h − 30 min pause; the failure entry counts 0
        row.GetProperty("failureEntryCount").GetInt32().Should().Be(1);
        row.GetProperty("sickDays").GetDouble().Should().Be(1);
        row.GetProperty("vacationDays").GetDouble().Should().Be(1);
        row.GetProperty("targetHours").GetDouble().Should().BeGreaterThan(0);
        row.GetProperty("name").GetString().Should().Be("Test User");

        var csv = await accountant.GetAsync($"/api/v1/organizations/{slug}/accounting/summary/csv?{query}&lang=en");
        csv.EnsureSuccessStatusCode();
        csv.Content.Headers.ContentType!.MediaType.Should().Be("text/csv");
        var text = Encoding.UTF8.GetString(await csv.Content.ReadAsByteArrayAsync());
        text.Should().Contain("Personnel number;Name;Email").And.Contain("acct-sum-m@acct.test");

        var pdf = await accountant.GetAsync($"/api/v1/organizations/{slug}/accounting/summary/pdf?{query}");
        pdf.EnsureSuccessStatusCode();
        pdf.Content.Headers.ContentType!.MediaType.Should().Be("application/pdf");
        Encoding.ASCII.GetString((await pdf.Content.ReadAsByteArrayAsync())[..4]).Should().Be("%PDF");

        // The day-by-day report of one member, although the visibility matrix hides it
        (await accountant.GetAsync($"/api/v1/organizations/{slug}/members/{memberId}/reports/monthly?{query}"))
            .StatusCode.Should().NotBe(HttpStatusCode.OK);
        var memberPdf = await accountant.GetAsync($"/api/v1/organizations/{slug}/accounting/members/{memberId}/report?{query}&lang=en");
        memberPdf.EnsureSuccessStatusCode();
        memberPdf.Content.Headers.ContentType!.MediaType.Should().Be("application/pdf");
        (await accountant.GetAsync($"/api/v1/organizations/{slug}/accounting/members/999999/report?{query}"))
            .StatusCode.Should().Be(HttpStatusCode.NotFound);
        (await member.GetAsync($"/api/v1/organizations/{slug}/accounting/members/{memberId}/report?{query}"))
            .StatusCode.Should().Be(HttpStatusCode.Forbidden);
    }

    [Fact]
    public async Task Accountant_ClosesAndReopensMonths_SeesWarningsAndLog_ButCannotChangeAClosedMonth()
    {
        var (owner, member, slug, orgId, memberId) = await SetupAsync("acct-close");
        var (accountant, accLogin) = await TestHelpers.CreateAuthenticatedUserAsync(_factory, "acct-close-b@acct.test");
        (await TestHelpers.AddMemberAsync(_factory, owner, slug, accLogin)).EnsureSuccessStatusCode();
        var closeUrl = $"/api/v1/organizations/{slug}/closed-periods";
        var close = new { from = LastMonth, to = LastMonth, reason = "Payroll" };

        (await accountant.PostAsJsonAsync(closeUrl, close)).StatusCode.Should().Be(HttpStatusCode.Forbidden);
        (await SetAccountantAsync(owner, slug, accLogin.User.Id, true)).EnsureSuccessStatusCode();

        // Warnings: a failure entry and a pending request in last month
        var day = Weekday(LastMonth, 5);
        var entry = await member.PostAsJsonAsync("/api/v1/TimeTracking/manual", new { startTime = At(day, 6), endTime = At(day, 14), organizationId = orgId });
        var entryId = (await entry.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("entry").GetProperty("id").GetInt32();
        using (var scope = _factory.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<TimeTrackingDbContext>();
            (await db.TimeEntries.SingleAsync(e => e.Id == entryId)).IsFailure = true;
            await db.SaveChangesAsync();
        }
        (await owner.PutAsJsonAsync($"/api/v1/Organizations/{slug}/settings", new { absenceSelfServiceMode = 1 })).EnsureSuccessStatusCode();
        (await member.PostAsJsonAsync($"/api/v1/organizations/{slug}/requests", new
        {
            type = 6, requestData = JsonSerializer.Serialize(new { dates = new[] { Weekday(LastMonth, 12) }, type = 1 })
        })).EnsureSuccessStatusCode();

        var months = await accountant.GetFromJsonAsync<JsonElement>($"/api/v1/organizations/{slug}/accounting/months?year={LastMonth.Year}");
        months.GetProperty("months").GetArrayLength().Should().Be(12);
        var last = months.GetProperty("months")[LastMonth.Month - 1];
        last.GetProperty("month").GetString().Should().Be(LastMonth.ToString("yyyy-MM-dd"));
        last.GetProperty("hasEnded").GetBoolean().Should().BeTrue();
        last.GetProperty("isClosed").GetBoolean().Should().BeFalse();
        last.GetProperty("failureEntryCount").GetInt32().Should().Be(1);
        last.GetProperty("pendingRequestCount").GetInt32().Should().Be(1);

        var closed = await accountant.PostAsJsonAsync(closeUrl, close);
        closed.StatusCode.Should().Be(HttpStatusCode.OK, await closed.Content.ReadAsStringAsync());
        // Accountants see the audit log like admins
        (await closed.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("events").GetArrayLength().Should().Be(1);
        (await accountant.GetFromJsonAsync<JsonElement>(closeUrl)).GetProperty("events").GetArrayLength().Should().Be(1);

        months = await accountant.GetFromJsonAsync<JsonElement>($"/api/v1/organizations/{slug}/accounting/months?year={LastMonth.Year}");
        last = months.GetProperty("months")[LastMonth.Month - 1];
        last.GetProperty("isClosed").GetBoolean().Should().BeTrue();
        last.GetProperty("closedByName").GetString().Should().Be("Test User");
        (await accountant.GetFromJsonAsync<JsonElement>($"/api/v1/organizations/{slug}/accounting/summary?year={LastMonth.Year}&month={LastMonth.Month}"))
            .GetProperty("isClosed").GetBoolean().Should().BeTrue();

        // Closed for the member — and for the accountant, who is no admin (a reason does not help)
        var other = Weekday(LastMonth, 19);
        var refused = await member.PostAsJsonAsync("/api/v1/TimeTracking/manual", new { startTime = At(other, 6), endTime = At(other, 14), organizationId = orgId });
        refused.StatusCode.Should().Be(HttpStatusCode.Conflict);
        (await CodeOf(refused)).Should().Be("period.closed");
        var own = new HttpRequestMessage(HttpMethod.Post, "/api/v1/TimeTracking/manual")
        {
            Content = JsonContent.Create(new { startTime = At(other, 6), endTime = At(other, 14), organizationId = orgId })
        };
        own.Headers.Add("X-Change-Reason", "accounting");
        var ownResponse = await accountant.SendAsync(own);
        ownResponse.StatusCode.Should().Be(HttpStatusCode.Conflict);
        (await CodeOf(ownResponse)).Should().Be("period.closed");
        var absence = await accountant.PostAsJsonAsync($"/api/v1/organizations/{slug}/absences/admin", new { userId = memberId, date = other.ToString("yyyy-MM-dd"), type = 0 });
        absence.StatusCode.Should().Be(HttpStatusCode.Forbidden);

        // Reopen with a reason; the log names it
        var reopened = await accountant.PostAsJsonAsync($"{closeUrl}/reopen", new { month = LastMonth, reason = "Late sick note" });
        reopened.StatusCode.Should().Be(HttpStatusCode.OK, await reopened.Content.ReadAsStringAsync());
        var events = (await reopened.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("events").EnumerateArray().ToList();
        events.Select(e => e.GetProperty("kind").GetString()).Should().Equal("Reopened", "Closed");
        events[0].GetProperty("reason").GetString().Should().Be("Late sick note");
        (await member.PostAsJsonAsync("/api/v1/TimeTracking/manual", new { startTime = At(other, 6), endTime = At(other, 14), organizationId = orgId }))
            .StatusCode.Should().Be(HttpStatusCode.Created);
    }

    [Fact]
    public async Task Summary_ListsFormerMembersWhoWorkedInTheMonth_AndTheirReport()
    {
        var (owner, member, slug, orgId, memberId) = await SetupAsync("acct-former");
        var ownerId = (await owner.GetFromJsonAsync<JsonElement>("/api/v1/Auth/me")).GetProperty("id").GetInt32();
        (await SetAccountantAsync(owner, slug, ownerId, true)).EnsureSuccessStatusCode();
        var day = Weekday(LastMonth, 4);
        (await member.PostAsJsonAsync("/api/v1/TimeTracking/manual", new { startTime = At(day, 6), endTime = At(day, 10), organizationId = orgId }))
            .StatusCode.Should().Be(HttpStatusCode.Created);
        (await owner.DeleteAsync($"/api/v1/Organizations/{slug}/members/{memberId}")).EnsureSuccessStatusCode();

        var query = $"year={LastMonth.Year}&month={LastMonth.Month}";
        var members = (await owner.GetFromJsonAsync<JsonElement>($"/api/v1/organizations/{slug}/accounting/summary?{query}")).GetProperty("members");
        var row = Row(members, memberId);
        row.GetProperty("isFormerMember").GetBoolean().Should().BeTrue();
        row.GetProperty("workedHours").GetDouble().Should().Be(4);
        (await owner.GetAsync($"/api/v1/organizations/{slug}/accounting/members/{memberId}/report?{query}")).EnsureSuccessStatusCode();
        // An earlier month without activity does not list them
        var earlier = LastMonth.AddMonths(-1);
        (await owner.GetFromJsonAsync<JsonElement>($"/api/v1/organizations/{slug}/accounting/summary?year={earlier.Year}&month={earlier.Month}"))
            .GetProperty("members").EnumerateArray().Should().NotContain(m => m.GetProperty("userId").GetInt32() == memberId);
    }

    [Fact]
    public async Task VacationOverview_ListsEntitlementTakenPlannedRemainingAndSickDays_ForEveryone()
    {
        var (owner, _, slug, _, memberId) = await SetupAsync("acct-vac");
        (await owner.PutAsJsonAsync($"/api/v1/organizations/{slug}/members/{memberId}/vacation-days", new { days = 25 })).EnsureSuccessStatusCode();
        var today = OrgClock.For((string?)null).Today();
        var lastYear = new DateOnly(today.Year - 1, 3, 1);
        var nextYear = new DateOnly(today.Year + 1, 3, 1);

        foreach (var (date, type) in new[]
                 {
                     (Weekday(lastYear, 2), 1), (Weekday(lastYear, 9), 1), (Weekday(lastYear, 16), 0), (Weekday(lastYear, 23), 2),
                     (Weekday(nextYear, 2), 1)
                 })
            (await owner.PostAsJsonAsync($"/api/v1/organizations/{slug}/absences/admin", new { userId = memberId, date = date.ToString("yyyy-MM-dd"), type }))
                .EnsureSuccessStatusCode();

        var past = await owner.GetFromJsonAsync<JsonElement>($"/api/v1/organizations/{slug}/accounting/vacation?year={lastYear.Year}");
        past.GetProperty("year").GetInt32().Should().Be(lastYear.Year);
        var row = Row(past.GetProperty("members"), memberId);
        // Setting the allowance (25) changes this year and later ones only: last year keeps the 0 days it had
        row.GetProperty("entitlement").GetDouble().Should().Be(0);
        row.GetProperty("taken").GetDouble().Should().Be(2);
        row.GetProperty("planned").GetDouble().Should().Be(0);
        row.GetProperty("remaining").GetDouble().Should().Be(0);
        row.GetProperty("sickDays").GetDouble().Should().Be(1);
        row.GetProperty("otherAbsenceDays").GetDouble().Should().Be(1);
        past.GetProperty("members").GetArrayLength().Should().Be(2);

        var future = Row((await owner.GetFromJsonAsync<JsonElement>($"/api/v1/organizations/{slug}/accounting/vacation?year={nextYear.Year}"))
            .GetProperty("members"), memberId);
        future.GetProperty("taken").GetDouble().Should().Be(0);
        future.GetProperty("planned").GetDouble().Should().Be(1);
        // Carry-over (new orgs: until 31 March from their creation year on): this year's 25 unused days move into next
        // year, and the planned day in early March uses them first
        future.GetProperty("carriedIn").GetDouble().Should().Be(25);
        future.GetProperty("remaining").GetDouble().Should().Be(49);
    }
}
