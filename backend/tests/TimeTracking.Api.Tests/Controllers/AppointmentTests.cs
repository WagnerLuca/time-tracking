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
/// Planned appointments (issue #83): CRUD and who may manage them, the visibility matrix kinds Appointments /
/// AppointmentNotes, not counting anywhere in hours, and the core-hours exemption (on time, catch-up, backfill,
/// night windows, re-evaluation in the compliance list).
/// </summary>
public class AppointmentTests : IClassFixture<TimeTrackingApiFactory>
{
    private static readonly OrgClock Berlin = OrgClock.For("Europe/Berlin");
    private readonly TimeTrackingApiFactory _factory;

    public AppointmentTests(TimeTrackingApiFactory factory) => _factory = factory;

    private static string Url(string slug) => $"/api/v1/organizations/{slug}/appointments";

    private static async Task<JsonElement> CreatedAsync(HttpResponseMessage res)
    {
        res.StatusCode.Should().Be(HttpStatusCode.Created, await res.Content.ReadAsStringAsync());
        return await res.Content.ReadFromJsonAsync<JsonElement>();
    }

    private static async Task<string?> CodeAsync(HttpResponseMessage res) =>
        (await res.Content.ReadFromJsonAsync<JsonElement>()).TryGetProperty("code", out var c) ? c.GetString() : null;

    private static async Task<List<JsonElement>> ListAsync(HttpClient client, string slug, string query = "from=2026-03-01&to=2026-03-31")
    {
        var res = await client.GetAsync($"{Url(slug)}?{query}");
        res.StatusCode.Should().Be(HttpStatusCode.OK, await res.Content.ReadAsStringAsync());
        return (await res.Content.ReadFromJsonAsync<List<JsonElement>>())!;
    }

    private async Task<(HttpClient Client, int Id)> JoinAsync(HttpClient owner, string slug, string email)
    {
        var (client, login) = await TestHelpers.CreateAuthenticatedUserAsync(_factory, email);
        (await TestHelpers.AddMemberAsync(_factory, owner, slug, login)).EnsureSuccessStatusCode();
        return (client, login.User.Id);
    }

    // ── CRUD ───────────────────────────────────────────────────────────

    [Fact]
    public async Task Member_CreatesEditsAndDeletesOwnAppointments_WithValidation()
    {
        var (_, member, slug, _, memberId) = await TestHelpers.OrgWithMemberAsync(_factory, "appt-crud", new { });

        var created = await CreatedAsync(await member.PostAsJsonAsync(Url(slug), new { date = "2026-03-03", startTime = "10:00", endTime = "12:00", note = " Arzt " }));
        created.GetProperty("userId").GetInt32().Should().Be(memberId);
        created.GetProperty("startTime").GetString().Should().Be("10:00");
        created.GetProperty("endTime").GetString().Should().Be("12:00");
        created.GetProperty("allDay").GetBoolean().Should().BeFalse();
        created.GetProperty("note").GetString().Should().Be("Arzt");
        created.GetProperty("canEdit").GetBoolean().Should().BeTrue();
        created.GetProperty("createdByUserId").GetInt32().Should().Be(memberId);
        var id = created.GetProperty("id").GetInt32();

        (await ListAsync(member, slug)).Should().ContainSingle().Which.GetProperty("id").GetInt32().Should().Be(id);
        (await ListAsync(member, slug, "from=2026-04-01&to=2026-04-30")).Should().BeEmpty();

        // All day: no times
        var updated = await member.PutAsJsonAsync($"{Url(slug)}/{id}", new { date = "2026-03-04", note = "Umzug" });
        updated.StatusCode.Should().Be(HttpStatusCode.OK);
        var body = await updated.Content.ReadFromJsonAsync<JsonElement>();
        body.GetProperty("allDay").GetBoolean().Should().BeTrue();
        body.GetProperty("startTime").ValueKind.Should().Be(JsonValueKind.Null);
        body.GetProperty("date").GetString().Should().Be("2026-03-04");

        foreach (var (payload, code) in new (object, string)[]
                 {
                     (new { date = "2026-03-03", startTime = "12:00", endTime = "10:00" }, "appointment.times"),
                     (new { date = "2026-03-03", startTime = "12:00", endTime = "12:00" }, "appointment.times"),
                     (new { date = "2026-03-03", startTime = "12:00" }, "appointment.times"),
                     (new { date = "2026-03-03", startTime = "9", endTime = "10:00" }, "appointment.times"),
                     (new { date = "2026-03-03", note = new string('x', 201) }, "appointment.noteTooLong"),
                     (new { date = "1999-12-31" }, "appointment.date")
                 })
        {
            var bad = await member.PostAsJsonAsync(Url(slug), payload);
            bad.StatusCode.Should().Be(HttpStatusCode.BadRequest);
            (await CodeAsync(bad)).Should().Be(code);
        }
        (await member.PostAsJsonAsync(Url(slug), new { date = "2026-03-03", note = new string('x', 200) })).StatusCode.Should().Be(HttpStatusCode.Created);
        (await member.GetAsync($"{Url(slug)}?from=2020-01-01&to=2026-01-01")).StatusCode.Should().Be(HttpStatusCode.BadRequest);

        (await member.DeleteAsync($"{Url(slug)}/{id}")).StatusCode.Should().Be(HttpStatusCode.NoContent);
        var gone = await member.DeleteAsync($"{Url(slug)}/{id}");
        gone.StatusCode.Should().Be(HttpStatusCode.NotFound);
        (await CodeAsync(gone)).Should().Be("appointment.notFound");
    }

