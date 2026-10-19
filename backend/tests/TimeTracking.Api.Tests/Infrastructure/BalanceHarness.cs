using System.Security.Claims;
using FluentAssertions;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using TimeTracking.Api.Data;
using TimeTracking.Api.Models;
using TimeTracking.Api.Services;

namespace TimeTracking.Api.Tests.Infrastructure;

/// <summary>
/// Shared helpers of the balance checkpoint tests (Phase 2 of docs/PERFORMANCE_PLAN.md): app scopes that act as a signed-in
/// user (row-level security on Postgres, closed-period guard with an optional <c>X-Change-Reason</c>), the from-scratch
/// reference balance (public <see cref="DailyReportAggregator.BuildAsync(TimeTrackingDbContext, int, int, DateOnly, DateOnly, double, bool, DateOnly?)"/>
/// from the first tracked day, no checkpoints) and the core invariant: every <em>valid</em> stored checkpoint equals that
/// reference at its month end.
/// </summary>
public sealed class BalanceHarness(WebApplicationFactory<Program> factory)
{
    public const double Exact = 1e-9;

    public WebApplicationFactory<Program> Factory { get; } = factory;

    /// <summary>
    /// Until disposed, app scopes created on this async flow see a request of <paramref name="userId"/> (RLS as that user,
    /// the closed-period guard and entry history as for a real request), with <paramref name="reason"/> as the
    /// <c>X-Change-Reason</c> header. Synchronous on purpose: the AsyncLocal it sets stays visible to the caller.
    /// </summary>
    public IDisposable As(int userId, string? reason = null)
    {
        var accessor = Factory.Services.GetRequiredService<IHttpContextAccessor>();
        var ctx = new DefaultHttpContext
        {
            User = new ClaimsPrincipal(new ClaimsIdentity([new Claim(ClaimTypes.NameIdentifier, userId.ToString())], "test"))
        };
        if (reason != null) ctx.Request.Headers["X-Change-Reason"] = Uri.EscapeDataString(reason);
        accessor.HttpContext = ctx;
        return new Reset(accessor);
    }

    private sealed class Reset(IHttpContextAccessor accessor) : IDisposable
    {
        public void Dispose() => accessor.HttpContext = null;
    }

    /// <summary>A fresh app scope (system work unless inside <see cref="As"/>).</summary>
    public AsyncServiceScope Scope() => Factory.Services.CreateAsyncScope();

    public static TimeTrackingDbContext Db(AsyncServiceScope scope) => scope.ServiceProvider.GetRequiredService<TimeTrackingDbContext>();

    /// <summary>The reference: rows from the member's first finished entry (org-local) to today with initial overtime 0,
    /// so a row's <see cref="DailyReportRow.CumulativeOvertime"/> is exactly the overtime sum a checkpoint stores.</summary>
    public static async Task<Reference> ReferenceAsync(TimeTrackingDbContext db, int userId, int orgId)
    {
        var timeZone = await db.Organizations.IgnoreQueryFilters().AsNoTracking()
            .Where(o => o.Id == orgId).Select(o => o.TimeZone).FirstAsync();
        var clock = OrgClock.For(timeZone);
        var first = await db.TimeEntries.AsNoTracking()
            .Where(e => e.UserId == userId && e.OrganizationId == orgId && !e.IsRunning)
            .MinAsync(e => (DateTime?)e.StartTime);
        if (first == null) return new Reference(null, [], clock.Today());
        var firstDay = clock.LocalDate(first.Value);
        var rows = await DailyReportAggregator.BuildAsync(db, userId, orgId, firstDay, clock.Today(), 0);
        return new Reference(firstDay, rows, clock.Today());
    }

    public sealed record Reference(DateOnly? FirstDay, List<DailyReportRow> Rows, DateOnly Today)
    {
        private readonly Dictionary<DateOnly, DailyReportRow> _byDate = Rows.ToDictionary(r => r.Date);

        /// <summary>The overtime sum up to and including <paramref name="day"/> (0 before the first day).</summary>
        public double SumAt(DateOnly day) =>
            _byDate.TryGetValue(day, out var r) ? r.CumulativeOvertime : (FirstDay == null || day < FirstDay ? 0 : Rows[^1].CumulativeOvertime);

        public DailyReportRow? Row(DateOnly day) => _byDate.GetValueOrDefault(day);

        /// <summary>The overall balance with <paramref name="initial"/> — the same expression the app evaluates.</summary>
        public double Balance(double initial) => Rows.Count == 0 ? initial : initial + Rows[^1].CumulativeOvertime;
    }

