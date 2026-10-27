using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.ChangeTracking;
using TimeTracking.Api.Data;
using TimeTracking.Api.Models;
using TimeTracking.Api.Models.Dtos;

namespace TimeTracking.Api.Services;

/// <summary>A write touches a closed month (409, code <c>period.closed</c> / <c>period.reasonRequired</c>, extension <c>month</c>).</summary>
public sealed class PeriodClosedException(DateOnly month, bool reasonRequired) : Exception(reasonRequired
    ? $"The period {month:yyyy-MM} is closed. Give a reason to change it."
    : $"The period {month:yyyy-MM} is closed. Only organization admins can change it.")
{
    public DateOnly Month { get; } = month;
    public string Code { get; } = reasonRequired ? "period.reasonRequired" : "period.closed";
}

/// <summary>
/// Closed months (issue #57). The one guard: <see cref="GuardAsync"/> runs inside
/// <see cref="TimeTrackingDbContext.SaveChangesAsync(bool, CancellationToken)"/> for every save
/// made for a person (not for system work), so every path that adds, changes or deletes a time
/// entry, an absence day or a work schedule goes through it without code of its own: the timer,
/// manual entries, member corrections, imports, request approvals, absences, schedules and the
/// terminal. A change whose day (entries: the org-local day they start on, both before and after
/// the change) lies in a closed month is refused for everyone but org admins/owners of that
/// organization, who must send a reason (<c>X-Change-Reason</c>); their entry changes are flagged
/// in the edit history and every such save is logged as an AdminChange event. Exceptions:
/// stopping a running timer (no history either), and system work (the business-day-end auto-stop,
/// core-hours checks), which never passes here. Requests are checked when they are made
/// (<see cref="EnsureRequestOpenAsync"/>) and again by the guard when they are approved.
/// </summary>
public static class ClosedPeriods
{
    public const int MaxReasonLength = 500;
    /// <summary>At most this many months per close call.</summary>
    public const int MaxMonthsPerClose = 24;

    public static DateOnly MonthOf(DateOnly day) => new(day.Year, day.Month, 1);

    /// <summary>The first closed month overlapping the days <paramref name="from"/>..<paramref name="to"/> (null = open-ended).</summary>
    public static DateOnly? FirstClosed(IEnumerable<DateOnly> closedMonths, DateOnly from, DateOnly? to) =>
        closedMonths.Where(m => m.AddMonths(1) > from && (to == null || m <= to))
            .Order().Select(m => (DateOnly?)m).FirstOrDefault();

    public static Task<List<DateOnly>> MonthsAsync(TimeTrackingDbContext db, int orgId) =>
        db.ClosedPeriods.AsNoTracking().Where(p => p.OrganizationId == orgId)
            .OrderBy(p => p.Month).Select(p => p.Month).ToListAsync();

    private sealed record Touch(int OrgId, int UserId, string Kind, object Entity, DateTime? StartUtc, DateOnly From, DateOnly? To);

    /// <summary>What an admin's save inside closed months changes: its time entries (for the history flag) and every closed
    /// member-month it touches (re-frozen in the same save, <see cref="FrozenFigures.RefreezeAsync"/>).</summary>
    internal sealed record AdminChanges(IReadOnlySet<TimeEntry> Entries, IReadOnlyCollection<(int OrgId, int UserId, DateOnly Month)> Months);