    [Fact]
    public async Task PastAppointments_InClosedMonths_StayEditable_WithoutAReason()
    {
        var (owner, member, slug, _, _) = await TestHelpers.OrgWithMemberAsync(_factory, "appt-closed", new { });
        (await owner.PostAsJsonAsync($"/api/v1/organizations/{slug}/closed-periods", new { from = "2026-01-01", to = "2026-01-31" })).EnsureSuccessStatusCode();

        var id = (await CreatedAsync(await member.PostAsJsonAsync(Url(slug), new { date = "2026-01-15", startTime = "08:00", endTime = "09:00" })))
            .GetProperty("id").GetInt32();
        (await member.PutAsJsonAsync($"{Url(slug)}/{id}", new { date = "2026-01-16", startTime = "08:00", endTime = "10:00" })).StatusCode.Should().Be(HttpStatusCode.OK);
        (await member.DeleteAsync($"{Url(slug)}/{id}")).StatusCode.Should().Be(HttpStatusCode.NoContent);
    }

    // ── Authorization ──────────────────────────────────────────────────

    [Fact]
    public async Task OnlyTheOwner_Admins_AndAbsenceApprovingLeads_ManageAppointments()
    {
        var (owner, member, slug, _, memberId) = await TestHelpers.OrgWithMemberAsync(_factory, "appt-auth", new { });
        var (colleague, colleagueId) = await JoinAsync(owner, slug, "appt-auth-c@t.test");
        var (lead, leadId) = await JoinAsync(owner, slug, "appt-auth-lead@t.test");
        var (viewer, viewerId) = await JoinAsync(owner, slug, "appt-auth-viewer@t.test");
        await TestHelpers.CreateGroupAsync(owner, slug, "Team", new { groupAdminCanApproveAbsences = true }, (memberId, "Member"), (leadId, "Admin"));
        await TestHelpers.CreateGroupAsync(owner, slug, "Viewers", new { groupAdminCanApproveAbsences = false }, (memberId, "Member"), (viewerId, "Admin"));

        // A member cannot add one for a colleague
        var forOther = await member.PostAsJsonAsync(Url(slug), new { date = "2026-03-03", userId = colleagueId });
        forOther.StatusCode.Should().Be(HttpStatusCode.Forbidden);
        (await CodeAsync(forOther)).Should().Be("appointment.forbidden");
        (await member.PostAsJsonAsync(Url(slug), new { date = "2026-03-03", userId = 999999 })).StatusCode.Should().Be(HttpStatusCode.NotFound);

        // The owner adds one for the member, who may change it
        var byOwner = await CreatedAsync(await owner.PostAsJsonAsync(Url(slug), new { date = "2026-03-03", startTime = "10:00", endTime = "11:00", userId = memberId }));
        byOwner.GetProperty("userId").GetInt32().Should().Be(memberId);
        var id = byOwner.GetProperty("id").GetInt32();
        (await ListAsync(member, slug)).Single().GetProperty("canEdit").GetBoolean().Should().BeTrue();
        (await member.PutAsJsonAsync($"{Url(slug)}/{id}", new { date = "2026-03-03", startTime = "10:00", endTime = "12:00" })).StatusCode.Should().Be(HttpStatusCode.OK);

        // A colleague sees it but may not touch it; a lead approving the member's absences may, one without that right not
        (await ListAsync(colleague, slug)).Single().GetProperty("canEdit").GetBoolean().Should().BeFalse();
        (await colleague.PutAsJsonAsync($"{Url(slug)}/{id}", new { date = "2026-03-03" })).StatusCode.Should().Be(HttpStatusCode.Forbidden);
        (await colleague.DeleteAsync($"{Url(slug)}/{id}")).StatusCode.Should().Be(HttpStatusCode.Forbidden);
        (await viewer.PutAsJsonAsync($"{Url(slug)}/{id}", new { date = "2026-03-03" })).StatusCode.Should().Be(HttpStatusCode.Forbidden);
        (await ListAsync(lead, slug)).Single().GetProperty("canEdit").GetBoolean().Should().BeTrue();
        (await lead.PutAsJsonAsync($"{Url(slug)}/{id}", new { date = "2026-03-05", startTime = "07:00", endTime = "08:00" })).StatusCode.Should().Be(HttpStatusCode.OK);
        (await CreatedAsync(await lead.PostAsJsonAsync(Url(slug), new { date = "2026-03-06", userId = memberId }))).GetProperty("createdByUserId").GetInt32().Should().Be(leadId);
        (await lead.PostAsJsonAsync(Url(slug), new { date = "2026-03-06", userId = colleagueId })).StatusCode.Should().Be(HttpStatusCode.Forbidden);

        // Hidden people: 404 for everything, never 403
        (await owner.PutAsJsonAsync($"/api/v1/Organizations/{slug}/settings", new { visibility = TestHelpers.Matrix(("Member", false, false)) })).EnsureSuccessStatusCode();
        (await colleague.PostAsJsonAsync(Url(slug), new { date = "2026-03-03", userId = memberId })).StatusCode.Should().Be(HttpStatusCode.NotFound);
        var hidden = await colleague.DeleteAsync($"{Url(slug)}/{id}");
        hidden.StatusCode.Should().Be(HttpStatusCode.NotFound);
        (await CodeAsync(hidden)).Should().Be("appointment.notFound");
        (await colleague.GetAsync($"{Url(slug)}?from=2026-03-01&to=2026-03-31&userId={memberId}")).StatusCode.Should().Be(HttpStatusCode.NotFound);
        (await ListAsync(colleague, slug)).Should().BeEmpty();
        (await ListAsync(lead, slug)).Should().HaveCount(2, "a lead always sees their members");

        // Other organizations' appointments are not reachable
        var (other, _, otherSlug, _, _) = await TestHelpers.OrgWithMemberAsync(_factory, "appt-auth-x", new { });
        (await other.DeleteAsync($"{Url(otherSlug)}/{id}")).StatusCode.Should().Be(HttpStatusCode.NotFound);
        (await other.GetAsync($"{Url(slug)}?from=2026-03-01&to=2026-03-31")).StatusCode.Should().Be(HttpStatusCode.Forbidden);
    }

