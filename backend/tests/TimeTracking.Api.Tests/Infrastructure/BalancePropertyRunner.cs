using System.Net.Http.Json;
using System.Text.Json;
using FluentAssertions;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using TimeTracking.Api.Data;
using TimeTracking.Api.Models;
using TimeTracking.Api.Services;
using Xunit.Abstractions;

namespace TimeTracking.Api.Tests.Infrastructure;

/// <summary>
/// The property test of the balance checkpoints (Phase 2 "Correctness net" of docs/PERFORMANCE_PLAN.md): a deterministic
/// random sequence of balance-input changes over ~2 years for three members of one organization (one of them also in a
/// second organization). After every operation (1) every valid stored checkpoint must equal the from-scratch reference, then
/// (2) a random selection of the real consumers — balance endpoint (service + HTTP as the member), team overview,
/// <c>BalancesAsync</c>, payroll rows and <c>TotalsAsync</c> for an ended month, <c>BuildWithRunningTotalAsync</c> — reads (and so
/// writes checkpoints) and must equal the reference (exactly to 1e-9 where unrounded, to the cent where the API rounds), then
/// (1) again. Data changes run in app scopes acting as the owner (RLS on Postgres; closed months with an
/// <c>X-Change-Reason</c>, sometimes without one = refused), timer stops as the member, auto-stops as system work; org-wide
/// changes (holidays, absence types, time zone, initial overtime, closing months) over HTTP as the owner.
/// </summary>
public sealed class BalancePropertyRunner
{
    private static readonly string[] Zones = ["Europe/Berlin", "America/New_York", "Asia/Tokyo", "Pacific/Auckland", "Europe/London"];

    private readonly BalanceHarness _h;
    private readonly int _seed;
    private readonly string _prefix;
    private readonly Random _rng;
    private readonly ITestOutputHelper? _out;
    private readonly List<string> _log = [];

    private HttpClient _owner = null!;
    private int _ownerId;
    private readonly Dictionary<int, string> _slugs = [];
    private readonly Dictionary<int, HttpClient> _members = [];
    private int _orgA, _orgB;
    private List<(int User, int Org)> _pairs = [];
    private readonly Dictionary<int, List<int>> _types = [];   // org -> custom type ids (incl. deleted ones)
    private int _typeCounter;

    public int ValidRowsChecked { get; private set; }
    public int MaxValidRows { get; private set; }
    public int Refused { get; private set; }
    public int Bumps { get; private set; }

    public BalancePropertyRunner(WebApplicationFactory<Program> factory, int seed, string prefix, ITestOutputHelper? output = null)
    {
        _h = new BalanceHarness(factory);
        _seed = seed;
        _prefix = prefix;
        _rng = new Random(seed);
        _out = output;
    }

    private string Context => $"seed {_seed}, ops:\n  " + string.Join("\n  ", _log.TakeLast(12));

    // ── Setup ──

    public async Task SetupAsync()
    {
        var (owner, ownerLogin) = await TestHelpers.CreateAuthenticatedUserAsync(_h.Factory, $"{_prefix}-owner@bcp.test");
        _owner = owner;
        _ownerId = ownerLogin.User.Id;
        var a = await TestHelpers.CreateOrganizationAsync(owner, $"{_prefix} A", $"{_prefix}-a");
        var b = await TestHelpers.CreateOrganizationAsync(owner, $"{_prefix} B", $"{_prefix}-b");
        (_orgA, _orgB) = (a.Id, b.Id);
        _slugs[a.Id] = a.Slug;
        _slugs[b.Id] = b.Slug;
        foreach (var slug in _slugs.Values)
            (await owner.PutAsJsonAsync($"/api/v1/Organizations/{slug}/settings",
                new { memberTimeEntryVisibility = true, timeZone = "Europe/Berlin", businessDayEnd = "00:00" })).EnsureSuccessStatusCode();
        await _h.SkipPrewarmAsync(_orgA, _orgB);

        var ids = new List<int>();
        LoginResponseDto last = null!;
        for (var i = 1; i <= 3; i++)
        {
            var (client, login) = await TestHelpers.CreateAuthenticatedUserAsync(_h.Factory, $"{_prefix}-m{i}@bcp.test");
            (await TestHelpers.AddMemberAsync(_h.Factory, owner, a.Slug, login)).EnsureSuccessStatusCode();
            _members[login.User.Id] = client;
            ids.Add(login.User.Id);
            last = login;
        }
        (await TestHelpers.AddMemberAsync(_h.Factory, owner, b.Slug, last)).EnsureSuccessStatusCode();
        _pairs = [(ids[0], _orgA), (ids[1], _orgA), (ids[2], _orgA), (ids[2], _orgB)];

        // History (system work): schedules from ~25 months back, entries on ~40 % of the working days, some absences
        await using var scope = _h.Scope();
        var db = BalanceHarness.Db(scope);
        var clock = OrgClock.For("Europe/Berlin");
        var today = clock.Today();
        var start = today.AddDays(-760);
        foreach (var (user, org) in _pairs)
        {
            var part = org == _orgB;
            db.WorkSchedules.Add(new WorkSchedule
            {
                UserId = user, OrganizationId = org, ValidFrom = start, WeeklyWorkHours = part ? 12 : 40,
                TargetMon = part ? 4 : 8, TargetTue = part ? 4 : 8, TargetWed = part ? 4 : 8, TargetThu = part ? 0 : 8, TargetFri = part ? 0 : 8
            });
            var firstDay = start.AddDays(_rng.Next(0, 60));
            for (var d = firstDay; d < today; d = d.AddDays(1))
            {
                if (d.DayOfWeek is DayOfWeek.Saturday or DayOfWeek.Sunday) continue;
                var roll = _rng.NextDouble();
                if (roll < 0.4)
                {
                    var s = clock.ToUtc(d.ToDateTime(new TimeOnly(7, 0)).AddMinutes(_rng.Next(0, 120)));
                    db.TimeEntries.Add(new TimeEntry
                    {
                        UserId = user, OrganizationId = org, StartTime = s, EndTime = s.AddMinutes(part ? 200 + _rng.Next(60) : 420 + _rng.Next(120)),
                        PauseDurationMinutes = part ? 0 : 30, PauseIsManual = true, Source = TimeEntrySource.Manual, CreatedAt = s, UpdatedAt = s
                    });
                }
                else if (roll < 0.43)
                    db.AbsenceDays.Add(new AbsenceDay
                    {
                        UserId = user, OrganizationId = org, Date = d, Type = _rng.Next(2) == 0 ? AbsenceType.Vacation : AbsenceType.SickDay,
                        IsHalfDay = _rng.Next(4) == 0
                    });
            }
        }
        db.Holidays.Add(new Holiday { OrganizationId = _orgA, Name = "Neujahr", Date = new DateOnly(2020, 1, 1), IsRecurring = true });
        db.Holidays.Add(new Holiday { OrganizationId = _orgA, Name = "Einmalig", Date = today.AddDays(-200) });
        await db.SaveChangesAsync();
        _types[_orgA] = [];
        _types[_orgB] = [];
        Log("setup");
        await VerifyAsync("after setup");
    }

