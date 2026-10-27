using System.Globalization;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using TimeTracking.Api.Data;
using TimeTracking.Api.Models;

namespace TimeTracking.Api.Services;

/// <summary>
/// A core working hours window in the organization's local time. End before start = past midnight
/// (night shift, e.g. 22:00–02:00): the window belongs to the day it starts on and ends on the next.
/// Start == end is not a window (rejected by the settings).
/// </summary>
public record CoreWindow(TimeOnly Start, TimeOnly End)
{
    public bool Overnight => End < Start;

    /// <summary>Minutes from the start day's midnight: [start, end), end + 1440 past midnight.</summary>
    public (int Start, int End) Minutes
    {
        get
        {
            int s = Start.Hour * 60 + Start.Minute, e = End.Hour * 60 + End.Minute;
            return (s, e <= s ? e + 1440 : e);
        }
    }

    /// <summary>The window of the local <paramref name="day"/> in UTC (DST-aware: a night window is an hour shorter or longer on the change nights).</summary>
    public (DateTime FromUtc, DateTime ToUtc) Utc(DateOnly day, OrgClock clock) =>
        (clock.ToUtc(day.ToDateTime(Start)), clock.ToUtc((Overnight ? day.AddDays(1) : day).ToDateTime(End)));

    public override string ToString() => $"{Start:HH:mm}-{End:HH:mm}";
}

/// <summary>
/// Core working hours (Kernarbeitszeit). The rules:
/// <list type="bullet">
/// <item>The window is the organization's, or the groups' override (<see cref="EffectiveRuleResolver.CombineCoreHours"/>),
/// the tolerance likewise (<see cref="EffectiveRuleResolver.CombineCoreTolerance"/>). A window past midnight
/// belongs to the local day it starts on (<see cref="CoreWindow"/>): working day, holidays and absences are
/// those of the start day, and entries across midnight cover it.</item>
/// <item>It applies on the member's working days (<see cref="WorkingDays.IsWorkingDay"/>: target &gt; 0 in their schedule,
/// weekend schedules included, Mon–Fri without one), not on holidays or absence days. A half-day absence or half
/// holiday exempts one half of the window: whichever half the member covered better, so it does not matter
/// whether they took the morning or the afternoon off. Two halves (half holiday + half absence) exempt the day.</item>
/// <item>Covering time is the member's finished entries in that organization plus a running timer up to now.
/// Failure entries do not cover (they have to be corrected anyway). Pauses inside an entry are not gaps.</item>
/// <item>Violations: <see cref="CoreHoursIssue.Late"/> (first covered minute later than start + tolerance),
/// <see cref="CoreHoursIssue.Early"/> (last earlier than end − tolerance), <see cref="CoreHoursIssue.Gap"/>
/// (a gap longer than the tolerance), <see cref="CoreHoursIssue.Missing"/> (nothing covered).</item>
/// </list>
/// The background job (<see cref="CheckDueAsync"/>, run by <see cref="FailureEntryService"/> every minute) checks a
/// member's day once its window end + tolerance has passed, writes one <see cref="CoreHoursCheck"/> per member and day
/// and notifies violations once. Views re-evaluate the stored violations, so a corrected day loses its marker.
/// </summary>
public static class CoreHours
{
    public const string NotificationType = "CoreHoursViolation";
    /// <summary>How many days before today the check catches up on (downtime, restarts).</summary>
    public const int CatchUpDays = 3;
    /// <summary>Most past days a backfill may flag ("Auch die letzten N Tage prüfen").</summary>
    public const int MaxBackfillDays = 31;
    /// <summary>A check this long after its due time counts as late (<see cref="CoreHoursCheck.CheckedLate"/>).</summary>
    public static readonly TimeSpan LateAfter = TimeSpan.FromHours(1);
    /// <summary>Appended to the English notification message of a late check (the frontend looks for it).</summary>
    public const string CheckedLateMarker = "(checked late)";

    public record Result(CoreHoursIssue Issues, int MissingMinutes, CoreWindow Window, bool Backfilled = false);

