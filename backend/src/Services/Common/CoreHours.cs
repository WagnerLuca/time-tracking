using System.Globalization;
using Microsoft.EntityFrameworkCore;
using TimeTracking.Api.Data;
using TimeTracking.Api.Models;

namespace TimeTracking.Api.Services;

/// <summary>A core working hours window in the organization's local time (start before end, same day).</summary>
public record CoreWindow(TimeOnly Start, TimeOnly End)
{
    public override string ToString() => $"{Start:HH:mm}-{End:HH:mm}";
}

/// <summary>
/// Core working hours (Kernarbeitszeit). The rules:
/// <list type="bullet">
/// <item>The window is the organization's, or the groups' override (<see cref="EffectiveRuleResolver.CombineCoreHours"/>).</item>
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

    public record Result(CoreHoursIssue Issues, int MissingMinutes, CoreWindow Window);

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
    /// Checks one local day. With <paramref name="halfDay"/> only one half of the window has to be covered:
    /// the result of the better covered half (no issues first, then fewer missing minutes, then the afternoon).
    /// </summary>
    public static Result EvaluateDay(IReadOnlyCollection<(DateTime Start, DateTime End)> covered, DateOnly day,
        CoreWindow window, bool halfDay, int toleranceMinutes, OrgClock clock)
    {
        CoreWindow[] parts = [window];
        if (halfDay)
        {
            var middle = window.Start.Add((window.End - window.Start) / 2);
            parts = [new CoreWindow(middle, window.End), new CoreWindow(window.Start, middle)];
        }

        return parts
            .Select(p =>
            {
                var (issues, missing) = Evaluate(covered,
                    clock.ToUtc(day.ToDateTime(p.Start)), clock.ToUtc(day.ToDateTime(p.End)), toleranceMinutes);
                return new Result(issues, missing, p);
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
    /// (a day the member has corrected since is left out), keyed by local day.
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
        var covered = await CoveredAsync(context, checkedUsers, orgId,
            clock.StartOfDayUtc(from), clock.StartOfDayUtc(to.AddDays(1)), nowUtc ?? DateTime.UtcNow);

        var open = new Dictionary<int, Dictionary<DateOnly, Result>>();
        foreach (var check in checks)
        {
            var result = EvaluateDay(covered[check.UserId].ToList(), check.Date, new CoreWindow(check.WindowStart, check.WindowEnd),
                check.HalfDay, check.ToleranceMinutes, clock);
            if (result.Issues == CoreHoursIssue.None) continue;
            if (!open.TryGetValue(check.UserId, out var days)) open[check.UserId] = days = [];
            days[check.Date] = result;
        }
        return open;
    }

    /// <summary>
    /// Checks every member whose core window (+ tolerance) ended today (org-local) and who has not been
    /// checked today, stores the check and notifies violations (in-app, plus mail when
    /// <paramref name="email"/> is given) to the member, the org admins/owners and the admins of the
    /// member's groups. Returns the number of violations.
    /// </summary>
    public static async Task<int> CheckDueAsync(TimeTrackingDbContext context, DateTime nowUtc,
        IEmailSender? email = null, string? frontendBaseUrl = null, CancellationToken ct = default)
    {
        var orgs = await context.Organizations.AsNoTracking()
            .Where(o => o.CoreHoursEnabled
                     || context.Groups.Any(g => g.OrganizationId == o.Id && g.IsActive && g.CoreHoursMode == CoreHoursMode.Own))
            .ToListAsync(ct);

        var added = 0;
        var violations = new List<(Organization Org, CoreHoursCheck Check)>();
        foreach (var org in orgs)
        {
            var clock = OrgClock.For(org);
            var day = clock.Today(nowUtc);
            var checkedUsers = context.CoreHoursChecks
                .Where(c => c.OrganizationId == org.Id && c.Date == day).Select(c => c.UserId);
            var pending = await context.UserOrganizations.AsNoTracking()
                .Where(uo => uo.OrganizationId == org.Id && uo.IsActive && uo.User.IsActive && !checkedUsers.Contains(uo.UserId))
                .Select(uo => uo.UserId)
                .ToListAsync(ct);
            if (pending.Count == 0) continue;

            var memberships = await context.GroupMembers.AsNoTracking()
                .Where(gm => gm.Group.OrganizationId == org.Id && gm.Group.IsActive && pending.Contains(gm.UserId))
                .Select(gm => new { gm.UserId, gm.Group })
                .ToListAsync(ct);
            Holiday? holiday = null;
            var holidayLoaded = false;

            foreach (var userId in pending)
            {
                var window = EffectiveRuleResolver.CombineCoreHours(org, memberships.Where(m => m.UserId == userId).Select(m => m.Group));
                if (window == null) continue;
                if (nowUtc < clock.ToUtc(day.ToDateTime(window.End)).AddMinutes(org.CoreHoursToleranceMinutes)) continue;

                if (!holidayLoaded)
                {
                    holiday = (await context.Holidays.AsNoTracking()
                            .Where(h => h.OrganizationId == org.Id && (h.IsRecurring || h.Date == day))
                            .ToListAsync(ct))
                        .FirstOrDefault(h => h.DateIn(day.Year) == day);
                    holidayLoaded = true;
                }

                var check = new CoreHoursCheck
                {
                    OrganizationId = org.Id, UserId = userId, Date = day,
                    WindowStart = window.Start, WindowEnd = window.End,
                    ToleranceMinutes = org.CoreHoursToleranceMinutes, CheckedAt = nowUtc
                };
                var halves = await ExemptHalvesAsync(context, org.Id, userId, day, holiday, ct);
                if (halves < 2)
                {
                    check.HalfDay = halves == 1;
                    var covered = await CoveredAsync(context, [userId], org.Id,
                        clock.ToUtc(day.ToDateTime(window.Start)), clock.ToUtc(day.ToDateTime(window.End)), nowUtc);
                    var result = EvaluateDay(covered[userId].ToList(), day, window, check.HalfDay, check.ToleranceMinutes, clock);
                    check.Issues = result.Issues;
                    check.MissingMinutes = result.MissingMinutes;
                }
                context.CoreHoursChecks.Add(check);
                added++;
                if (check.Issues != CoreHoursIssue.None) violations.Add((org, check));
            }
        }

        if (added == 0) return 0;
        await context.SaveChangesAsync(ct);   // stored before notifying: a crash may skip a notice, never repeat one
        if (violations.Count == 0) return 0;

        var mails = new List<(string To, string Subject, string Body)>();
        foreach (var (org, check) in violations)
        {
            var member = await context.Users.AsNoTracking().FirstAsync(u => u.Id == check.UserId, ct);
            var admins = await context.UserOrganizations.AsNoTracking()
                .Where(uo => uo.OrganizationId == org.Id && uo.IsActive && uo.Role >= OrganizationRole.Admin && uo.User.IsActive)
                .Select(uo => uo.User)
                .ToListAsync(ct);
            var groupAdmins = await context.GroupMembers.AsNoTracking()
                .Where(gm => gm.Role == GroupRole.Admin && gm.Group.OrganizationId == org.Id && gm.Group.IsActive
                          && gm.User.IsActive && gm.Group.Members.Any(m => m.UserId == check.UserId))
                .Select(gm => gm.User)
                .ToListAsync(ct);
            var recipients = admins.Concat(groupAdmins).Prepend(member).DistinctBy(u => u.Id).ToList();

            var result = new Result(check.Issues, check.MissingMinutes, new CoreWindow(check.WindowStart, check.WindowEnd));
            var date = check.Date.ToString("dd.MM.yyyy", CultureInfo.InvariantCulture);
            var name = $"{member.FirstName} {member.LastName}".Trim();
            var (issuesDe, issuesEn) = IssueTexts(check.Issues);
            check.NotifiedAt = nowUtc;

            foreach (var user in recipients)
            {
                context.Notifications.Add(new Notification
                {
                    UserId = user.Id,
                    OrganizationId = org.Id,
                    Title = "Core working hours missed",
                    Message = $"{name} did not keep the core working hours on {date} ({result.Window}): {issuesEn}, {check.MissingMinutes} min not covered.",
                    Type = NotificationType,
                    RelatedEntityId = check.UserId
                });
                var link = user.Id == check.UserId
                    ? $"{frontendBaseUrl}/history?date={check.Date:yyyy-MM-dd}"
                    : $"{frontendBaseUrl}/organizations/{org.Slug}/members/{check.UserId}";
                mails.Add((user.Email,
                    "Kernarbeitszeit nicht eingehalten / Core working hours missed",
                    $"{name} hat die Kernarbeitszeit am {date} ({result.Window}) nicht eingehalten: {issuesDe}, {check.MissingMinutes} min nicht abgedeckt. / " +
                    $"{name} did not keep the core working hours on {date} ({result.Window}): {issuesEn}, {check.MissingMinutes} min not covered.\n\n{link}"));
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
    /// How many halves of the day are exempt: 2 on a day off (no working day, full holiday or full-day
    /// absence), 1 for a half holiday or half-day absence, 2 for both together.
    /// </summary>
    private static async Task<int> ExemptHalvesAsync(TimeTrackingDbContext context, int orgId, int userId, DateOnly day,
        Holiday? holiday, CancellationToken ct)
    {
        var schedules = await context.WorkSchedules.AsNoTracking()
            .Where(ws => ws.UserId == userId && ws.OrganizationId == orgId)
            .ToListAsync(ct);
        if (!WorkingDays.IsWorkingDay(schedules, day)) return 2;

        var absence = await context.AbsenceDays.AsNoTracking()
            .FirstOrDefaultAsync(a => a.UserId == userId && a.OrganizationId == orgId && a.Date == day, ct);
        var halves = (holiday == null ? 0 : holiday.IsHalfDay ? 1 : 2) + (absence == null ? 0 : absence.IsHalfDay ? 1 : 2);
        return Math.Min(halves, 2);
    }

    /// <summary>The members' covering intervals overlapping [fromUtc, toUtc): finished non-failure entries, running ones up to now.</summary>
    private static async Task<ILookup<int, (DateTime Start, DateTime End)>> CoveredAsync(TimeTrackingDbContext context,
        IReadOnlyCollection<int> userIds, int orgId, DateTime fromUtc, DateTime toUtc, DateTime nowUtc)
    {
        var entries = await context.TimeEntries.AsNoTracking()
            .Where(e => userIds.Contains(e.UserId) && e.OrganizationId == orgId && !e.IsFailure
                     && e.StartTime < toUtc && (e.IsRunning || e.EndTime > fromUtc))
            .Select(e => new { e.UserId, e.StartTime, e.EndTime, e.IsRunning })
            .ToListAsync();
        return entries.ToLookup(e => e.UserId, e => (e.StartTime, e.IsRunning ? nowUtc : e.EndTime ?? e.StartTime));
    }
}