    // ── Run ──

    private sealed record Op(string Name, int Weight, Func<Task<string>> Run);

    public async Task RunAsync(int count)
    {
        var ops = new List<Op>
        {
            new("add entry", 14, AddEntryAsync),
            new("edit entry", 12, EditEntryAsync),
            new("move entry between orgs", 4, MoveEntryOrgAsync),
            new("delete entry", 6, DeleteEntryAsync),
            new("timer across a month end", 5, TimerAcrossMonthEndAsync),
            new("timer now", 3, TimerNowAsync),
            new("auto-stop failure", 4, FailureCloseAsync),
            new("correct failure", 2, CorrectFailureAsync),
            new("add absence", 8, AddAbsenceAsync),
            new("edit absence", 4, EditAbsenceAsync),
            new("delete absence", 3, DeleteAbsenceAsync),
            new("create absence type", 2, CreateTypeAsync),
            new("change credit mode", 3, ChangeCreditModeAsync),
            new("delete absence type", 1, DeleteTypeAsync),
            new("add schedule period", 3, AddScheduleAsync),
            new("change schedule targets", 3, ScheduleTargetsAsync),
            new("change schedule ValidTo", 3, ScheduleValidToAsync),
            new("change schedule ValidFrom", 2, ScheduleValidFromAsync),
            new("delete schedule", 1, DeleteScheduleAsync),
            new("create holiday", 4, CreateHolidayAsync),
            new("edit holiday", 3, EditHolidayAsync),
            new("delete holiday", 2, DeleteHolidayAsync),
            new("time zone", 2, TimeZoneAsync),
            new("initial overtime", 2, InitialOvertimeAsync),
            new("close months", 3, CloseMonthsAsync),
            new("reopen month", 1, ReopenMonthAsync)
        };
        var total = ops.Sum(o => o.Weight);
        for (var i = 0; i < count; i++)
        {
            var pick = _rng.Next(total);
            var op = ops.First(o => (pick -= o.Weight) < 0);
            var before = await AllVersionsAsync();
            var detail = await op.Run();
            if (!(await AllVersionsAsync()).SequenceEqual(before)) Bumps++;
            Log($"#{i} {op.Name}: {detail}");
            await VerifyAsync($"after op #{i} ({op.Name})");
        }
        _out?.WriteLine($"seed {_seed}: {count} ops, {Bumps} with a version bump, {Refused} refused, {ValidRowsChecked} valid checkpoint checks (max {MaxValidRows} at once)");
    }

    private sealed class Refs : Dictionary<(int User, int Org), (BalanceHarness.Reference Ref, double Initial)>;

    private void Log(string line) => _log.Add(line);

    private async Task<List<int>> AllVersionsAsync()
    {
        await using var scope = _h.Scope();
        var db = BalanceHarness.Db(scope);
        var orgIds = new[] { _orgA, _orgB };
        var members = await db.UserOrganizations.IgnoreQueryFilters().AsNoTracking()
            .Where(uo => orgIds.Contains(uo.OrganizationId)).OrderBy(uo => uo.OrganizationId).ThenBy(uo => uo.UserId)
            .Select(uo => uo.BalanceVersion).ToListAsync();
        var orgs = await db.Organizations.IgnoreQueryFilters().AsNoTracking().Where(o => orgIds.Contains(o.Id)).OrderBy(o => o.Id)
            .Select(o => o.BalanceVersion).ToListAsync();
        return [.. members, .. orgs];
    }