    /// <summary>
    /// Checks the pending changes; returns what an admin changes inside closed months
    /// and stages one AdminChange event per organization and month.
    /// </summary>
    internal static async Task<AdminChanges?> GuardAsync(
        TimeTrackingDbContext db, int? callerId, string? reason, CancellationToken ct)
    {
        var touches = Collect(db);
        if (touches.Count == 0) return null;

        var orgIds = touches.Select(t => t.OrgId).Distinct().ToList();
        var closed = (await db.ClosedPeriods.AsNoTracking().Where(p => orgIds.Contains(p.OrganizationId))
                .Select(p => new { p.OrganizationId, p.Month }).ToListAsync(ct))
            .GroupBy(p => p.OrganizationId).ToDictionary(g => g.Key, g => g.Select(p => p.Month).ToList());
        if (closed.Count == 0) return null;

        var zones = touches.Any(t => t.StartUtc != null && closed.ContainsKey(t.OrgId))
            ? await db.Organizations.AsNoTracking().Where(o => closed.Keys.Contains(o.Id))
                .ToDictionaryAsync(o => o.Id, o => OrgClock.For(o.TimeZone), ct)
            : [];
        var hits = touches
            .Where(t => closed.ContainsKey(t.OrgId))
            .Select(t =>
            {
                var from = t.StartUtc is { } s ? zones[t.OrgId].LocalDate(s) : t.From;
                return (Touch: t, Month: FirstClosed(closed[t.OrgId], from, t.StartUtc != null ? from : t.To));
            })
            .Where(h => h.Month != null)
            .Select(h => (h.Touch, Month: h.Month!.Value))
            .ToList();
        if (hits.Count == 0) return null;

        var first = hits.MinBy(h => h.Month).Month;
        var hitOrgs = hits.Select(h => h.Touch.OrgId).Distinct().ToList();
        var adminOrgs = callerId is int caller
            ? await db.UserOrganizations.AsNoTracking()
                .Where(uo => uo.UserId == caller && hitOrgs.Contains(uo.OrganizationId) && uo.Role >= OrganizationRole.Admin)
                .Select(uo => uo.OrganizationId).ToListAsync(ct)
            : [];
        if (hits.FirstOrDefault(h => !adminOrgs.Contains(h.Touch.OrgId)) is { Touch: not null } refused)
            throw new PeriodClosedException(refused.Month, reasonRequired: false);
        if (reason == null)
            throw new PeriodClosedException(first, reasonRequired: true);

        foreach (var group in hits.GroupBy(h => (h.Touch.OrgId, h.Month)))
        {
            var members = group.Select(h => h.Touch.UserId).Distinct().ToList();
            db.ClosedPeriodEvents.Add(new ClosedPeriodEvent
            {
                OrganizationId = group.Key.OrgId,
                Month = group.Key.Month,
                Kind = ClosedPeriodEventKind.AdminChange,
                UserId = callerId,
                At = DateTime.UtcNow,
                Reason = reason,
                MemberUserId = members.Count == 1 ? members[0] : null,
                Summary = string.Join(",", group.DistinctBy(h => h.Touch.Entity).GroupBy(h => h.Touch.Kind).Select(k => $"{k.Key}:{k.Count()}"))
            });
        }
        // Every closed month a change touches (a schedule's range may span several) is re-frozen
        var months = hits.SelectMany(h =>
            {
                var from = h.Touch.StartUtc is { } s ? zones[h.Touch.OrgId].LocalDate(s) : h.Touch.From;
                var to = h.Touch.StartUtc != null ? from : h.Touch.To;
                return closed[h.Touch.OrgId].Where(m => FirstClosed([m], from, to) != null)
                    .Select(m => (h.Touch.OrgId, h.Touch.UserId, m));
            })
            .ToHashSet();
        return new AdminChanges(hits.Select(h => h.Touch.Entity).OfType<TimeEntry>().ToHashSet(), months);
    }

    /// <summary>The days each pending change touches: added → new values, deleted → old values, changed → both.</summary>
    private static List<Touch> Collect(TimeTrackingDbContext db)
    {
        var touches = new List<Touch>();
        foreach (var e in db.ChangeTracker.Entries())
        {
            if (e.State is not (EntityState.Added or EntityState.Modified or EntityState.Deleted)) continue;
            switch (e.Entity)
            {
                case TimeEntry entry:
                    // Stopping a running timer is not a change of a finished entry — as long as the start and org stay put
                    if (e.State == EntityState.Modified && (bool)e.Property(nameof(TimeEntry.IsRunning)).OriginalValue!
                        && Unchanged(e, nameof(TimeEntry.StartTime)) && Unchanged(e, nameof(TimeEntry.OrganizationId))) continue;
                    foreach (var (org, start) in Versions(e, nameof(TimeEntry.OrganizationId), nameof(TimeEntry.StartTime)))
                        if (org is int orgId)
                            touches.Add(new Touch(orgId, entry.UserId, nameof(TimeEntry), entry, (DateTime)start!, default, null));
                    break;
                case AbsenceDay absence:
                    foreach (var (org, date) in Versions(e, nameof(AbsenceDay.OrganizationId), nameof(AbsenceDay.Date)))
                        touches.Add(new Touch((int)org!, absence.UserId, nameof(AbsenceDay), absence, null, (DateOnly)date!, (DateOnly)date!));
                    break;
                case WorkSchedule schedule:
                    foreach (var (from, to) in ScheduleRanges(e))
                        touches.Add(new Touch(schedule.OrganizationId, schedule.UserId, nameof(WorkSchedule), schedule, null, from, to));
                    break;
            }
        }
        return touches;
    }