    // ── Visibility ─────────────────────────────────────────────────────

    [Fact]
    public async Task Visibility_SlotAndNoteAreSeparateKinds_GroupOverridesApply()
    {
        var (owner, member, slug, _, memberId) = await TestHelpers.OrgWithMemberAsync(_factory, "appt-vis", new { });
        var (colleague, colleagueId) = await JoinAsync(owner, slug, "appt-vis-c@t.test");
        await CreatedAsync(await member.PostAsJsonAsync(Url(slug), new { date = "2026-03-10", startTime = "10:00", endTime = "12:00", note = "Arzt" }));
        await CreatedAsync(await colleague.PostAsJsonAsync(Url(slug), new { date = "2026-03-11", note = "Seminar" }));

        // New organizations: the slot is visible, the note is not
        var detail = await owner.GetFromJsonAsync<JsonElement>($"/api/v1/Organizations/{slug}");
        var rows = detail.GetProperty("visibility").EnumerateArray().ToDictionary(r => r.GetProperty("kind").GetString()!);
        rows["Appointments"].GetProperty("inGroups").GetBoolean().Should().BeTrue();
        rows["Appointments"].GetProperty("outsideGroups").GetBoolean().Should().BeTrue();
        rows["AppointmentNotes"].GetProperty("inGroups").GetBoolean().Should().BeFalse();

        var seen = (await ListAsync(colleague, slug)).Single(a => a.GetProperty("userId").GetInt32() == memberId);
        seen.GetProperty("startTime").GetString().Should().Be("10:00");
        seen.GetProperty("note").ValueKind.Should().Be(JsonValueKind.Null);
        (await ListAsync(owner, slug)).Single(a => a.GetProperty("userId").GetInt32() == memberId).GetProperty("note").GetString().Should().Be("Arzt");
        (await ListAsync(member, slug)).Single(a => a.GetProperty("userId").GetInt32() == memberId).GetProperty("note").GetString().Should().Be("Arzt");

        // The org shows the notes
        (await owner.PutAsJsonAsync($"/api/v1/Organizations/{slug}/settings", new { visibility = TestHelpers.Matrix(("AppointmentNotes", true, true)) })).EnsureSuccessStatusCode();
        (await ListAsync(colleague, slug)).Single(a => a.GetProperty("userId").GetInt32() == memberId).GetProperty("note").GetString().Should().Be("Arzt");

        // The member's group hides their appointments from everyone outside it (the data owner's groups decide)
        var team = await TestHelpers.CreateGroupAsync(owner, slug, "Team",
            new { visibility = TestHelpers.Matrix(("Appointments", null, false)) }, (memberId, "Member"));
        (await ListAsync(colleague, slug)).Should().OnlyContain(a => a.GetProperty("userId").GetInt32() == colleagueId);
        (await ListAsync(owner, slug)).Should().HaveCount(2, "admins see all");

        // The group filter takes the group and its sub-groups; unknown groups are 404
        (await ListAsync(owner, slug, $"from=2026-03-01&to=2026-03-31&groupId={team}")).Should().ContainSingle()
            .Which.GetProperty("userId").GetInt32().Should().Be(memberId);
        (await owner.GetAsync($"{Url(slug)}?from=2026-03-01&to=2026-03-31&groupId=999999")).StatusCode.Should().Be(HttpStatusCode.NotFound);

        // Off for the org: the colleague's own still shows to themselves only
        (await owner.PutAsJsonAsync($"/api/v1/Organizations/{slug}/settings", new { visibility = TestHelpers.Matrix(("Appointments", false, false)) })).EnsureSuccessStatusCode();
        (await ListAsync(member, slug)).Should().OnlyContain(a => a.GetProperty("userId").GetInt32() == memberId);
        var memberInfo = (await member.GetFromJsonAsync<JsonElement>($"/api/v1/Organizations/{slug}")).GetProperty("members").EnumerateArray()
            .Single(m => m.GetProperty("id").GetInt32() == colleagueId).GetProperty("visibleInfo").EnumerateArray().Select(k => k.GetString());
        memberInfo.Should().NotContain("Appointments");
    }