    /// <summary>Invariant, then a random selection of consumers (each also writes checkpoints), then the invariant again.</summary>
    private async Task VerifyAsync(string because)
    {
        // The reads change no balance input: one reference for the checks and all reads
        var refs = await ReferencesAsync();
        await CheckStoredAsync(because + ", before the reads", refs);
        var consumers = new List<Func<Refs, Task>> { BalancesAsync, BalanceServiceAsync, TeamOverviewAsync, PayrollAsync, RunningTotalAsync, MemberHttpBalanceAsync };
        var chosen = consumers.OrderBy(_ => _rng.Next()).Take(_rng.Next(2, 5)).ToList();
        foreach (var consumer in chosen)
            await consumer(refs);
        await CheckStoredAsync(because + ", after the reads", refs);
    }

    private async Task CheckStoredAsync(string because, Refs? refs = null)
    {
        var n = await _h.AssertValidCheckpointsAsync([_orgA, _orgB], because + "\n" + Context,
            refs?.ToDictionary(r => (r.Key.Org, r.Key.User), r => r.Value.Ref));
        ValidRowsChecked += n;
        MaxValidRows = Math.Max(MaxValidRows, n);
    }

    private async Task<Refs> ReferencesAsync()
    {
        await using var scope = _h.Scope();
        var db = BalanceHarness.Db(scope);
        var result = new Refs();
        foreach (var (user, org) in _pairs)
        {
            var initial = await db.UserOrganizations.AsNoTracking().Where(uo => uo.UserId == user && uo.OrganizationId == org)
                .Select(uo => uo.InitialOvertimeHours).SingleAsync();
            result[(user, org)] = (await BalanceHarness.ReferenceAsync(db, user, org), initial);
        }
        return result;
    }

    // ── Consumers ──

    private async Task BalancesAsync(Refs refs)
    {
        foreach (var orgId in new[] { _orgA, _orgB })
        {
            using var _ = _h.As(_ownerId);
            await using var scope = _h.Scope();
            var db = BalanceHarness.Db(scope);
            var org = await db.Organizations.AsNoTracking().SingleAsync(o => o.Id == orgId);
            var pairs = refs.Where(r => r.Key.Org == orgId).ToList();
            var balances = await DailyReportAggregator.BalancesAsync(db, org, pairs.ToDictionary(p => p.Key.User, p => p.Value.Initial));
            foreach (var (key, (reference, initial)) in pairs)
                Math.Abs(balances[key.User].Balance - reference.Balance(initial)).Should().BeLessThanOrEqualTo(BalanceHarness.Exact,
                    $"BalancesAsync user {key.User} org {orgId}: got {balances[key.User].Balance:R}, reference {reference.Balance(initial):R}\n{Context}");
        }
    }

    private async Task BalanceServiceAsync(Refs refs)
    {
        foreach (var ((user, org), (reference, initial)) in refs)
        {
            using var _ = _h.As(_ownerId);
            await using var scope = _h.Scope();
            var result = await scope.ServiceProvider.GetRequiredService<IPdfReportService>().GetBalanceAsync(_slugs[org], _ownerId, user);
            result.Data.Should().NotBeNull(Context);
            result.Data!.CumulativeOvertimeHours.Should().Be(Math.Round(reference.Balance(initial), 2),
                $"balance endpoint user {user} org {org}\n{Context}");
        }
    }

    private async Task MemberHttpBalanceAsync(Refs refs)
    {
        var ((user, org), (reference, initial)) = refs.ElementAt(_rng.Next(refs.Count));
        var balance = await _members[user].GetFromJsonAsync<JsonElement>($"/api/v1/organizations/{_slugs[org]}/members/{user}/balance");
        balance.GetProperty("cumulativeOvertimeHours").GetDouble().Should().Be(Math.Round(reference.Balance(initial), 2),
            $"HTTP balance as the member {user} org {org}\n{Context}");
    }

    private async Task TeamOverviewAsync(Refs refs)
    {
        foreach (var orgId in new[] { _orgA, _orgB })
        {
            using var _ = _h.As(_ownerId);
            await using var scope = _h.Scope();
            var overview = (await scope.ServiceProvider.GetRequiredService<IOrganizationService>().GetTeamOverviewAsync(_slugs[orgId], _ownerId)).Data;
            overview.Should().NotBeNull(Context);
            foreach (var ((user, org), (reference, initial)) in refs.Where(r => r.Key.Org == orgId))
                overview!.Members.Single(m => m.UserId == user).BalanceHours.Should().Be(Math.Round(reference.Balance(initial), 2),
                    $"team overview user {user} org {org}\n{Context}");
        }
    }