    private static bool Unchanged(EntityEntry e, string property) =>
        Equals(e.Property(property).OriginalValue, e.Property(property).CurrentValue);

    private static IEnumerable<(object? A, object? B)> Versions(EntityEntry e, string a, string b)
    {
        var current = (e.Property(a).CurrentValue, e.Property(b).CurrentValue);
        var original = (e.Property(a).OriginalValue, e.Property(b).OriginalValue);
        return e.State switch
        {
            EntityState.Added => [current],
            EntityState.Deleted => [original],
            _ => Equals(current, original) ? [current] : [original, current]
        };
    }

    /// <summary>The days whose target hours a schedule change alters (to null = open-ended).</summary>
    internal static IEnumerable<(DateOnly From, DateOnly? To)> ScheduleRanges(EntityEntry e)
    {
        var (from, to) = (e.Property(nameof(WorkSchedule.ValidFrom)), e.Property(nameof(WorkSchedule.ValidTo)));
        if (e.State != EntityState.Modified)
        {
            var values = e.State == EntityState.Added ? (from.CurrentValue, to.CurrentValue) : (from.OriginalValue, to.OriginalValue);
            yield return ((DateOnly)values.Item1!, (DateOnly?)values.Item2);
            yield break;
        }
        var (oldFrom, newFrom) = ((DateOnly)from.OriginalValue!, (DateOnly)from.CurrentValue!);
        var (oldTo, newTo) = ((DateOnly?)to.OriginalValue, (DateOnly?)to.CurrentValue);
        DateOnly? Later(DateOnly? x, DateOnly? y) => x == null || y == null ? null : (x > y ? x : y);
        DateOnly? Earlier(DateOnly? x, DateOnly? y) => x == null ? y : y == null ? x : (x < y ? x : y);

        var targetsChanged = e.Properties.Any(p => p.IsModified
            && (p.Metadata.Name.StartsWith("Target") || p.Metadata.Name == nameof(WorkSchedule.WeeklyWorkHours)));
        if (targetsChanged)
        {
            yield return (oldFrom < newFrom ? oldFrom : newFrom, Later(oldTo, newTo));
            yield break;
        }
        if (oldFrom != newFrom)
            yield return (oldFrom < newFrom ? oldFrom : newFrom, (oldFrom > newFrom ? oldFrom : newFrom).AddDays(-1));
        if (oldTo != newTo)
            yield return (Earlier(oldTo, newTo)!.Value.AddDays(1), Later(oldTo, newTo));
    }

    // ── Requests ──

    private static readonly JsonSerializerOptions RequestDataJson = new() { PropertyNameCaseInsensitive = true };

    /// <summary>
    /// A member's new request must not change a closed month. Org admins may ask; approving then
    /// needs a reason like any admin change — right away when the request applies at once
    /// (<paramref name="appliesNow"/>: an auto-approved absence), so nothing is half saved.
    /// </summary>
    public static async Task EnsureRequestOpenAsync(TimeTrackingDbContext db, Organization org, int userId,
        OrgRequest request, bool appliesNow, string? reason)
    {
        var months = await MonthsAsync(db, org.Id);
        if (months.Count == 0) return;
        var starts = await EntryStartsAsync(db, [request]);
        if (FirstClosed(months, OrgClock.For(org), request, starts) is not { } month) return;
        if (!(await db.GetOrgRoleAsync(userId, org.Id) >= OrganizationRole.Admin))
            throw new PeriodClosedException(month, reasonRequired: false);
        if (appliesNow && reason == null)
            throw new PeriodClosedException(month, reasonRequired: true);
    }

    /// <summary>The first closed month each of the (pending) requests would change, by request id.</summary>
    public static async Task<Dictionary<int, DateOnly>> TouchedByRequestsAsync(TimeTrackingDbContext db, IReadOnlyCollection<OrgRequest> requests)
    {
        var pending = requests.Where(r => r.Status == RequestStatus.Pending).ToList();
        if (pending.Count == 0) return [];
        var orgIds = pending.Select(r => r.OrganizationId).Distinct().ToList();
        var months = (await db.ClosedPeriods.AsNoTracking().Where(p => orgIds.Contains(p.OrganizationId))
                .Select(p => new { p.OrganizationId, p.Month }).ToListAsync())
            .GroupBy(p => p.OrganizationId).ToDictionary(g => g.Key, g => g.Select(p => p.Month).ToList());
        if (months.Count == 0) return [];

        var clocks = await db.Organizations.AsNoTracking().Where(o => months.Keys.Contains(o.Id))
            .ToDictionaryAsync(o => o.Id, o => OrgClock.For(o.TimeZone));
        var relevant = pending.Where(r => months.ContainsKey(r.OrganizationId)).ToList();
        var starts = await EntryStartsAsync(db, relevant);
        var result = new Dictionary<int, DateOnly>();
        foreach (var r in relevant)
            if (FirstClosed(months[r.OrganizationId], clocks[r.OrganizationId], r, starts) is { } month)
                result[r.Id] = month;
        return result;
    }