    /// <summary>Checks [fromUtc, toUtc) against the covered intervals.</summary>
    public static (CoreHoursIssue Issues, int MissingMinutes) Evaluate(
        IEnumerable<(DateTime Start, DateTime End)> covered, DateTime fromUtc, DateTime toUtc, int toleranceMinutes)
    {
        var tolerance = TimeSpan.FromMinutes(toleranceMinutes);
        var merged = new List<(DateTime Start, DateTime End)>();
        foreach (var (start, end) in covered
                     .Select(c => (Start: c.Start < fromUtc ? fromUtc : c.Start, End: c.End > toUtc ? toUtc : c.End))
                     .Where(c => c.End > c.Start)
                     .OrderBy(c => c.Start))
        {
            if (merged.Count > 0 && start <= merged[^1].End)
                merged[^1] = (merged[^1].Start, end > merged[^1].End ? end : merged[^1].End);
            else
                merged.Add((start, end));
        }

        var coveredMinutes = merged.Sum(m => (m.End - m.Start).TotalMinutes);
        var missing = (int)Math.Round((toUtc - fromUtc).TotalMinutes - coveredMinutes);
        if (merged.Count == 0) return (CoreHoursIssue.Missing, missing);

        var issues = CoreHoursIssue.None;
        if (merged[0].Start - fromUtc > tolerance) issues |= CoreHoursIssue.Late;
        if (toUtc - merged[^1].End > tolerance) issues |= CoreHoursIssue.Early;
        for (var i = 1; i < merged.Count; i++)
            if (merged[i].Start - merged[i - 1].End > tolerance) issues |= CoreHoursIssue.Gap;
        return (issues, missing);
    }

    /// <summary>
    /// Checks one local day (the day the window starts on). With <paramref name="halfDay"/> only one half
    /// of the window has to be covered: the result of the better covered half (no issues first, then fewer
    /// missing minutes, then the second half). The halves split the real duration, so a DST night splits evenly.
    /// </summary>
    public static Result EvaluateDay(IReadOnlyCollection<(DateTime Start, DateTime End)> covered, DateOnly day,
        CoreWindow window, bool halfDay, int toleranceMinutes, OrgClock clock)
    {
        var (from, to) = window.Utc(day, clock);
        (DateTime From, DateTime To, CoreWindow Window)[] parts = [(from, to, window)];
        if (halfDay)
        {
            var middle = from + (to - from) / 2;
            var local = TimeOnly.FromDateTime(clock.ToLocal(middle));
            parts = [(middle, to, new CoreWindow(local, window.End)), (from, middle, new CoreWindow(window.Start, local))];
        }

        return parts
            .Select(p =>
            {
                var (issues, missing) = Evaluate(covered, p.From, p.To, toleranceMinutes);
                return new Result(issues, missing, p.Window);
            })
            .OrderBy(r => r.Issues != CoreHoursIssue.None)
            .ThenBy(r => r.MissingMinutes)
            .First();
    }

    /// <summary>"late", "early", "gap", "missing" — the API's names of the issues.</summary>
    public static List<string> IssueNames(CoreHoursIssue issues) =>
        Enum.GetValues<CoreHoursIssue>()
            .Where(i => i != CoreHoursIssue.None && issues.HasFlag(i))
            .Select(i => i.ToString().ToLowerInvariant())
            .ToList();

    /// <summary>
    /// The stored violations of a member in [from, to] that still hold against the current entries
    /// (a day the member has corrected since is left out), keyed by local (start) day.
    /// </summary>
    public static async Task<Dictionary<DateOnly, Result>> OpenViolationsAsync(
        TimeTrackingDbContext context, int userId, int orgId, DateOnly from, DateOnly to, DateTime? nowUtc = null) =>
        (await OpenViolationsAsync(context, [userId], orgId, from, to, nowUtc)).GetValueOrDefault(userId) ?? [];