    // ── Not counted anywhere ───────────────────────────────────────────

    [Fact]
    public async Task Appointments_DoNotChangeBalanceOrReports_AndAreInThePersonalExport()
    {
        var (owner, member, slug, orgId, memberId) = await TestHelpers.OrgWithMemberAsync(_factory, "appt-count", new { memberTimeEntryVisibility = true });
        var lastMonth = Berlin.Today().AddMonths(-1);
        var balanceUrl = $"/api/v1/organizations/{slug}/members/{memberId}/balance";
        var summaryUrl = $"/api/v1/organizations/{slug}/accounting/summary?year={lastMonth.Year}&month={lastMonth.Month}";
        var balanceBefore = await owner.GetStringAsync(balanceUrl);
        var summaryBefore = await owner.GetStringAsync(summaryUrl);

        var day = new DateOnly(lastMonth.Year, lastMonth.Month, 10);
        await CreatedAsync(await member.PostAsJsonAsync(Url(slug), new { date = day.ToString("yyyy-MM-dd"), startTime = "08:00", endTime = "16:00", note = "Messe" }));
        await CreatedAsync(await member.PostAsJsonAsync(Url(slug), new { date = Berlin.Today().ToString("yyyy-MM-dd") }));

        (await owner.GetStringAsync(balanceUrl)).Should().Be(balanceBefore);
        (await owner.GetStringAsync(summaryUrl)).Should().Be(summaryBefore);

        var export = await member.GetFromJsonAsync<JsonElement>("/api/v1/Auth/me/export");
        export.GetProperty("appointments").EnumerateArray().Select(a => a.GetProperty("note").GetString()).Should().Contain("Messe");

        // Leaving the organization removes them; so does deleting the account
        var (other, otherLogin) = await TestHelpers.CreateAuthenticatedUserAsync(_factory, "appt-count-del@t.test");
        (await TestHelpers.AddMemberAsync(_factory, owner, slug, otherLogin)).EnsureSuccessStatusCode();
        await CreatedAsync(await other.PostAsJsonAsync(Url(slug), new { date = "2026-03-03" }));
        (await other.PostAsJsonAsync("/api/v1/Auth/account/delete", new { password = "Password123!" })).EnsureSuccessStatusCode();
        (await owner.DeleteAsync($"/api/v1/Organizations/{slug}/members/{memberId}")).EnsureSuccessStatusCode();
        using var scope = _factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<TimeTrackingDbContext>();
        (await db.Appointments.CountAsync(a => a.OrganizationId == orgId)).Should().Be(0);
    }