    /// <summary>Payroll rows (rounded) and <c>TotalsAsync</c> (exact) for a random ended month of the last two years.</summary>
    private async Task PayrollAsync(Refs refs)
    {
        foreach (var orgId in new[] { _orgA, _orgB })
        {
            using var _ = _h.As(_ownerId);
            await using var scope = _h.Scope();
            var db = BalanceHarness.Db(scope);
            var org = await db.Organizations.AsNoTracking().SingleAsync(o => o.Id == orgId);
            var today = OrgClock.For(org).Today();
            var from = BalanceHarness.MonthOf(today).AddMonths(-_rng.Next(1, 25));
            var to = from.AddMonths(1).AddDays(-1);
            var pairs = refs.Where(r => r.Key.Org == orgId).ToList();

            var totals = await DailyReportAggregator.TotalsAsync(db, org, pairs.ToDictionary(p => p.Key.User, p => p.Value.Initial), from, to,
                failureCounts: false);
            var rows = await scope.ServiceProvider.GetRequiredService<PayrollExportService>().RowsAsync(org, from, to);
            foreach (var ((user, _), (reference, initial)) in pairs)
            {
                var period = reference.Rows.Where(r => r.Date >= from && r.Date <= to).ToList();
                var balance = period.Count == 0 && (reference.FirstDay == null || reference.FirstDay > to) ? initial : initial + reference.SumAt(to);
                var what = $"month {from:yyyy-MM} user {user} org {orgId}";
                var t = totals[user];
                Math.Abs(t.Balance - balance).Should().BeLessThanOrEqualTo(BalanceHarness.Exact, $"TotalsAsync balance {what}: {t.Balance:R} vs {balance:R}\n{Context}");
                Math.Abs(t.Target - period.Sum(r => r.TargetHours)).Should().BeLessThanOrEqualTo(BalanceHarness.Exact, $"TotalsAsync target {what}\n{Context}");
                Math.Abs(t.Worked - period.Sum(r => r.NetWorkedHours)).Should().BeLessThanOrEqualTo(BalanceHarness.Exact, $"TotalsAsync worked {what}\n{Context}");
                Math.Abs(t.Overtime - period.Sum(r => r.Overtime)).Should().BeLessThanOrEqualTo(BalanceHarness.Exact, $"TotalsAsync overtime {what}\n{Context}");
                var row = rows.Single(r => r.UserId == user);
                row.OvertimeBalance.Should().Be(Math.Round(balance, 2), $"payroll balance {what}\n{Context}");
                row.OvertimeHours.Should().Be(Math.Round(period.Sum(r => r.Overtime), 2), $"payroll overtime {what}\n{Context}");
            }
        }
    }

    /// <summary><c>BuildWithRunningTotalAsync</c> (PDFs, daily CSV) from a random past day: every row exact.</summary>
    private async Task RunningTotalAsync(Refs refs)
    {
        foreach (var ((user, org), (reference, initial)) in refs)
        {
            using var _ = _h.As(_ownerId);
            await using var scope = _h.Scope();
            var db = BalanceHarness.Db(scope);
            var today = reference.Today;
            var from = _rng.Next(4) == 0 ? BalanceHarness.MonthOf(today) : today.AddDays(-_rng.Next(1, 760));
            var (rows, overall) = await DailyReportAggregator.BuildWithRunningTotalAsync(db, user, org, from, today, initial);
            var what = $"BuildWithRunningTotalAsync user {user} org {org} from {from}";
            Math.Abs(overall - reference.Balance(initial)).Should().BeLessThanOrEqualTo(BalanceHarness.Exact,
                $"{what}: overall {overall:R} vs {reference.Balance(initial):R}\n{Context}");
            rows.Select(r => r.Date).Should().Equal(reference.Rows.Where(r => r.Date >= from).Select(r => r.Date), $"{what}: days\n{Context}");
            foreach (var r in rows)
                Math.Abs(r.CumulativeOvertime - (initial + reference.Row(r.Date)!.CumulativeOvertime)).Should().BeLessThanOrEqualTo(BalanceHarness.Exact,
                    $"{what}: row {r.Date}\n{Context}");
        }
    }

    // ── Picking ──

    private (int User, int Org) Pair() => _pairs[_rng.Next(_pairs.Count)];

    private async Task<OrgClock> ClockAsync(int orgId)
    {
        await using var scope = _h.Scope();
        return OrgClock.For(await BalanceHarness.Db(scope).Organizations.AsNoTracking().Where(o => o.Id == orgId).Select(o => o.TimeZone).SingleAsync());
    }

    /// <summary>Mostly a day of the last two years, sometimes one of the current month (up to yesterday when there is one).</summary>
    private DateOnly Day(OrgClock clock)
    {
        var today = clock.Today();
        if (_rng.Next(8) == 0)
        {
            var first = BalanceHarness.MonthOf(today);
            return today > first ? first.AddDays(_rng.Next(today.DayNumber - first.DayNumber)) : today;
        }
        return today.AddDays(-_rng.Next(1, 730));
    }

    /// <summary>The last day of a random ended month.</summary>
    private DateOnly MonthEnd(OrgClock clock) => BalanceHarness.MonthOf(clock.Today()).AddMonths(-_rng.Next(0, 24)).AddDays(-1);

    /// <summary>A change as the owner: with a reason for closed months, about one in six without (then a closed month refuses it).</summary>
    private async Task<string> AsOwnerAsync(Func<TimeTrackingDbContext, Task<string?>> change)
    {
        var withReason = _rng.Next(6) != 0;
        using var _ = _h.As(_ownerId, withReason ? "property test" : null);
        await using var scope = _h.Scope();
        var db = BalanceHarness.Db(scope);
        try
        {
            var detail = await change(db);
            if (detail == null) return "nothing to do";
            await db.SaveChangesAsync();
            return detail + (withReason ? "" : " (no reason)");
        }
        catch (PeriodClosedException ex)
        {
            Refused++;
            return $"refused: {ex.Message}";
        }
    }

    private static DateTime Clamp(DateTime end) => end > DateTime.UtcNow.AddMinutes(-1) ? DateTime.UtcNow.AddMinutes(-1) : end;