    private static async Task<Dictionary<int, DateTime>> EntryStartsAsync(TimeTrackingDbContext db, IEnumerable<OrgRequest> requests)
    {
        var ids = requests.Where(r => r.Type is RequestType.EditPastEntry or RequestType.EditPause)
            .Select(r => r.RelatedEntityId).OfType<int>().Distinct().ToList();
        return ids.Count == 0 ? [] : await db.TimeEntries.AsNoTracking().IgnoreQueryFilters()
            .Where(e => ids.Contains(e.Id)).ToDictionaryAsync(e => e.Id, e => e.StartTime);
    }

    /// <summary>
    /// How many pending requests of the organization would change each of the given months (first days) —
    /// the accounting panel's warning before closing (same ranges as the guard uses for requests).
    /// </summary>
    public static async Task<Dictionary<DateOnly, int>> PendingRequestsPerMonthAsync(TimeTrackingDbContext db, Organization org, IReadOnlyCollection<DateOnly> months)
    {
        var pending = await db.OrgRequests.AsNoTracking()
            .Where(r => r.OrganizationId == org.Id && r.Status == RequestStatus.Pending)
            .ToListAsync();
        var result = months.ToDictionary(m => m, _ => 0);
        if (pending.Count == 0) return result;
        var clock = OrgClock.For(org);
        var starts = await EntryStartsAsync(db, pending);
        foreach (var r in pending)
        {
            var ranges = Ranges(clock, r, starts);
            foreach (var month in months)
                if (ranges.Any(x => FirstClosed([month], x.From, x.To) != null)) result[month]++;
        }
        return result;
    }

    private static DateOnly? FirstClosed(List<DateOnly> months, OrgClock clock, OrgRequest r, Dictionary<int, DateTime> entryStarts) =>
        Ranges(clock, r, entryStarts).Select(x => FirstClosed(months, x.From, x.To)).Where(m => m != null).Min();

    /// <summary>The org-local days a request would change (to null = open-ended).</summary>
    private static List<(DateOnly From, DateOnly? To)> Ranges(OrgClock clock, OrgRequest r, Dictionary<int, DateTime> entryStarts)
    {
        var ranges = new List<(DateOnly From, DateOnly? To)>();
        void Day(DateTime utc) => ranges.Add((clock.LocalDate(utc), clock.LocalDate(utc)));
        try
        {
            if (r.RelatedEntityId is int id && entryStarts.TryGetValue(id, out var start)) Day(start);
            switch (r.Type)
            {
                case RequestType.EditPastEntry when r.RequestData != null:
                    if (JsonSerializer.Deserialize<EditEntryRequestData>(r.RequestData, RequestDataJson)?.StartTime is { } newStart) Day(newStart);
                    break;
                case RequestType.AddManualEntry when r.RequestData != null:
                    if (JsonSerializer.Deserialize<ManualEntryRequestData>(r.RequestData, RequestDataJson) is { } manual) Day(manual.StartTime);
                    break;
                case RequestType.RequestAbsence when r.RequestData != null:
                    foreach (var date in JsonSerializer.Deserialize<AbsenceRequestData>(r.RequestData, RequestDataJson)?.Dates ?? [])
                        ranges.Add((date, date));
                    break;
                case RequestType.WorkScheduleChange when r.RequestData != null:
                    if (JsonSerializer.Deserialize<WorkScheduleChangeRequestData>(r.RequestData, RequestDataJson) is { } schedule)
                        ranges.Add((schedule.ValidFrom, null));
                    break;
                case RequestType.CsvImport when r.RequestData != null:
                    foreach (var row in JsonSerializer.Deserialize<CsvImportRequestData>(r.RequestData, RequestDataJson)?.Entries ?? [])
                        if (TimeEntryExportService.ParseImportRow(clock, row.Date, row.StartTime, row.EndTime, row.PauseMinutes,
                                row.Description, out var startUtc, out _) == null)
                            Day(startUtc);
                    break;
            }
        }
        catch (JsonException)
        {
            // Invalid data cannot be applied anyway
        }
        return ranges;
    }
}