    /// <summary><see cref="OpenViolationsAsync(TimeTrackingDbContext, int, int, DateOnly, DateOnly, DateTime?)"/> for several members at once (one query when nobody has a stored violation, three otherwise).</summary>
    public static async Task<Dictionary<int, Dictionary<DateOnly, Result>>> OpenViolationsAsync(
        TimeTrackingDbContext context, IReadOnlyCollection<int> userIds, int orgId, DateOnly from, DateOnly to, DateTime? nowUtc = null)
    {
        var checks = await context.CoreHoursChecks.AsNoTracking()
            .Where(c => userIds.Contains(c.UserId) && c.OrganizationId == orgId && c.Date >= from && c.Date <= to
                     && c.Issues != CoreHoursIssue.None)
            .ToListAsync();
        if (checks.Count == 0) return [];

        var timeZone = await context.Organizations.AsNoTracking()
            .Where(o => o.Id == orgId).Select(o => o.TimeZone).FirstOrDefaultAsync();
        var clock = OrgClock.For(timeZone);
        var checkedUsers = checks.Select(c => c.UserId).Distinct().ToList();
        // Up to the end of the day after `to`: a night window ends there
        var covered = await CoveredAsync(context, checkedUsers, orgId,
            clock.StartOfDayUtc(from), clock.StartOfDayUtc(to.AddDays(2)), nowUtc ?? DateTime.UtcNow);

        var open = new Dictionary<int, Dictionary<DateOnly, Result>>();
        foreach (var check in checks)
        {
            var window = new CoreWindow(check.WindowStart, check.WindowEnd);
            var result = EvaluateDay(covered.Reaching(check.UserId, window.Utc(check.Date, clock)), check.Date, window,
                check.HalfDay, check.ToleranceMinutes, clock);
            if (result.Issues == CoreHoursIssue.None) continue;
            if (!open.TryGetValue(check.UserId, out var days)) open[check.UserId] = days = [];
            days[check.Date] = result with { Backfilled = check.Backfilled };
        }
        return open;
    }

    /// <summary>
    /// Checks every member day whose core window (+ tolerance) has ended and that has no
    /// <see cref="CoreHoursCheck"/> yet: today and up to <see cref="CatchUpDays"/> days back (org-local
    /// start days), so a restart or downtime catches up. Stores the checks and notifies violations once
    /// (in-app, plus mail when <paramref name="email"/> is given) to the member, the org admins/owners and
    /// the admins of the member's groups. A check more than <see cref="LateAfter"/> after its due time is
    /// <see cref="CoreHoursCheck.CheckedLate"/> and says so. A window that started before the member joined or
    /// before the core hours last changed (<see cref="Organization.CoreHoursChangedAt"/>) is never checked:
    /// that day was not (wholly) under this rule — switching on at 15:20 or joining at 14:00 notifies nothing
    /// that day. A requested backfill (<see cref="Organization.CoreHoursBackfillFrom"/>) flags exactly the days
    /// before the change silently (<see cref="CoreHoursCheck.Backfilled"/>) and is then cleared.
    /// The groups of every organization are loaded once; an organization with nothing due then costs one
    /// query, one with due days a few, independent of members and days. Each organization is saved on its
    /// own, and a failing one is logged and retried next run without blocking the others. Returns the number
    /// of notified violations.
    /// </summary>
    public static async Task<int> CheckDueAsync(TimeTrackingDbContext context, DateTime nowUtc,
        IEmailSender? email = null, string? frontendBaseUrl = null, CancellationToken ct = default, ILogger? logger = null)
    {
        var orgs = await context.Organizations.AsNoTracking()
            .Where(o => o.CoreHoursEnabled || o.CoreHoursBackfillFrom != null
                     || context.Groups.Any(g => g.OrganizationId == o.Id && g.IsActive && g.CoreHoursMode == CoreHoursMode.Own))
            .ToListAsync(ct);
        if (orgs.Count == 0) return 0;
        var orgIds = orgs.Select(o => o.Id).ToList();
        var groups = (await context.Groups.AsNoTracking()
                .Where(g => orgIds.Contains(g.OrganizationId) && g.IsActive)
                .Include(g => g.Members)
                .ToListAsync(ct))
            .ToLookup(g => g.OrganizationId);

        var notified = 0;
        foreach (var org in orgs)
        {
            try
            {
                notified += await CheckOrgAsync(context, org, new GroupTree(groups[org.Id]), nowUtc, email, frontendBaseUrl, ct);
            }
            catch (Exception ex) when (ex is not OperationCanceledException)
            {
                context.ChangeTracker.Clear();   // nothing of this organization stays pending; the next run retries it
                logger?.LogError(ex, "Error while checking the core working hours of organization {OrganizationId}.", org.Id);
            }
        }
        return notified;
    }