    private async Task<TimeEntry?> PickEntryAsync(TimeTrackingDbContext db, int user, int org, bool? failure = null)
    {
        var q = db.TimeEntries.Where(e => e.UserId == user && e.OrganizationId == org && !e.IsRunning);
        if (failure != null) q = q.Where(e => e.IsFailure == failure);
        var n = await q.CountAsync();
        if (n == 0) return null;
        return await q.OrderBy(e => e.Id).Skip(_rng.Next(n)).FirstAsync();
    }

    // ── Entries ──

    private async Task<string> AddEntryAsync()
    {
        var (user, org) = Pair();
        var clock = await ClockAsync(org);
        var day = Day(clock);
        var cross = _rng.Next(4) == 0;
        var start = clock.ToUtc(day.ToDateTime(cross ? new TimeOnly(21, 30) : new TimeOnly(6, 0)).AddMinutes(_rng.Next(0, 240)));
        var end = start.AddMinutes(cross ? 240 + _rng.Next(120) : 120 + _rng.Next(480));
        if (end > DateTime.UtcNow) { start = DateTime.UtcNow.AddHours(-3); end = DateTime.UtcNow.AddMinutes(-5); }
        var pause = new[] { 0, 0, 15, 30, 45 }[_rng.Next(5)];
        return await AsOwnerAsync(db =>
        {
            db.TimeEntries.Add(new TimeEntry
            {
                UserId = user, OrganizationId = org, StartTime = start, EndTime = end, PauseDurationMinutes = pause, PauseIsManual = true,
                Source = TimeEntrySource.Admin, CreatedAt = start, UpdatedAt = start
            });
            return Task.FromResult<string?>($"user {user} org {org} {clock.ToLocal(start):yyyy-MM-dd HH:mm}+{(end - start).TotalMinutes}m pause {pause}");
        });
    }

    private async Task<string> EditEntryAsync()
    {
        var (user, org) = Pair();
        var clock = await ClockAsync(org);
        var newDay = Day(clock);
        var variant = _rng.Next(4);
        var minutes = _rng.Next(15, 180);
        var pause = new[] { 0, 15, 30, 45, 60 }[_rng.Next(5)];
        return await AsOwnerAsync(async db =>
        {
            var e = await PickEntryAsync(db, user, org);
            if (e == null) return null;
            var before = $"{clock.ToLocal(e.StartTime):yyyy-MM-dd HH:mm}";
            switch (variant)
            {
                case 0:
                    e.EndTime = Clamp(e.EndTime!.Value.AddMinutes(_rng.Next(2) == 0 ? -Math.Min(minutes, (e.EndTime!.Value - e.StartTime).TotalMinutes / 2) : minutes));
                    if (e.EndTime <= e.StartTime) e.EndTime = e.StartTime.AddMinutes(10);
                    return $"entry {e.Id} ({before}) end -> {clock.ToLocal(e.EndTime.Value):HH:mm}";
                case 1:
                    e.PauseDurationMinutes = pause;
                    e.PauseIsManual = true;
                    return $"entry {e.Id} ({before}) pause -> {pause}";
                default:
                    // Moves to another day (often another month), same local time of day
                    var duration = e.EndTime!.Value - e.StartTime;
                    var start = clock.ToUtc(newDay.ToDateTime(TimeOnly.FromDateTime(clock.ToLocal(e.StartTime))));
                    if (start + duration > DateTime.UtcNow) return null;
                    (e.StartTime, e.EndTime) = (start, start + duration);
                    return $"entry {e.Id} ({before}) moved to {newDay}";
            }
        });
    }

    private async Task<string> MoveEntryOrgAsync()
    {
        var user = _pairs[3].User;
        var (from, to) = _rng.Next(2) == 0 ? (_orgA, _orgB) : (_orgB, _orgA);
        return await AsOwnerAsync(async db =>
        {
            var e = await PickEntryAsync(db, user, from);
            if (e == null) return null;
            e.OrganizationId = to;
            return $"entry {e.Id} of user {user} org {from} -> {to} ({e.StartTime:yyyy-MM-dd})";
        });
    }

    private async Task<string> DeleteEntryAsync()
    {
        var (user, org) = Pair();
        return await AsOwnerAsync(async db =>
        {
            var e = await PickEntryAsync(db, user, org);
            if (e == null) return null;
            db.TimeEntries.Remove(e);
            return $"entry {e.Id} of user {user} org {org} ({e.StartTime:yyyy-MM-dd HH:mm}Z)";
        });
    }