    // ── Core hours ─────────────────────────────────────────────────────

    private static readonly object CoreSettings = new
    {
        coreHoursEnabled = true, coreHoursStart = "09:00", coreHoursEnd = "15:00", coreHoursToleranceMinutes = 5, timeZone = "Europe/Berlin",
        memberTimeEntryVisibility = true
    };

    private async Task WithDbAsync(Func<TimeTrackingDbContext, Task> action)
    {
        using var scope = _factory.Services.CreateScope();
        await action(scope.ServiceProvider.GetRequiredService<TimeTrackingDbContext>());
    }

    private static DateTime At(DateOnly day, int hour, int minute = 0) => day.ToDateTime(new TimeOnly(hour, minute));

    private Task AddSpanAsync(int userId, int orgId, DateTime fromLocal, DateTime toLocal) =>
        WithDbAsync(async db =>
        {
            var start = Berlin.ToUtc(fromLocal);
            db.TimeEntries.Add(new TimeEntry
            {
                UserId = userId, OrganizationId = orgId, StartTime = start, EndTime = Berlin.ToUtc(toLocal), CreatedAt = start, UpdatedAt = start
            });
            await db.SaveChangesAsync();
        });

    private Task AddAppointmentAsync(int userId, int orgId, DateOnly day, int? from = null, int? to = null) =>
        WithDbAsync(async db =>
        {
            db.Appointments.Add(new Appointment
            {
                UserId = userId, OrganizationId = orgId, Date = day, CreatedAt = DateTime.UtcNow,
                StartTime = from is int f ? new TimeOnly(f / 100, f % 100) : null, EndTime = to is int t ? new TimeOnly(t / 100, t % 100) : null
            });
            await db.SaveChangesAsync();
        });

    /// <summary>Core hours and memberships long in place, every day a working day.</summary>
    private Task BackdateAsync(int orgId) =>
        WithDbAsync(async db =>
        {
            (await db.Organizations.SingleAsync(o => o.Id == orgId)).CoreHoursChangedAt = null;
            foreach (var m in await db.UserOrganizations.Where(uo => uo.OrganizationId == orgId).ToListAsync())
            {
                m.JoinedAt = DateTime.UtcNow.AddDays(-60);
                db.WorkSchedules.Add(new WorkSchedule
                {
                    OrganizationId = orgId, UserId = m.UserId, ValidFrom = new DateOnly(2026, 1, 1),
                    TargetMon = 8, TargetTue = 8, TargetWed = 8, TargetThu = 8, TargetFri = 8, TargetSat = 8, TargetSun = 8
                });
            }
            await db.SaveChangesAsync();
        });