    /// <summary>One organization of <see cref="CheckDueAsync"/>: a single query when nothing is due.</summary>
    public static async Task<int> CheckOrgAsync(TimeTrackingDbContext context, Organization org, GroupTree tree, DateTime nowUtc,
        IEmailSender? email, string? frontendBaseUrl, CancellationToken ct)
    {
        var clock = OrgClock.For(org);
        var today = clock.Today(nowUtc);
        var catchUpFrom = today.AddDays(-CatchUpDays);
        var backfillFrom = org.CoreHoursBackfillFrom;
        var first = backfillFrom < catchUpFrom ? backfillFrom.Value : catchUpFrom;

        // The members with their days already checked, in one query
        var members = await context.UserOrganizations.AsNoTracking()
            .Where(uo => uo.OrganizationId == org.Id && uo.IsActive && uo.User.IsActive)
            .Select(uo => new
            {
                uo.UserId, uo.JoinedAt,
                Done = context.CoreHoursChecks
                    .Where(c => c.OrganizationId == org.Id && c.UserId == uo.UserId && c.Date >= first && c.Date <= today)
                    .Select(c => c.Date).ToList()
            })
            .ToListAsync(ct);

        var due = new List<CoreHoursCheck>();
        foreach (var member in members)
        {
            var groups = tree.EffectiveGroupsOf(member.UserId).ToList();
            var window = EffectiveRuleResolver.CombineCoreHours(org, groups);
            if (window == null) continue;
            var tolerance = EffectiveRuleResolver.CombineCoreTolerance(org, groups);

            for (var day = first; day <= today; day = day.AddDays(1))
            {
                if (member.Done.Contains(day)) continue;
                var (from, to) = window.Utc(day, clock);
                var dueAt = to.AddMinutes(tolerance);
                if (nowUtc < dueAt) continue;
                var beforeChange = from < org.CoreHoursChangedAt;
                var beforeJoining = from < member.JoinedAt;
                var backfill = day >= backfillFrom && beforeChange;
                if (backfill ? beforeJoining : day < catchUpFrom || beforeChange || beforeJoining)
                    continue;

                due.Add(new CoreHoursCheck
                {
                    OrganizationId = org.Id, UserId = member.UserId, Date = day,
                    WindowStart = window.Start, WindowEnd = window.End, ToleranceMinutes = tolerance,
                    CheckedAt = nowUtc, CheckedLate = !backfill && nowUtc >= dueAt + LateAfter, Backfilled = backfill
                });
            }
        }

        if (backfillFrom != null)
        {
            context.Organizations.Attach(org);   // the backfill request is handled
            org.CoreHoursBackfillFrom = null;
        }
        else if (due.Count == 0) return 0;

        if (due.Count > 0)
        {
            await EvaluateAsync(context, org, clock, due, nowUtc, ct);
            context.CoreHoursChecks.AddRange(due);
        }
        await context.SaveChangesAsync(ct);   // stored before notifying: a crash may skip a notice, never repeat one
        var violations = due.Where(c => c.Issues != CoreHoursIssue.None && !c.Backfilled).ToList();
        if (violations.Count == 0) return 0;

        // Recipients, batched: the org admins once, every user involved in one query.
        // Org admins only while they may see members' hours (MemberTimeEntryVisibility, #63).
        var orgAdmins = org.MemberTimeEntryVisibility
            ? await context.UserOrganizations.AsNoTracking()
                .Where(uo => uo.OrganizationId == org.Id && uo.IsActive && uo.Role >= OrganizationRole.Admin)
                .Select(uo => uo.UserId)
                .ToListAsync(ct)
            : [];
        var recipients = violations.Select(check =>
        {
            // The admins of the member's groups and of every ancestor group (sub-groups, GroupTree);
            // with the org switch off only those of groups whose admins may view entries (effective value)
            var groupAdmins = tree.ChainGroupsOf(check.UserId)
                .Where(id => org.MemberTimeEntryVisibility || tree.Effective(id).GroupAdminCanViewAllTimeEntries == true)
                .SelectMany(tree.AdminsOf);
            return orgAdmins.Concat(groupAdmins).Prepend(check.UserId).Distinct().ToList();
        }).ToList();
        var allIds = recipients.SelectMany(ids => ids).ToHashSet();
        var users = await context.Users.AsNoTracking()
            .Where(u => u.IsActive && allIds.Contains(u.Id))
            .ToDictionaryAsync(u => u.Id, ct);

        var mails = new List<(string To, string Subject, string Body)>();
        for (var i = 0; i < violations.Count; i++)
        {
            var check = violations[i];
            if (!users.TryGetValue(check.UserId, out var member)) continue;
            var window = new CoreWindow(check.WindowStart, check.WindowEnd);
            var date = check.Date.ToString("dd.MM.yyyy", CultureInfo.InvariantCulture);
            var name = $"{member.FirstName} {member.LastName}".Trim();
            var (issuesDe, issuesEn) = IssueTexts(check.Issues);
            var (lateDe, lateEn) = check.CheckedLate ? (" (nachträglich geprüft)", " " + CheckedLateMarker) : ("", "");
            var textEn = $"{name} did not keep the core working hours on {date} ({window}): {issuesEn}, {check.MissingMinutes} min not covered{lateEn}.";
            check.NotifiedAt = nowUtc;

            foreach (var userId in recipients[i])
            {
                if (!users.TryGetValue(userId, out var user)) continue;
                context.Notifications.Add(new Notification
                {
                    UserId = user.Id,
                    OrganizationId = org.Id,
                    Title = "Core working hours missed",
                    Message = textEn,
                    Type = NotificationType,
                    RelatedEntityId = check.UserId
                });
                var link = user.Id == check.UserId
                    ? $"{frontendBaseUrl}/history?date={check.Date:yyyy-MM-dd}"
                    : $"{frontendBaseUrl}/organizations/{org.Slug}/members/{check.UserId}";
                mails.Add((user.Email,
                    "Kernarbeitszeit nicht eingehalten / Core working hours missed",
                    $"{name} hat die Kernarbeitszeit am {date} ({window}) nicht eingehalten: {issuesDe}, {check.MissingMinutes} min nicht abgedeckt{lateDe}. / " +
                    $"{textEn}\n\n{link}"));
            }
        }
        await context.SaveChangesAsync(ct);

        if (email != null)
            foreach (var (to, subject, body) in mails)
                await NotificationMails.TrySendAsync(email, to, subject, body, ct: ct);
        return violations.Count;
    }