    /// <summary>A timer started on the last evening of an ended month: read while it runs, then stopped by the member (a pure stop).</summary>
    private async Task<string> TimerAcrossMonthEndAsync()
    {
        var (user, org) = Pair();
        var clock = await ClockAsync(org);
        var start = clock.ToUtc(MonthEnd(clock).ToDateTime(new TimeOnly(21, 0)));
        int id;
        using (_h.As(_ownerId, "property test: timer"))
        {
            await using var scope = _h.Scope();
            var db = BalanceHarness.Db(scope);
            var entry = new TimeEntry { UserId = user, OrganizationId = org, StartTime = start, IsRunning = true, CreatedAt = start, UpdatedAt = start };
            db.TimeEntries.Add(entry);
            await db.SaveChangesAsync();
            id = entry.Id;
        }
        // While it runs it is not part of any balance: these reads store the month without it
        await BalancesAsync(await ReferencesAsync());
        await CheckStoredAsync($"timer {id} running\n{Context}");
        var hours = _rng.Next(2, 6);
        using (_h.As(user))
        {
            await using var scope = _h.Scope();
            var db = BalanceHarness.Db(scope);
            var entry = await db.TimeEntries.SingleAsync(e => e.Id == id);
            if (!entry.IsRunning) return $"timer {id} was auto-stopped first";
            entry.EndTime = start.AddHours(hours);
            entry.IsRunning = false;
            entry.UpdatedAt = DateTime.UtcNow;
            await db.SaveChangesAsync();
        }
        return $"timer {id} user {user} org {org} from {clock.ToLocal(start):yyyy-MM-dd HH:mm} stopped after {hours} h";
    }

    private async Task<string> TimerNowAsync()
    {
        var (user, org) = Pair();
        int id;
        using (_h.As(user))
        {
            await using var scope = _h.Scope();
            var db = BalanceHarness.Db(scope);
            var start = DateTime.UtcNow.AddMinutes(-_rng.Next(5, 90));
            var entry = new TimeEntry { UserId = user, OrganizationId = org, StartTime = start, IsRunning = true, CreatedAt = start, UpdatedAt = start };
            db.TimeEntries.Add(entry);
            await db.SaveChangesAsync();
            id = entry.Id;
            entry.EndTime = DateTime.UtcNow;
            entry.IsRunning = false;
            await db.SaveChangesAsync();
        }
        return $"timer {id} user {user} org {org} started and stopped now";
    }

    /// <summary>A forgotten timer from an ended month, closed by the business-day-end auto-stop (system work).</summary>
    private async Task<string> FailureCloseAsync()
    {
        var (user, org) = Pair();
        var clock = await ClockAsync(org);
        var day = _rng.Next(2) == 0 ? MonthEnd(clock) : Day(clock);
        var start = clock.ToUtc(day.ToDateTime(new TimeOnly(18, 0)));
        if (start > DateTime.UtcNow.AddHours(-1)) return "too recent";
        await using (var scope = _h.Scope())
        {
            var db = BalanceHarness.Db(scope);
            db.TimeEntries.Add(new TimeEntry { UserId = user, OrganizationId = org, StartTime = start, IsRunning = true, CreatedAt = start, UpdatedAt = start });
            await db.SaveChangesAsync();
        }
        await using (var scope = _h.Scope())
        {
            var closed = await FailureEntryService.CloseOverdueEntriesAsync(BalanceHarness.Db(scope), DateTime.UtcNow);
            return $"user {user} org {org} timer from {day} 18:00 auto-stopped ({closed} closed)";
        }
    }

    private async Task<string> CorrectFailureAsync()
    {
        var (user, org) = Pair();
        var hours = _rng.Next(1, 8);
        return await AsOwnerAsync(async db =>
        {
            var e = await PickEntryAsync(db, user, org, failure: true);
            if (e == null) return null;
            e.EndTime = Clamp(e.StartTime.AddHours(hours));
            e.IsFailure = false;
            return $"failure entry {e.Id} corrected to {hours} h";
        });
    }

    // ── Absences ──

    private async Task<(AbsenceType Type, int? Custom)> AbsenceKindAsync(int org)
    {
        var roll = _rng.Next(4);
        if (roll == 0 || _types[org].Count == 0) return (_rng.Next(2) == 0 ? AbsenceType.Vacation : AbsenceType.SickDay, null);
        await Task.CompletedTask;
        return (AbsenceType.Other, _types[org][_rng.Next(_types[org].Count)]);
    }

    private async Task<string> AddAbsenceAsync()
    {
        var (user, org) = Pair();
        var clock = await ClockAsync(org);
        var day = Day(clock);
        var (type, custom) = await AbsenceKindAsync(org);
        var half = _rng.Next(3) == 0;
        return await AsOwnerAsync(async db =>
        {
            if (await db.AbsenceDays.AnyAsync(a => a.UserId == user && a.OrganizationId == org && a.Date == day)) return null;
            db.AbsenceDays.Add(new AbsenceDay { UserId = user, OrganizationId = org, Date = day, Type = type, CustomTypeId = custom, IsHalfDay = half });
            return $"user {user} org {org} {day} {type}{(custom != null ? $" (type {custom})" : "")}{(half ? " half" : "")}";
        });
    }

    private async Task<string> EditAbsenceAsync()
    {
        var (user, org) = Pair();
        var clock = await ClockAsync(org);
        var newDay = Day(clock);
        var (type, custom) = await AbsenceKindAsync(org);
        var variant = _rng.Next(3);
        return await AsOwnerAsync(async db =>
        {
            var q = db.AbsenceDays.Where(a => a.UserId == user && a.OrganizationId == org);
            var n = await q.CountAsync();
            if (n == 0) return null;
            var a = await q.OrderBy(x => x.Id).Skip(_rng.Next(n)).FirstAsync();
            switch (variant)
            {
                case 0:
                    a.IsHalfDay = !a.IsHalfDay;
                    return $"absence {a.Id} ({a.Date}) half -> {a.IsHalfDay}";
                case 1:
                    (a.Type, a.CustomTypeId) = (type, custom);
                    return $"absence {a.Id} ({a.Date}) -> {type} {custom}";
                default:
                    if (await db.AbsenceDays.AnyAsync(x => x.UserId == user && x.OrganizationId == org && x.Date == newDay)) return null;
                    var old = a.Date;
                    a.Date = newDay;
                    return $"absence {a.Id} moved {old} -> {newDay}";
            }
        });
    }