    private async Task CheckAsync(DateTime nowUtc) =>
        await WithDbAsync(async db => await CoreHours.CheckDueAsync(db, nowUtc, _factory.Mail, "https://app.test"));

    private async Task<CoreHoursCheck?> RowAsync(int userId, DateOnly day)
    {
        CoreHoursCheck? row = null;
        await WithDbAsync(async db => row = await db.CoreHoursChecks.AsNoTracking().FirstOrDefaultAsync(c => c.UserId == userId && c.Date == day));
        return row;
    }

    private async Task<List<Notification>> NoticesAsync(int memberId, DateOnly day)
    {
        var date = day.ToString("dd.MM.yyyy");
        List<Notification> list = [];
        await WithDbAsync(async db => list = await db.Notifications.AsNoTracking()
            .Where(n => n.Type == CoreHours.NotificationType && n.RelatedEntityId == memberId && n.Message.Contains(date)).ToListAsync());
        return list;
    }

    [Theory]
    [InlineData(false)]   // checked on time, right after the window
    [InlineData(true)]    // caught up two days later (checked late)
    public async Task Appointments_CountAsCovered_AndAnAllDayOneExemptsTheDay(bool catchUp)
    {
        var prefix = catchUp ? "appt-core-late" : "appt-core";
        var (owner, _, slug, orgId, covered) = await TestHelpers.OrgWithMemberAsync(_factory, prefix, CoreSettings);
        var (_, partial) = await JoinAsync(owner, slug, $"{prefix}-p@t.test");
        var (_, allDay) = await JoinAsync(owner, slug, $"{prefix}-a@t.test");
        var (_, nothing) = await JoinAsync(owner, slug, $"{prefix}-n@t.test");
        var day = Berlin.Today().AddDays(-5);
        await BackdateAsync(orgId);

        await AddAppointmentAsync(covered, orgId, day, 900, 1100);   // late start covered by the doctor's appointment
        await AddSpanAsync(covered, orgId, At(day, 11), At(day, 15));
        await AddAppointmentAsync(partial, orgId, day, 1000, 1200);  // covers part of a gap, leaves early anyway
        await AddSpanAsync(partial, orgId, At(day, 9), At(day, 10));
        await AddSpanAsync(partial, orgId, At(day, 12, 30), At(day, 14));
        await AddAppointmentAsync(allDay, orgId, day);
        await AddAppointmentAsync(nothing, orgId, day.AddDays(1));   // another day does not help

        await CheckAsync(catchUp ? Berlin.ToUtc(At(day.AddDays(2), 10)) : Berlin.ToUtc(At(day, 15, 30)));

        (await RowAsync(covered, day))!.Issues.Should().Be(CoreHoursIssue.None);
        (await RowAsync(allDay, day))!.Issues.Should().Be(CoreHoursIssue.None);
        (await RowAsync(nothing, day))!.Issues.Should().Be(CoreHoursIssue.Missing);
        var row = (await RowAsync(partial, day))!;
        row.Issues.Should().Be(CoreHoursIssue.Gap | CoreHoursIssue.Early);
        row.MissingMinutes.Should().Be(90);
        row.CheckedLate.Should().Be(catchUp);
        (await NoticesAsync(covered, day)).Should().BeEmpty();
        (await NoticesAsync(partial, day)).Should().NotBeEmpty()
            .And.OnlyContain(n => n.Message.Contains("Appointment 10:00-12:00 considered."));
        _factory.Mail.Sent.Should().Contain(m => m.To == $"{prefix}-p@t.test" && m.Body.Contains("Termin 10:00-12:00 berücksichtigt."));

        // The compliance list names the appointment; adding one for the rest clears the day
        var compliance = $"/api/v1/organizations/{slug}/members/{partial}/compliance?from={day:yyyy-MM-dd}&to={day:yyyy-MM-dd}";
        var flagged = (await owner.GetFromJsonAsync<List<JsonElement>>(compliance))!.Single();
        flagged.GetProperty("coreHoursAppointments").EnumerateArray().Select(a => a.GetString()).Should().Equal("10:00-12:00");
        (await owner.PostAsJsonAsync(Url(slug), new { date = day.ToString("yyyy-MM-dd"), startTime = "12:00", endTime = "15:00", userId = partial }))
            .StatusCode.Should().Be(HttpStatusCode.Created);
        (await owner.GetFromJsonAsync<List<JsonElement>>(compliance)).Should().BeEmpty();
        var nothingCompliance = $"/api/v1/organizations/{slug}/members/{nothing}/compliance?from={day:yyyy-MM-dd}&to={day:yyyy-MM-dd}";
        (await owner.GetFromJsonAsync<List<JsonElement>>(nothingCompliance))!.Single().GetProperty("coreHoursAppointments").ValueKind
            .Should().Be(JsonValueKind.Null);
    }