    private static (string De, string En) IssueTexts(CoreHoursIssue issues)
    {
        var de = new List<string>();
        var en = new List<string>();
        if (issues.HasFlag(CoreHoursIssue.Missing)) { de.Add("keine Zeit erfasst"); en.Add("no time tracked"); }
        if (issues.HasFlag(CoreHoursIssue.Late)) { de.Add("später begonnen"); en.Add("late start"); }
        if (issues.HasFlag(CoreHoursIssue.Early)) { de.Add("früher beendet"); en.Add("early leave"); }
        if (issues.HasFlag(CoreHoursIssue.Gap)) { de.Add("Lücke"); en.Add("gap"); }
        return (string.Join(", ", de), string.Join(", ", en));
    }

    /// <summary>
    /// Evaluates the new checks of one organization together (schedules, absences, holidays and entries:
    /// one query each). A day off (no working day, full holiday or full-day absence, or a half holiday
    /// plus a half-day absence) stays without issues; one exempt half checks one half of the window.
    /// </summary>
    private static async Task EvaluateAsync(TimeTrackingDbContext context, Organization org, OrgClock clock,
        List<CoreHoursCheck> checks, DateTime nowUtc, CancellationToken ct)
    {
        var orgId = org.Id;
        var userIds = checks.Select(c => c.UserId).Distinct().ToList();
        var first = checks.Min(c => c.Date);
        var last = checks.Max(c => c.Date);
        var schedules = (await context.WorkSchedules.AsNoTracking()
                .Where(ws => userIds.Contains(ws.UserId) && ws.OrganizationId == orgId)
                .ToListAsync(ct))
            .ToLookup(ws => ws.UserId);
        var absences = (await context.AbsenceDays.AsNoTracking()
                .Where(a => userIds.Contains(a.UserId) && a.OrganizationId == orgId && a.Date >= first && a.Date <= last)
                .ToListAsync(ct))
            .ToLookup(a => (a.UserId, a.Date));
        var holidays = await HolidayCalendar.LoadAsync(context, org, userIds, first, last, ct);
        var covered = await CoveredAsync(context, userIds, orgId,
            clock.StartOfDayUtc(first), clock.StartOfDayUtc(last.AddDays(2)), nowUtc);

        foreach (var check in checks)
        {
            if (!WorkingDays.IsWorkingDay(schedules[check.UserId], check.Date)) continue;
            var holiday = holidays.On(check.UserId, check.Date, schedules[check.UserId].ToList());
            var halves = (holiday == null ? 0 : holiday.IsHalfDay ? 1 : 2)
                + absences[(check.UserId, check.Date)].Sum(a => a.IsHalfDay ? 1 : 2);
            if (halves >= 2) continue;

            check.HalfDay = halves == 1;
            var window = new CoreWindow(check.WindowStart, check.WindowEnd);
            var result = EvaluateDay(covered.Reaching(check.UserId, window.Utc(check.Date, clock)), check.Date, window,
                check.HalfDay, check.ToleranceMinutes, clock);
            check.Issues = result.Issues;
            check.MissingMinutes = result.MissingMinutes;
        }
    }