    private async Task<string> DeleteAbsenceAsync()
    {
        var (user, org) = Pair();
        return await AsOwnerAsync(async db =>
        {
            var q = db.AbsenceDays.Where(a => a.UserId == user && a.OrganizationId == org);
            var n = await q.CountAsync();
            if (n == 0) return null;
            var a = await q.OrderBy(x => x.Id).Skip(_rng.Next(n)).FirstAsync();
            db.AbsenceDays.Remove(a);
            return $"absence {a.Id} ({a.Date}) of user {user} org {org}";
        });
    }

    // ── Absence types (HTTP as the owner) ──

    private int OrgPick() => _rng.Next(3) == 0 ? _orgB : _orgA;

    private async Task<string> CreateTypeAsync()
    {
        var org = OrgPick();
        var mode = (AbsenceCreditMode)_rng.Next(3);
        var name = $"Typ {++_typeCounter}";
        var response = await _owner.PostAsJsonAsync($"/api/v1/organizations/{_slugs[org]}/absence-types",
            new { name, creditMode = (int)mode, countsAsVacation = false });
        response.EnsureSuccessStatusCode();
        var id = (await response.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("id").GetInt32();
        _types[org].Add(id);
        return $"org {org} type {id} {mode}";
    }

    private async Task<string> ChangeCreditModeAsync()
    {
        var org = OrgPick();
        if (_types[org].Count == 0) return await CreateTypeAsync();
        var id = _types[org][_rng.Next(_types[org].Count)];
        var mode = (AbsenceCreditMode)_rng.Next(3);
        var response = await _owner.PutAsJsonAsync($"/api/v1/organizations/{_slugs[org]}/absence-types/{id}",
            new { name = $"Typ {id}-{_rng.Next(1000)}", creditMode = (int)mode, countsAsVacation = false });
        return $"org {org} type {id} -> {mode}: {(int)response.StatusCode}";
    }

    private async Task<string> DeleteTypeAsync()
    {
        var org = OrgPick();
        if (_types[org].Count == 0) return "no type";
        var id = _types[org][_rng.Next(_types[org].Count)];
        var response = await _owner.DeleteAsync($"/api/v1/organizations/{_slugs[org]}/absence-types/{id}");
        return $"org {org} type {id} deleted: {(int)response.StatusCode}";
    }

    // ── Schedules ──

    private double Target() => new[] { 0, 4, 6, 7.5, 8, 8, 9.25 }[_rng.Next(7)];

    private async Task<WorkSchedule?> PickScheduleAsync(TimeTrackingDbContext db, int user, int org)
    {
        var q = db.WorkSchedules.Where(ws => ws.UserId == user && ws.OrganizationId == org);
        var n = await q.CountAsync();
        return n == 0 ? null : await q.OrderBy(ws => ws.Id).Skip(_rng.Next(n)).FirstAsync();
    }

    private async Task<string> AddScheduleAsync()
    {
        var (user, org) = Pair();
        var clock = await ClockAsync(org);
        var from = Day(clock);
        DateOnly? to = _rng.Next(2) == 0 ? null : from.AddDays(_rng.Next(10, 200));
        var t = Enumerable.Range(0, 5).Select(_ => Target()).ToArray();
        return await AsOwnerAsync(db =>
        {
            db.WorkSchedules.Add(new WorkSchedule
            {
                UserId = user, OrganizationId = org, ValidFrom = from, ValidTo = to, WeeklyWorkHours = t.Sum(),
                TargetMon = t[0], TargetTue = t[1], TargetWed = t[2], TargetThu = t[3], TargetFri = t[4]
            });
            return Task.FromResult<string?>($"user {user} org {org} {from}..{to} [{string.Join(",", t)}]");
        });
    }

    private async Task<string> ScheduleTargetsAsync()
    {
        var (user, org) = Pair();
        var (mon, fri) = (Target(), Target());
        return await AsOwnerAsync(async db =>
        {
            var ws = await PickScheduleAsync(db, user, org);
            if (ws == null) return null;
            (ws.TargetMon, ws.TargetFri) = (mon, fri);
            return $"schedule {ws.Id} ({ws.ValidFrom}..{ws.ValidTo}) Mon {mon} Fri {fri}";
        });
    }

    private async Task<string> ScheduleValidToAsync()
    {
        var (user, org) = Pair();
        var clock = await ClockAsync(org);
        var day = Day(clock);
        var open = _rng.Next(3) == 0;
        return await AsOwnerAsync(async db =>
        {
            var ws = await PickScheduleAsync(db, user, org);
            if (ws == null) return null;
            var old = ws.ValidTo;
            ws.ValidTo = open ? null : (day < ws.ValidFrom ? ws.ValidFrom : day);
            return $"schedule {ws.Id} ValidTo {old} -> {ws.ValidTo}";
        });
    }

    private async Task<string> ScheduleValidFromAsync()
    {
        var (user, org) = Pair();
        var clock = await ClockAsync(org);
        var day = Day(clock);
        return await AsOwnerAsync(async db =>
        {
            var ws = await PickScheduleAsync(db, user, org);
            if (ws == null) return null;
            var old = ws.ValidFrom;
            ws.ValidFrom = ws.ValidTo is { } to && day > to ? to : day;
            return $"schedule {ws.Id} ValidFrom {old} -> {ws.ValidFrom}";
        });
    }

    private async Task<string> DeleteScheduleAsync()
    {
        var (user, org) = Pair();
        return await AsOwnerAsync(async db =>
        {
            var ws = await PickScheduleAsync(db, user, org);
            if (ws == null) return null;
            db.WorkSchedules.Remove(ws);
            return $"schedule {ws.Id} ({ws.ValidFrom}..{ws.ValidTo}) of user {user} org {org}";
        });
    }

    // ── Holidays (HTTP as the owner) ──

    private async Task<string> CreateHolidayAsync()
    {
        var org = OrgPick();
        var clock = await ClockAsync(org);
        var recurring = _rng.Next(4) == 0;
        var half = _rng.Next(4) == 0;
        var date = Day(clock);
        var response = await _owner.PostAsJsonAsync($"/api/v1/organizations/{_slugs[org]}/holidays",
            new { date = date.ToString("yyyy-MM-dd"), name = $"Feiertag {_rng.Next(10000)}", isRecurring = recurring, isHalfDay = half });
        return $"org {org} {date}{(recurring ? " recurring" : "")}{(half ? " half" : "")}: {(int)response.StatusCode}";
    }

    private async Task<int?> PickHolidayAsync(int org)
    {
        await using var scope = _h.Scope();
        var ids = await BalanceHarness.Db(scope).Holidays.AsNoTracking().Where(h => h.OrganizationId == org).OrderBy(h => h.Id).Select(h => h.Id).ToListAsync();
        return ids.Count == 0 ? null : ids[_rng.Next(ids.Count)];
    }

    private async Task<string> EditHolidayAsync()
    {
        var org = OrgPick();
        var clock = await ClockAsync(org);
        if (await PickHolidayAsync(org) is not { } id) return "no holiday";
        var variant = _rng.Next(3);
        object body = variant switch
        {
            0 => new { date = Day(clock).ToString("yyyy-MM-dd") },
            1 => new { isHalfDay = _rng.Next(2) == 0 },
            _ => new { isRecurring = _rng.Next(2) == 0 }
        };
        var response = await _owner.PutAsJsonAsync($"/api/v1/organizations/{_slugs[org]}/holidays/{id}", body);
        return $"org {org} holiday {id} {JsonSerializer.Serialize(body)}: {(int)response.StatusCode}";
    }

    private async Task<string> DeleteHolidayAsync()
    {
        var org = OrgPick();
        if (await PickHolidayAsync(org) is not { } id) return "no holiday";
        var response = await _owner.DeleteAsync($"/api/v1/organizations/{_slugs[org]}/holidays/{id}");
        return $"org {org} holiday {id}: {(int)response.StatusCode}";
    }

    // ── Org settings, initial overtime, closed months (HTTP as the owner) ──

    private async Task<string> TimeZoneAsync()
    {
        var org = OrgPick();
        var zone = Zones[_rng.Next(Zones.Length)];
        var response = await _owner.PutAsJsonAsync($"/api/v1/Organizations/{_slugs[org]}/settings", new { timeZone = zone });
        response.EnsureSuccessStatusCode();
        return $"org {org} -> {zone}";
    }

    private async Task<string> InitialOvertimeAsync()
    {
        var (user, org) = Pair();
        var hours = Math.Round(_rng.NextDouble() * 40 - 10, 2);
        var response = await _owner.PutAsJsonAsync($"/api/v1/Organizations/{_slugs[org]}/members/{user}/initial-overtime",
            new { initialOvertimeHours = hours });
        response.EnsureSuccessStatusCode();
        return $"user {user} org {org} initial {hours}";
    }

    private async Task<string> CloseMonthsAsync()
    {
        var org = OrgPick();
        var clock = await ClockAsync(org);
        var to = BalanceHarness.MonthOf(clock.Today()).AddMonths(-_rng.Next(1, 24));
        var from = to.AddMonths(-_rng.Next(0, 3));
        var response = await _owner.PostAsJsonAsync($"/api/v1/organizations/{_slugs[org]}/closed-periods",
            new { from = from.ToString("yyyy-MM-dd"), to = to.ToString("yyyy-MM-dd"), reason = "property test" });
        return $"org {org} {from:yyyy-MM}..{to:yyyy-MM}: {(int)response.StatusCode}";
    }

    private async Task<string> ReopenMonthAsync()
    {
        var org = OrgPick();
        await using var scope = _h.Scope();
        var months = await BalanceHarness.Db(scope).ClosedPeriods.AsNoTracking().Where(c => c.OrganizationId == org).Select(c => c.Month).ToListAsync();
        if (months.Count == 0) return "nothing closed";
        var month = months.OrderBy(m => m).ElementAt(_rng.Next(months.Count));
        var response = await _owner.PostAsJsonAsync($"/api/v1/organizations/{_slugs[org]}/closed-periods/reopen",
            new { month = month.ToString("yyyy-MM-dd"), reason = "property test" });
        return $"org {org} {month:yyyy-MM}: {(int)response.StatusCode}";
    }
}