    [Fact]
    public async Task Backfill_CountsAppointments()
    {
        var (owner, member, slug, orgId, memberId) = await TestHelpers.OrgWithMemberAsync(_factory, "appt-backfill",
            new { timeZone = "Europe/Berlin", memberTimeEntryVisibility = true });
        await BackdateAsync(orgId);
        var today = Berlin.Today();
        await AddAppointmentAsync(memberId, orgId, today.AddDays(-2));
        await AddAppointmentAsync(memberId, orgId, today.AddDays(-3), 900, 1200);
        await AddSpanAsync(memberId, orgId, At(today.AddDays(-3), 12), At(today.AddDays(-3), 15));

        (await owner.PutAsJsonAsync($"/api/v1/Organizations/{slug}/settings",
            new { coreHoursEnabled = true, coreHoursStart = "09:00", coreHoursEnd = "15:00", coreHoursBackfillDays = 3 })).EnsureSuccessStatusCode();
        await CheckAsync(DateTime.UtcNow);

        (await RowAsync(memberId, today.AddDays(-1)))!.Issues.Should().Be(CoreHoursIssue.Missing);
        var exempt = (await RowAsync(memberId, today.AddDays(-2)))!;
        exempt.Backfilled.Should().BeTrue();
        exempt.Issues.Should().Be(CoreHoursIssue.None);
        (await RowAsync(memberId, today.AddDays(-3)))!.Issues.Should().Be(CoreHoursIssue.None);
    }

    [Fact]
    public async Task NightWindow_AppointmentsOnBothDaysCover_AnAllDayOneOnTheStartDayExempts()
    {
        var (owner, _, slug, orgId, split) = await TestHelpers.OrgWithMemberAsync(_factory, "appt-night",
            new { coreHoursEnabled = true, coreHoursStart = "22:00", coreHoursEnd = "02:00", coreHoursToleranceMinutes = 5, timeZone = "Europe/Berlin" });
        var (_, startDay) = await JoinAsync(owner, slug, "appt-night-s@t.test");
        var (_, nextDay) = await JoinAsync(owner, slug, "appt-night-x@t.test");
        var day = Berlin.Today().AddDays(-6);
        await BackdateAsync(orgId);

        await AddAppointmentAsync(split, orgId, day, 2200, 2359);
        await AddAppointmentAsync(split, orgId, day.AddDays(1), 0, 200);
        await AddAppointmentAsync(startDay, orgId, day);
        await AddAppointmentAsync(nextDay, orgId, day.AddDays(1));   // covers 00:00-02:00 only

        await CheckAsync(Berlin.ToUtc(At(day.AddDays(1), 2, 10)));
        (await RowAsync(split, day))!.Issues.Should().Be(CoreHoursIssue.None);
        (await RowAsync(startDay, day))!.Issues.Should().Be(CoreHoursIssue.None);
        var late = (await RowAsync(nextDay, day))!;
        late.Issues.Should().Be(CoreHoursIssue.Late);
        late.MissingMinutes.Should().Be(120);
    }
}