    /// <summary>The members' covering intervals overlapping [fromUtc, toUtc): finished non-failure entries, running ones up to now.</summary>
    private static async Task<CoveredIntervals> CoveredAsync(TimeTrackingDbContext context,
        IReadOnlyCollection<int> userIds, int orgId, DateTime fromUtc, DateTime toUtc, DateTime nowUtc)
    {
        // Finished entries are bounded by their length (EntryTimes.LongestEntry), so the query seeks the
        // (UserId, StartTime) index instead of reading each member's whole history before toUtc
        var earliestStart = fromUtc - EntryTimes.LongestEntry;
        var entries = await context.TimeEntries.AsNoTracking()
            .Where(e => userIds.Contains(e.UserId) && e.OrganizationId == orgId && !e.IsFailure
                     && ((e.StartTime < toUtc && e.StartTime > earliestStart && e.EndTime > fromUtc)
                         || (e.IsRunning && e.StartTime < toUtc)))
            .Select(e => new { e.UserId, e.StartTime, e.EndTime, e.IsRunning })
            .ToListAsync();
        return new CoveredIntervals(entries.ToLookup(e => e.UserId, e => (e.StartTime, e.IsRunning ? nowUtc : e.EndTime ?? e.StartTime)));
    }

    /// <summary>
    /// The covering intervals per member, sorted by start, so a check only looks at the intervals that can reach
    /// its window (start within the member's longest interval before the window, up to its end) instead of all of
    /// them: O(log n + k) per check rather than O(n). <see cref="Evaluate"/> clips and drops whatever does not
    /// overlap anyway and its merge does not depend on the input order, so the result is the same as with the full list.
    /// </summary>
    private sealed class CoveredIntervals
    {
        private readonly Dictionary<int, ((DateTime Start, DateTime End)[] Sorted, TimeSpan Longest)> _byUser;

        public CoveredIntervals(ILookup<int, (DateTime Start, DateTime End)> intervals)
        {
            _byUser = intervals.ToDictionary(g => g.Key, g =>
            {
                var sorted = g.OrderBy(i => i.Start).ToArray();
                var longest = TimeSpan.Zero;
                foreach (var (start, end) in sorted)
                    if (end - start > longest) longest = end - start;
                return (sorted, longest);
            });
        }

        public List<(DateTime Start, DateTime End)> Reaching(int userId, (DateTime FromUtc, DateTime ToUtc) window)
        {
            var found = new List<(DateTime Start, DateTime End)>();
            if (!_byUser.TryGetValue(userId, out var user)) return found;
            var (sorted, longest) = user;
            var earliest = window.FromUtc - longest;
            // First interval starting at or after `earliest`
            int lo = 0, hi = sorted.Length;
            while (lo < hi)
            {
                var mid = (lo + hi) / 2;
                if (sorted[mid].Start < earliest) lo = mid + 1; else hi = mid;
            }
            for (var i = lo; i < sorted.Length && sorted[i].Start < window.ToUtc; i++)
                if (sorted[i].End > window.FromUtc) found.Add(sorted[i]);
            return found;
        }
    }
}