    /// <summary>The stored checkpoints of the organizations that are valid right now (versions and algorithm match).</summary>
    public static Task<List<BalanceCheckpoint>> ValidCheckpointsAsync(TimeTrackingDbContext db, IReadOnlyCollection<int> orgIds) =>
        db.BalanceCheckpoints.IgnoreQueryFilters().AsNoTracking()
            .Where(c => orgIds.Contains(c.OrganizationId) && c.Algorithm == BalanceCheckpoints.AlgorithmVersion
                        && db.UserOrganizations.IgnoreQueryFilters().Any(uo =>
                            uo.OrganizationId == c.OrganizationId && uo.UserId == c.UserId && uo.BalanceVersion == c.Version)
                        && db.Organizations.IgnoreQueryFilters().Any(o => o.Id == c.OrganizationId && o.BalanceVersion == c.OrgVersion))
            .OrderBy(c => c.OrganizationId).ThenBy(c => c.UserId).ThenBy(c => c.Month)
            .ToListAsync();

    /// <summary>
    /// The core invariant (system scope, sees everything): every valid checkpoint of the organizations is for an ended month,
    /// starts at the member's real first day and holds exactly the reference overtime sum at its month end. Returns how many
    /// valid rows were checked.
    /// </summary>
    public async Task<int> AssertValidCheckpointsAsync(IReadOnlyCollection<int> orgIds, string because,
        IReadOnlyDictionary<(int Org, int User), Reference>? known = null)
    {
        await using var scope = Scope();
        var db = Db(scope);
        var valid = await ValidCheckpointsAsync(db, orgIds);
        var refs = known?.ToDictionary(k => k.Key, k => k.Value) ?? new Dictionary<(int Org, int User), Reference>();
        foreach (var c in valid)
        {
            if (!refs.TryGetValue((c.OrganizationId, c.UserId), out var reference))
                refs[(c.OrganizationId, c.UserId)] = reference = await ReferenceAsync(db, c.UserId, c.OrganizationId);
            var monthEnd = c.Month.AddMonths(1).AddDays(-1);
            var what = $"valid checkpoint org {c.OrganizationId} user {c.UserId} {c.Month:yyyy-MM} (v{c.Version}/o{c.OrgVersion}) — {because}";
            monthEnd.Should().BeBefore(reference.Today, what + ": only ended months");
            c.FirstDay.Should().Be(reference.FirstDay, what + ": first day");
            var row = reference.Row(monthEnd);
            row.Should().NotBeNull(what + ": the reference has a row at the month end");
            Math.Abs(c.OvertimeDelta - row!.CumulativeOvertime).Should().BeLessThanOrEqualTo(Exact,
                $"{what}: stored {c.OvertimeDelta:R}, recomputed {row.CumulativeOvertime:R}");
        }
        // InMemory has no unique index: never two rows for one member and month
        valid.GroupBy(c => (c.OrganizationId, c.UserId, c.Month)).Should().OnlyContain(g => g.Count() == 1, because);
        return valid.Count;
    }

    /// <summary>The member's and organization's current versions.</summary>
    public async Task<(int Member, int Org)> VersionsAsync(int userId, int orgId)
    {
        await using var scope = Scope();
        var db = Db(scope);
        var member = await db.UserOrganizations.IgnoreQueryFilters().AsNoTracking()
            .Where(uo => uo.UserId == userId && uo.OrganizationId == orgId).Select(uo => uo.BalanceVersion).SingleAsync();
        var org = await db.Organizations.IgnoreQueryFilters().AsNoTracking().Where(o => o.Id == orgId).Select(o => o.BalanceVersion).SingleAsync();
        return (member, org);
    }

    /// <summary>All stored checkpoints of a member (valid or not), by month.</summary>
    public async Task<List<BalanceCheckpoint>> CheckpointsAsync(int userId, int orgId)
    {
        await using var scope = Scope();
        return await Db(scope).BalanceCheckpoints.IgnoreQueryFilters().AsNoTracking()
            .Where(c => c.UserId == userId && c.OrganizationId == orgId).OrderBy(c => c.Month).ToListAsync();
    }

    /// <summary>Marks the organizations as pre-warmed for this month, so the hosted minute loop leaves them alone.</summary>
    public async Task SkipPrewarmAsync(params int[] orgIds)
    {
        await using var scope = Scope();
        var db = Db(scope);
        foreach (var org in await db.Organizations.IgnoreQueryFilters().Where(o => orgIds.Contains(o.Id)).ToListAsync())
        {
            var today = OrgClock.For(org).Today();
            org.BalanceWarmedMonth = new DateOnly(today.Year, today.Month, 1).AddMonths(1);
        }
        await db.SaveChangesAsync();
    }

    public static DateOnly MonthOf(DateOnly day) => new(day.Year, day.Month, 1);
}
