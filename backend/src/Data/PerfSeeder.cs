using System.Globalization;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using Npgsql;
using NpgsqlTypes;
using TimeTracking.Api.Models;
using TimeTracking.Api.Models.Dtos;
using TimeTracking.Api.Services;

namespace TimeTracking.Api.Data;

/// <summary>What <see cref="PerfSeeder"/> generates. The slug defaults to <c>perf-{employees}-{years}</c>.</summary>
public sealed record PerfSeedOptions
{
    public int Employees { get; init; } = 100;
    public int Years { get; init; } = 10;
    /// <summary>Seed of the deterministic random numbers: same options + same end date = same history.</summary>
    public int Seed { get; init; } = 1;
    public string? OrgSlug { get; init; }
    /// <summary>Last (org-local) day of the history; default today. Entries end the day before, today only has the running timers.</summary>
    public DateOnly? EndDate { get; init; }
    /// <summary>Password of every seeded login; null = a random one (returned in the result, printed once by the command).</summary>
    public string? Password { get; init; }
    /// <summary>
    /// Core working hours on (09:00–12:00) with a check row per member and day, as the background job writes them.
    /// Off on a shared instance: the job would otherwise check the perf members every minute.
    /// </summary>
    public bool CoreHours { get; init; } = true;

    public string Slug => string.IsNullOrWhiteSpace(OrgSlug) ? $"perf-{Employees}-{Years}" : OrgSlug.Trim();
}

/// <summary>The seeded organization, who is in it and how many rows each table got.</summary>
public sealed record PerfSeedResult(
    int OrganizationId, string Slug, int OwnerId, IReadOnlyList<int> AdminIds, IReadOnlyList<int> MemberIds,
    IReadOnlyDictionary<string, int> Rows, TimeSpan Elapsed, string Password)
{
    public static string OwnerEmail(string slug) => $"perf-owner@{slug}.test";
    public static string AdminEmail(string slug, int n) => $"perf-admin{n}@{slug}.test";
    public static string MemberEmail(string slug, int n) => $"perf-user{n}@{slug}.test";
}

/// <summary>
/// Generates a realistic multi-year history for one organization on Postgres, for the scaling tests and
/// load tests of docs/PERFORMANCE_PLAN.md (Phase 0). Small tables (users, memberships, groups, schedules,
/// holidays, terminal key and cards) go through EF; the large ones are written with binary COPY. The data
/// keeps the app's invariants: UTC instants, entries ≤ 24 h that never overlap per user, automatic pauses
/// as <see cref="PauseRules"/> computes them, at most one running timer per user, unique days for absences
/// and core hours checks. Bypasses row-level security (sets <c>app.bypass_rls</c>; a superuser bypasses it anyway).
/// Members: an owner, up to 5 admins (<c>perf-admin{n}</c>) and members <c>perf-user{n}</c>, all with one password
/// (<see cref="PerfSeedOptions.Password"/>, else random); <c>perf-user1</c> joined at the start, works full time and
/// has no running timer. <see cref="DropAsync"/> removes such an organization again.
/// </summary>
public static class PerfSeeder
{

    private static readonly (double MinHours, int PauseMinutes)[] Rules = [(6, 30), (9, 45)];
    private static readonly TimeOnly CoreStart = new(9, 0), CoreEnd = new(12, 0);
    private const int CoreTolerance = 5;
    private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web);
    private static readonly string[] FirstNames =
        ["Anna", "Ben", "Clara", "David", "Elena", "Felix", "Greta", "Hannes", "Ida", "Jonas", "Klara", "Lukas", "Mia", "Noah", "Paula", "Tim"];

    public static async Task<PerfSeedResult> SeedAsync(NpgsqlConnection conn, PerfSeedOptions o, CancellationToken ct = default)
    {
        if (o.Employees < 3) throw new ArgumentException("At least 3 employees (owner, admin, member).", nameof(o));
        if (o.Years < 1) throw new ArgumentException("At least 1 year.", nameof(o));
        var watch = System.Diagnostics.Stopwatch.StartNew();
        if (conn.State != System.Data.ConnectionState.Open) await conn.OpenAsync(ct);
        await ExecuteAsync(conn, "SELECT set_config('app.bypass_rls', 'on', false)", ct);

        var clock = OrgClock.For(OrgClock.DefaultTimeZone);
        var today = clock.Today();
        var end = o.EndDate ?? today;
        var start = end.AddYears(-o.Years);
        var nowUtc = DateTime.UtcNow < clock.StartOfDayUtc(end.AddDays(1)) ? DateTime.UtcNow : clock.StartOfDayUtc(end.AddDays(1));
        var slug = o.Slug;
        var password = o.Password ?? Convert.ToBase64String(RandomNumberGenerator.GetBytes(12)).Replace('+', 'x').Replace('/', 'y');

        await using var tx = await conn.BeginTransactionAsync(ct);
        // COPY writes ids reserved from the identity sequences (ReserveIdsAsync): no other session may take
        // ids from them until the seed commits. Reads go on; the app's inserts into these tables wait.
        await ExecuteAsync(conn, """LOCK TABLE "TimeEntries", "OrgRequests" IN SHARE ROW EXCLUSIVE MODE""", ct);
        await using var db = new TimeTrackingDbContext(new DbContextOptionsBuilder<TimeTrackingDbContext>().UseNpgsql(conn).Options);
        await db.Database.UseTransactionAsync(tx, ct);
        if (await db.Organizations.IgnoreQueryFilters().AnyAsync(x => x.Slug == slug, ct))
            throw new InvalidOperationException($"Organization '{slug}' already exists.");

        // ── Organization, people, groups, schedules, holidays (EF) ──────────────
        var startUtc = clock.StartOfDayUtc(start);
        var org = new Organization
        {
            Name = $"Perf {o.Employees}x{o.Years}", Slug = slug, CreatedAt = startUtc, UpdatedAt = startUtc,
            MemberTimeEntryVisibility = true, DefaultVacationDays = 30,
            CoreHoursEnabled = o.CoreHours, CoreHoursStart = CoreStart, CoreHoursEnd = CoreEnd, CoreHoursToleranceMinutes = CoreTolerance,
            CoreHoursChangedAt = startUtc
        };
        db.Organizations.Add(org);
        db.PauseRules.AddRange(Rules.Select(r => new PauseRule { Organization = org, MinHours = r.MinHours, PauseMinutes = r.PauseMinutes, CreatedAt = startUtc }));
        var holidays = GermanHolidays(start.Year, end.Year + 1).Select(h => new Holiday
            { Organization = org, Name = h.Name, Date = h.Date, IsRecurring = h.Recurring, IsHalfDay = h.Half, CreatedAt = startUtc }).ToList();
        db.Holidays.AddRange(holidays);

        var hash = BCrypt.Net.BCrypt.HashPassword(password);
        var admins = Math.Clamp(o.Employees / 20, 1, 5);
        var people = new List<Person>();
        for (var i = 0; i < o.Employees; i++)
        {
            var rng = new Random(o.Seed * 100_003 + i);
            var (role, email, number) = i == 0 ? (OrganizationRole.Owner, PerfSeedResult.OwnerEmail(slug), 0)
                : i <= admins ? (OrganizationRole.Admin, PerfSeedResult.AdminEmail(slug, i), i)
                : (OrganizationRole.Member, PerfSeedResult.MemberEmail(slug, i - admins), i - admins);
            var memberNo = role == OrganizationRole.Member ? number : 0;
            // ≈ 20 % join later (never perf-user1, the scaling tests' member), at least a month before the end
            var joined = memberNo != 1 && role == OrganizationRole.Member && rng.NextDouble() < 0.2
                ? start.AddDays(rng.Next(1, Math.Max(2, end.DayNumber - start.DayNumber - 30)))
                : start;
            var joinedUtc = clock.StartOfDayUtc(joined);
            var user = new User
            {
                Email = email, FirstName = FirstNames[i % FirstNames.Length], LastName = role switch
                {
                    OrganizationRole.Owner => "Owner", OrganizationRole.Admin => $"Admin{number}", _ => $"Perf{number}"
                },
                PasswordHash = hash, EmailConfirmed = true, CreatedAt = joinedUtc, UpdatedAt = joinedUtc
            };
            var membership = new UserOrganization
            {
                User = user, Organization = org, Role = role, JoinedAt = joinedUtc, VacationDaysPerYear = 30,
                InitialOvertimeHours = rng.NextDouble() < 0.3 ? Math.Round(rng.NextDouble() * 20, 1) : 0,
                PersonnelNumber = $"P{i + 1:00000}"
            };
            db.Users.Add(user);
            db.UserOrganizations.Add(membership);
            var person = new Person(user, membership, role, memberNo, joined, rng);

            // Mon–Fri 8 h (≈ 15 % part-time 6 h), one change every ~3 years (8 ↔ 7, 6 ↔ 5)
            var daily = memberNo != 1 && rng.NextDouble() < 0.15 ? 6.0 : 8.0;
            var from = joined;
            while (from < end)
            {
                var next = from.AddDays(rng.Next(900, 1280));
                var schedule = new WorkSchedule
                {
                    User = user, Organization = org, ValidFrom = from, ValidTo = next < end ? next.AddDays(-1) : null,
                    WeeklyWorkHours = daily * 5, TargetMon = daily, TargetTue = daily, TargetWed = daily, TargetThu = daily, TargetFri = daily,
                    CreatedAt = clock.StartOfDayUtc(from)
                };
                person.Schedules.Add(schedule);
                db.WorkSchedules.Add(schedule);
                daily = daily is 8 or 6 ? daily - 1 : daily + 1;
                from = next;
            }
            people.Add(person);
        }
        var members = people.Where(p => p.Role == OrganizationRole.Member).ToList();
        // Two open running timers (never perf-user1)
        foreach (var p in members.Where(p => p.MemberNo != 1).TakeLast(2)) p.Running = true;

        // 2 departments × 3 teams × 2 sub-teams; members in the sub-teams, admins lead the departments, the first of each team leads it
        var depts = Enumerable.Range(1, 2).Select(d => new Group
        {
            Organization = org, Name = $"Abteilung {d}", CreatedAt = startUtc, UpdatedAt = startUtc,
            GroupAdminCanApproveAbsences = true, GroupAdminCanViewAllTimeEntries = true
        }).ToList();
        var teams = Enumerable.Range(0, 6).Select(t => new Group
            { Organization = org, Name = $"Team {t + 1}", ParentGroup = depts[t / 3], CreatedAt = startUtc, UpdatedAt = startUtc }).ToList();
        var subTeams = Enumerable.Range(0, 12).Select(s => new Group
            { Organization = org, Name = $"Team {s / 2 + 1}{(char)('a' + s % 2)}", ParentGroup = teams[s / 2], CreatedAt = startUtc, UpdatedAt = startUtc }).ToList();
        db.Groups.AddRange(depts.Concat(teams).Concat(subTeams));
        for (var i = 1; i < people.Count; i++)
            db.GroupMembers.Add(new GroupMember { Group = subTeams[i % 12], User = people[i].User, JoinedAt = people[i].Membership.JoinedAt });
        for (var a = 1; a <= admins; a++)
            db.GroupMembers.Add(new GroupMember { Group = depts[(a - 1) % 2], User = people[a].User, Role = GroupRole.Admin, JoinedAt = startUtc });
        for (var t = 0; t < 6; t++)
            if (members.FirstOrDefault(m => people.IndexOf(m) % 12 / 2 == t) is { } lead)
                db.GroupMembers.Add(new GroupMember { Group = teams[t], User = lead.User, Role = GroupRole.Admin, JoinedAt = lead.Membership.JoinedAt });

        // A terminal at the entrance; ≈ half of the people badge there (punch log only for the last 400 days, like retention)
        var apiKey = new ApiKey
        {
            Organization = org, Name = "Eingang", KeyPrefix = "tt_perfsee", CreatedAt = startUtc, CreatedByUserId = 0,
            KeyHash = Convert.ToHexString(SHA256.HashData(RandomNumberGenerator.GetBytes(32)))
        };
        await db.SaveChangesAsync(ct);
        apiKey.CreatedByUserId = people[0].User.Id;
        db.ApiKeys.Add(apiKey);
        foreach (var p in people.Where((_, i) => i % 2 == 0))
        {
            var value = CardIds.Normalize($"PERF{org.Id:X4}{p.User.Id:X6}");
            p.Card = new BadgeCard
            {
                OrganizationId = org.Id, UserId = p.User.Id, CardHash = CardIds.Hash(value), CardHint = CardIds.Hint(value),
                Label = "Chip", CreatedAt = clock.StartOfDayUtc(p.Joined)
            };
            db.BadgeCards.Add(p.Card);
        }
        await db.SaveChangesAsync(ct);

        // Core hours notices as CoreHours.CheckOrgAsync sends them (MemberTimeEntryVisibility is on): the member,
        // every org admin and the admins of the member's groups and of their parents
        var orgAdminIds = people.Where(p => p.Role != OrganizationRole.Member).Select(p => p.User.Id).ToList();
        for (var i = 0; i < people.Count; i++)
        {
            var groupAdmins = new List<int>();
            if (i > 0)
            {
                var team = i % 12 / 2;
                groupAdmins.AddRange(Enumerable.Range(1, admins).Where(a => (a - 1) % 2 == team / 3).Select(a => people[a].User.Id));
                if (members.FirstOrDefault(m => people.IndexOf(m) % 12 / 2 == team) is { } lead) groupAdmins.Add(lead.User.Id);
            }
            people[i].CoreNoticeTo = orgAdminIds.Concat(groupAdmins).Prepend(people[i].User.Id).Distinct().ToArray();
        }

        // ── History (generated in memory, written with COPY) ────────────────────
        var fullHolidays = new HashSet<DateOnly>();
        var halfHolidays = new HashSet<DateOnly>();
        for (var y = start.Year; y <= end.Year + 1; y++)
            foreach (var h in holidays)
                if (h.DateIn(y) is { } d) (h.IsHalfDay ? halfHolidays : fullHolidays).Add(d);

        var data = new History();
        var adminIds = people.Where(p => p.Role != OrganizationRole.Member).Select(p => p.User.Id).ToArray();
        foreach (var p in people)
            Generate(p, data, org.Id, adminIds, o.CoreHours, clock, start, end, today, nowUtc, fullHolidays, halfHolidays);

        var entryIds = await ReserveIdsAsync(conn, "TimeEntries", data.Entries.Count, ct);
        var requestIds = await ReserveIdsAsync(conn, "OrgRequests", data.Requests.Count, ct);
        for (var i = 0; i < data.Entries.Count; i++) data.Entries[i].Id = entryIds + i;
        for (var i = 0; i < data.Requests.Count; i++) data.Requests[i].Request.Id = requestIds + i;
        foreach (var (absence, request) in data.Absences) absence.CreatedViaRequestId = request?.Request.Id;

        var rows = new Dictionary<string, int>
        {
            ["Users"] = people.Count, ["WorkSchedules"] = people.Sum(p => p.Schedules.Count), ["Holidays"] = holidays.Count,
            ["Groups"] = depts.Count + teams.Count + subTeams.Count
        };
        rows["TimeEntries"] = await CopyAsync(conn, "TimeEntries",
            ["Id", "UserId", "OrganizationId", "StartTime", "EndTime", "IsRunning", "PauseDurationMinutes", "PauseIsManual", "IsFailure", "Source", "CreatedAt", "UpdatedAt"],
            data.Entries, (w, e) =>
            {
                w.Write(e.Id, NpgsqlDbType.Integer);
                w.Write(e.UserId, NpgsqlDbType.Integer);
                w.Write(org.Id, NpgsqlDbType.Integer);
                w.Write(e.StartTime, NpgsqlDbType.TimestampTz);
                WriteNullable(w, e.EndTime, NpgsqlDbType.TimestampTz);
                w.Write(e.IsRunning, NpgsqlDbType.Boolean);
                w.Write(e.PauseDurationMinutes, NpgsqlDbType.Integer);
                w.Write(e.PauseIsManual, NpgsqlDbType.Boolean);
                w.Write(e.IsFailure, NpgsqlDbType.Boolean);
                w.Write((int)e.Source, NpgsqlDbType.Integer);
                w.Write(e.CreatedAt, NpgsqlDbType.TimestampTz);
                w.Write(e.UpdatedAt, NpgsqlDbType.TimestampTz);
            }, ct);
        rows["TimeEntryChanges"] = await CopyAsync(conn, "TimeEntryChanges",
            ["TimeEntryId", "UserId", "OrganizationId", "ChangedByUserId", "ChangedAt", "Kind", "Changes", "InClosedPeriod"],
            data.Changes, (w, c) =>
            {
                w.Write(c.Entry.Id, NpgsqlDbType.Integer);
                w.Write(c.Entry.UserId, NpgsqlDbType.Integer);
                w.Write(org.Id, NpgsqlDbType.Integer);
                w.Write(c.ChangedBy, NpgsqlDbType.Integer);
                w.Write(c.ChangedAt, NpgsqlDbType.TimestampTz);
                w.Write((int)c.Kind, NpgsqlDbType.Integer);
                w.Write(JsonSerializer.Serialize(c.Changes, Json), NpgsqlDbType.Text);
                w.Write(false, NpgsqlDbType.Boolean);
            }, ct);
        rows["OrgRequests"] = await CopyAsync(conn, "OrgRequests",
            ["Id", "UserId", "OrganizationId", "Type", "Status", "Message", "RequestData", "CreatedAt", "RespondedAt", "RespondedByUserId", "ResponseReason", "UserNotifiedAt"],
            data.Requests, (w, r) =>
            {
                var q = r.Request;
                w.Write(q.Id, NpgsqlDbType.Integer);
                w.Write(q.UserId, NpgsqlDbType.Integer);
                w.Write(org.Id, NpgsqlDbType.Integer);
                w.Write((int)q.Type, NpgsqlDbType.Integer);
                w.Write((int)q.Status, NpgsqlDbType.Integer);
                WriteNullable(w, q.Message, NpgsqlDbType.Varchar);
                WriteNullable(w, q.RequestData, NpgsqlDbType.Varchar);
                w.Write(q.CreatedAt, NpgsqlDbType.TimestampTz);
                WriteNullable(w, q.RespondedAt, NpgsqlDbType.TimestampTz);
                WriteNullable(w, q.RespondedByUserId, NpgsqlDbType.Integer);
                WriteNullable(w, q.ResponseReason, NpgsqlDbType.Varchar);
                WriteNullable(w, q.UserNotifiedAt, NpgsqlDbType.TimestampTz);
            }, ct);
        rows["AbsenceDays"] = await CopyAsync(conn, "AbsenceDays",
            ["UserId", "OrganizationId", "Date", "Type", "IsHalfDay", "CreatedAt", "CreatedByUserId", "CreatedViaRequestId"],
            data.Absences.Select(a => a.Absence), (w, a) =>
            {
                w.Write(a.UserId, NpgsqlDbType.Integer);
                w.Write(org.Id, NpgsqlDbType.Integer);
                w.Write(a.Date, NpgsqlDbType.Date);
                w.Write((int)a.Type, NpgsqlDbType.Integer);
                w.Write(a.IsHalfDay, NpgsqlDbType.Boolean);
                w.Write(a.CreatedAt, NpgsqlDbType.TimestampTz);
                WriteNullable(w, a.CreatedByUserId, NpgsqlDbType.Integer);
                WriteNullable(w, a.CreatedViaRequestId, NpgsqlDbType.Integer);
            }, ct);
        rows["CoreHoursChecks"] = await CopyAsync(conn, "CoreHoursChecks",
            ["OrganizationId", "UserId", "Date", "WindowStart", "WindowEnd", "HalfDay", "ToleranceMinutes", "Issues", "MissingMinutes", "CheckedAt", "NotifiedAt", "CheckedLate", "Backfilled"],
            data.Checks, (w, c) =>
            {
                w.Write(org.Id, NpgsqlDbType.Integer);
                w.Write(c.UserId, NpgsqlDbType.Integer);
                w.Write(c.Date, NpgsqlDbType.Date);
                w.Write(CoreStart, NpgsqlDbType.Time);
                w.Write(CoreEnd, NpgsqlDbType.Time);
                w.Write(c.HalfDay, NpgsqlDbType.Boolean);
                w.Write(CoreTolerance, NpgsqlDbType.Integer);
                w.Write((int)c.Issues, NpgsqlDbType.Integer);
                w.Write(c.MissingMinutes, NpgsqlDbType.Integer);
                w.Write(c.CheckedAt, NpgsqlDbType.TimestampTz);
                WriteNullable(w, c.NotifiedAt, NpgsqlDbType.TimestampTz);
                w.Write(false, NpgsqlDbType.Boolean);
                w.Write(false, NpgsqlDbType.Boolean);
            }, ct);
        rows["TerminalPunches"] = await CopyAsync(conn, "TerminalPunches",
            ["OrganizationId", "ApiKeyId", "BadgeCardId", "UserId", "CardHash", "CardHint", "Result", "ScannedAt", "ReceivedAt", "TimeEntryId"],
            data.Punches, (w, p) =>
            {
                w.Write(org.Id, NpgsqlDbType.Integer);
                w.Write(apiKey.Id, NpgsqlDbType.Integer);
                w.Write(p.Card.Id, NpgsqlDbType.Integer);
                w.Write(p.Card.UserId, NpgsqlDbType.Integer);
                w.Write(p.Card.CardHash, NpgsqlDbType.Varchar);
                w.Write(p.Card.CardHint, NpgsqlDbType.Varchar);
                w.Write((int)p.Result, NpgsqlDbType.Integer);
                w.Write(p.At, NpgsqlDbType.TimestampTz);
                w.Write(p.At, NpgsqlDbType.TimestampTz);
                w.Write(p.Entry.Id, NpgsqlDbType.Integer);
            }, ct);
        rows["Notifications"] = await CopyAsync(conn, "Notifications",
            ["UserId", "OrganizationId", "Title", "Message", "Type", "IsRead", "RelatedEntityId", "CreatedAt"],
            data.Notifications, (w, n) =>
            {
                w.Write(n.UserId, NpgsqlDbType.Integer);
                w.Write(org.Id, NpgsqlDbType.Integer);
                w.Write(n.Title, NpgsqlDbType.Varchar);
                w.Write(n.Message, NpgsqlDbType.Varchar);
                w.Write(n.Type, NpgsqlDbType.Varchar);
                w.Write(n.IsRead, NpgsqlDbType.Boolean);
                WriteNullable(w, n.RelatedEntityId ?? n.Request?.Request.Id, NpgsqlDbType.Integer);
                w.Write(n.CreatedAt, NpgsqlDbType.TimestampTz);
            }, ct);
        await tx.CommitAsync(ct);

        // Fresh statistics, so the planner sees the history like a database that grew into it
        await ExecuteAsync(conn, """ANALYZE "TimeEntries", "TimeEntryChanges", "OrgRequests", "AbsenceDays", "CoreHoursChecks", "TerminalPunches", "Notifications" """, ct);

        return new PerfSeedResult(org.Id, slug, people[0].User.Id,
            people.Where(p => p.Role == OrganizationRole.Admin).Select(p => p.User.Id).ToList(),
            members.Select(p => p.User.Id).ToList(), rows, watch.Elapsed, password);
    }

    // ── Generation ──────────────────────────────────────────────────────────

    private sealed class Person(User user, UserOrganization membership, OrganizationRole role, int memberNo, DateOnly joined, Random rng)
    {
        public User User { get; } = user;
        public UserOrganization Membership { get; } = membership;
        public OrganizationRole Role { get; } = role;
        public int MemberNo { get; } = memberNo;
        public DateOnly Joined { get; } = joined;
        public Random Rng { get; } = rng;
        public List<WorkSchedule> Schedules { get; } = [];
        public BadgeCard? Card { get; set; }
        public bool Running { get; set; }
        /// <summary>Who gets a core hours notice about this person: themself, the org admins and the admins of their groups and parents.</summary>
        public int[] CoreNoticeTo { get; set; } = [];

        public double TargetOn(DateOnly day) =>
            Schedules.LastOrDefault(s => s.ValidFrom <= day && (s.ValidTo == null || s.ValidTo >= day))?.TargetFor(day.DayOfWeek) ?? 0;
    }

    private sealed record Change(TimeEntry Entry, TimeEntryChangeKind Kind, List<TimeEntryFieldChange> Changes, int ChangedBy, DateTime ChangedAt);
    private sealed record Punch(BadgeCard Card, TimeEntry Entry, PunchResult Result, DateTime At);
    private sealed record Check(int UserId, DateOnly Date, bool HalfDay, CoreHoursIssue Issues, int MissingMinutes, DateTime CheckedAt, DateTime? NotifiedAt);
    private sealed class Req(OrgRequest request) { public OrgRequest Request { get; } = request; }
    private sealed record Note(int UserId, string Title, string Message, string Type, bool IsRead, int? RelatedEntityId, Req? Request, DateTime CreatedAt);

    private sealed class History
    {
        public List<TimeEntry> Entries { get; } = [];
        public List<Change> Changes { get; } = [];
        public List<Punch> Punches { get; } = [];
        public List<Check> Checks { get; } = [];
        public List<Req> Requests { get; } = [];
        public List<(AbsenceDay Absence, Req? Request)> Absences { get; } = [];
        public List<Note> Notifications { get; } = [];
    }

    private static void Generate(Person p, History data, int orgId, int[] approvers, bool coreHours, OrgClock clock,
        DateOnly start, DateOnly end, DateOnly today, DateTime nowUtc, HashSet<DateOnly> fullHolidays, HashSet<DateOnly> halfHolidays)
    {
        var rng = p.Rng;
        var userId = p.User.Id;
        bool IsWorkday(DateOnly d) => p.TargetOn(d) > 0 && !fullHolidays.Contains(d);
        DateTime Local(DateOnly d, int minutes) => clock.ToUtc(d.ToDateTime(TimeOnly.MinValue).AddMinutes(minutes));

        // Absences: ≈ 28 vacation days/year in blocks of 1–10 (single days often half), ≈ 8 sick days in blocks of 1–5
        var absences = new SortedDictionary<DateOnly, (AbsenceType Type, bool Half)>();
        void Place(DateOnly from, DateOnly to, AbsenceType type, double total, int maxBlock)
        {
            var span = to.DayNumber - from.DayNumber + 1;
            var placed = 0.0;
            for (var attempt = 0; placed < total - 0.25 && attempt < 300 && span > 0; attempt++)
            {
                var length = Math.Max(1, Math.Min(rng.Next(1, maxBlock + 1), (int)Math.Ceiling(total - placed)));
                var half = type == AbsenceType.Vacation && length == 1 && rng.NextDouble() < 0.35;
                var day = from.AddDays(rng.Next(span));
                if (absences.ContainsKey(day) || !IsWorkday(day)) continue;
                for (var n = 0; n < length && day <= to; day = day.AddDays(1))
                {
                    if (!IsWorkday(day)) continue;
                    if (absences.ContainsKey(day)) break;
                    absences[day] = (type, half);
                    placed += half ? 0.5 : 1;
                    n++;
                }
            }
        }
        for (var y = p.Joined.Year; y <= end.Year; y++)
        {
            var from = new DateOnly(y, 1, 1) < p.Joined ? p.Joined : new DateOnly(y, 1, 1);
            var to = new DateOnly(y, 12, 31) < end.AddDays(-1) ? new DateOnly(y, 12, 31) : end.AddDays(-1);
            if (from > to) continue;
            var fraction = (to.DayNumber - from.DayNumber + 1) / 365.0;
            Place(from, to, AbsenceType.Vacation, Math.Round(28 * fraction), 10);
            Place(from, to, AbsenceType.SickDay, Math.Round(8 * fraction), 5);
        }
        if (rng.NextDouble() < 0.3)   // planned vacation
        {
            var from = end.AddDays(rng.Next(14, 60));
            Place(from, from.AddDays(7), AbsenceType.Vacation, 5, 5);
        }

        // Entries, edits and terminal punches per working day; with core hours one check row per calendar day,
        // like CoreHours.CheckOrgAsync writes them (days off: no issues; a half day off: half the window)
        var terminalFrom = end.AddDays(-395);
        var name = $"{p.User.FirstName} {p.User.LastName}";
        void CoreCheck(DateOnly d, bool halfDay, CoreHoursIssue issues, int missing)
        {
            if (!coreHours) return;
            var checkedAt = Local(d, CoreEnd.Hour * 60 + CoreTolerance + 1);
            var notified = issues != CoreHoursIssue.None ? checkedAt : (DateTime?)null;
            data.Checks.Add(new Check(userId, d, halfDay, issues, missing, checkedAt, notified));
            if (notified == null) return;
            var text = issues == CoreHoursIssue.Missing ? "no time tracked" : "late start";
            foreach (var to in p.CoreNoticeTo)
                data.Notifications.Add(new Note(to, "Core working hours missed",
                    $"{name} did not keep the core working hours on {d:dd.MM.yyyy} ({CoreStart:HH:mm}-{CoreEnd:HH:mm}): {text}, {missing} min not covered.",
                    CoreHours.NotificationType, checkedAt < nowUtc.AddDays(-14), userId, null, checkedAt));
        }
        for (var d = p.Joined; d < end; d = d.AddDays(1))
        {
            var target = p.TargetOn(d);
            absences.TryGetValue(d, out var absence);
            var isAbsent = absences.ContainsKey(d);
            var offHalves = (fullHolidays.Contains(d) ? 2 : halfHolidays.Contains(d) ? 1 : 0) + (isAbsent ? absence.Half ? 1 : 2 : 0);
            if (target <= 0 || offHalves >= 2)
            {
                CoreCheck(d, false, CoreHoursIssue.None, 0);
                continue;
            }
            var half = offHalves == 1;
            var late = !half && rng.NextDouble() < 0.03;
            var startMin = late ? rng.Next(9 * 60 + 6, 9 * 60 + 31) : rng.Next(7 * 60, 9 * 60 + 1);
            // Never leave before the (half) window ends: the only violations are the late starts and open failure entries
            var coveredUntil = half ? CoreStart.Hour * 60 + (CoreEnd.Hour - CoreStart.Hour) * 30 : CoreEnd.Hour * 60;
            var net = Math.Max((int)Math.Round((half ? target / 2 : target) * 60) + rng.Next(-45, 61), coveredUntil + 10 - startMin);
            var source = p.Card != null && d >= terminalFrom ? TimeEntrySource.Terminal
                : rng.NextDouble() < 0.02 ? TimeEntrySource.Manual : TimeEntrySource.App;

            // ≈ 1 % failure entries (auto-stopped at the business day end, 00:00); the recent ones are still open, older ones corrected
            if (rng.NextDouble() < 0.01)
            {
                var failureStart = Local(d, startMin);
                var midnight = clock.StartOfDayUtc(d.AddDays(1));
                if (d >= end.AddDays(-60))
                {
                    data.Entries.Add(new TimeEntry
                    {
                        UserId = userId, OrganizationId = orgId, StartTime = failureStart, EndTime = midnight, IsFailure = true,
                        Source = TimeEntrySource.App, CreatedAt = failureStart, UpdatedAt = midnight
                    });
                    // A failure entry covers nothing
                    CoreCheck(d, half, CoreHoursIssue.Missing, (CoreEnd.Hour - CoreStart.Hour) * (half ? 30 : 60));
                    continue;
                }
                var corrected = AddDay(p, data, orgId, d, startMin, net, false, TimeEntrySource.App, clock, rng).Last();
                data.Changes.Add(new Change(corrected, TimeEntryChangeKind.Updated,
                    [
                        new(nameof(TimeEntry.EndTime), Iso(midnight), Iso(corrected.EndTime!.Value)),
                        new(nameof(TimeEntry.IsFailure), "true", "false")
                    ], userId, Min(midnight.AddHours(rng.Next(8, 72)), nowUtc)));
            }
            else
            {
                var entries = AddDay(p, data, orgId, d, startMin, net, !half && rng.NextDouble() < 0.3, source, clock, rng);
                if (source == TimeEntrySource.Manual)
                    foreach (var e in entries)
                        data.Changes.Add(new Change(e, TimeEntryChangeKind.Created,
                            [new(nameof(TimeEntry.StartTime), null, Iso(e.StartTime)), new(nameof(TimeEntry.EndTime), null, Iso(e.EndTime!.Value))],
                            userId, Min(e.EndTime!.Value.AddHours(rng.Next(1, 48)), nowUtc)));
                else if (rng.NextDouble() < 0.05)   // ≈ 5 % edited afterwards (by the member, sometimes an admin)
                {
                    var e = entries[0];
                    data.Changes.Add(new Change(e, TimeEntryChangeKind.Updated,
                        [new(nameof(TimeEntry.StartTime), Iso(e.StartTime.AddMinutes(15)), Iso(e.StartTime))],
                        rng.NextDouble() < 0.2 ? approvers[rng.Next(approvers.Length)] : userId,
                        Min(e.EndTime!.Value.AddHours(rng.Next(1, 72)), nowUtc)));
                }
                if (source == TimeEntrySource.Terminal)
                    foreach (var e in entries)
                    {
                        data.Punches.Add(new Punch(p.Card!, e, PunchResult.Started, e.StartTime));
                        data.Punches.Add(new Punch(p.Card!, e, PunchResult.Stopped, e.EndTime!.Value));
                    }
            }
            CoreCheck(d, half, late ? CoreHoursIssue.Late : CoreHoursIssue.None, late ? startMin - CoreStart.Hour * 60 : 0);
        }
        if (p.Running && end == today)
        {
            var started = Min(Local(today, 7 * 60 + 30), nowUtc.AddMinutes(-10));
            data.Entries.Add(new TimeEntry
            {
                UserId = userId, OrganizationId = orgId, StartTime = started, IsRunning = true, Source = TimeEntrySource.App,
                CreatedAt = started, UpdatedAt = started
            });
        }

        // One request per month: vacation blocks accepted, otherwise a declined day off; the last two weeks still pending
        var requestOf = new Dictionary<DateOnly, Req>();
        for (var month = new DateOnly(p.Joined.Year, p.Joined.Month, 1); month <= end; month = month.AddMonths(1))
        {
            var first = month < p.Joined ? p.Joined : month;
            var last = month.AddMonths(1).AddDays(-1) < end ? month.AddMonths(1).AddDays(-1) : end;
            var created = first.AddDays(rng.Next(last.DayNumber - first.DayNumber + 1));
            var approver = approvers[rng.Next(approvers.Length)];
            if (approver == userId) approver = approvers[0] == userId && approvers.Length > 1 ? approvers[1] : approvers[0];
            OrgRequest request;
            List<DateOnly> dates;
            if (created >= end.AddDays(-13))
            {
                var day = end.AddDays(rng.Next(7, 40));
                while (!IsWorkday(day) || absences.ContainsKey(day)) day = day.AddDays(1);
                dates = [day];
                request = Request(userId, orgId, RequestStatus.Pending, dates, false, Min(Local(created, 10 * 60), nowUtc));
            }
            else if (absences.Where(a => a.Key >= month && a.Key < month.AddMonths(1) && a.Value.Type == AbsenceType.Vacation && !requestOf.ContainsKey(a.Key))
                         .Select(a => (DateOnly?)a.Key).FirstOrDefault() is { } blockStart)
            {
                dates = [];
                for (var day = blockStart; day <= blockStart.AddDays(20); day = day.AddDays(1))
                {
                    if (!IsWorkday(day)) continue;
                    if (!absences.TryGetValue(day, out var a) || a.Type != AbsenceType.Vacation) break;
                    dates.Add(day);
                }
                var at = Local(blockStart.AddDays(-14) < p.Joined ? p.Joined : blockStart.AddDays(-14), 10 * 60);
                request = Request(userId, orgId, RequestStatus.Accepted, dates, absences[blockStart].Half, Min(at, nowUtc.AddHours(-2)));
                Decide(request, approver, null, nowUtc, rng);
            }
            else
            {
                var day = created;
                while (!IsWorkday(day) || absences.ContainsKey(day)) day = day.AddDays(1);
                dates = [day];
                request = Request(userId, orgId, RequestStatus.Declined, dates, false, Min(Local(created, 10 * 60), nowUtc.AddHours(-2)));
                Decide(request, approver, "Engpass im Team", nowUtc, rng);
            }
            var req = new Req(request);
            data.Requests.Add(req);
            if (request.Status == RequestStatus.Accepted)
                foreach (var day in dates) requestOf[day] = req;
        }
        foreach (var (day, (type, half)) in absences)
        {
            var req = requestOf.GetValueOrDefault(day);
            data.Absences.Add((new AbsenceDay
            {
                UserId = userId, OrganizationId = orgId, Date = day, Type = type, IsHalfDay = half,
                CreatedAt = req?.Request.CreatedAt ?? Min(Local(day, 8 * 60), nowUtc),
                CreatedByUserId = req?.Request.RespondedByUserId ?? userId
            }, req));
        }

        // The decision on each request (as the app notifies it), plus ≈ 2 other notifications per week; read except the last two weeks
        foreach (var req in data.Requests.Where(r => r.Request.UserId == userId && r.Request.RespondedAt != null))
        {
            var at = req.Request.RespondedAt!.Value;
            var accepted = req.Request.Status == RequestStatus.Accepted;
            data.Notifications.Add(new Note(userId, accepted ? "Anfrage angenommen" : "Anfrage abgelehnt",
                accepted ? "Deine Abwesenheitsanfrage wurde angenommen." : "Deine Abwesenheitsanfrage wurde abgelehnt.",
                accepted ? "RequestAccepted" : "RequestDeclined", at < nowUtc.AddDays(-14), null, req, at));
        }
        for (var week = clock.StartOfDayUtc(p.Joined); week < nowUtc; week = week.AddDays(7))
            for (var n = 0; n < 2; n++)
            {
                var at = week.AddMinutes(rng.Next(7 * 24 * 60));
                if (at >= nowUtc) continue;
                data.Notifications.Add(new Note(userId, "Einstellungen geändert", "Die Regeln der Organisation wurden geändert.", "SettingsChanged",
                    at < nowUtc.AddDays(-14), null, null, at));
            }
    }

    /// <summary>
    /// One working day: a single entry, or two around a lunch break of 30–60 min; the gross time is chosen so
    /// that gross − automatic pause = <paramref name="net"/>, and the pause sits on the longest entry as the
    /// pause rules put it.
    /// </summary>
    private static List<TimeEntry> AddDay(Person p, History data, int orgId, DateOnly d, int startMin, int net, bool split,
        TimeEntrySource source, OrgClock clock, Random rng)
    {
        var lunchStart = 12 * 60 + rng.Next(0, 31);
        var gap = split ? rng.Next(30, 61) : 0;
        var gross = net;
        for (var i = 0; i < 3; i++)
            gross = net + Math.Max(0, PauseRules.RequiredPauseMinutes(Rules, gross / 60.0) - gap);
        var spans = split && gross - (lunchStart - startMin) > 30
            ? new List<(int From, int To)> { (startMin, lunchStart), (lunchStart + gap, lunchStart + gap + gross - (lunchStart - startMin)) }
            : [(startMin, startMin + gross)];

        var entries = spans.Select(s =>
        {
            var from = clock.ToUtc(d.ToDateTime(TimeOnly.MinValue).AddMinutes(s.From));
            var to = clock.ToUtc(d.ToDateTime(TimeOnly.MinValue).AddMinutes(s.To));
            return new TimeEntry
            {
                UserId = p.User.Id, OrganizationId = orgId, StartTime = from, EndTime = to, Source = source,
                CreatedAt = from, UpdatedAt = to
            };
        }).ToList();
        var deficit = Math.Max(0, PauseRules.RequiredPauseMinutes(Rules, entries.Sum(e => (e.EndTime!.Value - e.StartTime).TotalHours))
            - PauseRules.CreditedGapMinutes(entries.Select(e => (e.StartTime, e.EndTime!.Value)).ToList()));
        entries.OrderByDescending(e => e.EndTime!.Value - e.StartTime).ThenByDescending(e => e.StartTime).First().PauseDurationMinutes = deficit;
        data.Entries.AddRange(entries);
        return entries;
    }

    private static OrgRequest Request(int userId, int orgId, RequestStatus status, List<DateOnly> dates, bool half, DateTime createdAt) => new()
    {
        UserId = userId, OrganizationId = orgId, Type = RequestType.RequestAbsence, Status = status, CreatedAt = createdAt,
        Message = dates.Count > 1 ? "Urlaub" : "Freier Tag",
        RequestData = JsonSerializer.Serialize(new AbsenceRequestData { Dates = dates, Type = AbsenceType.Vacation, IsHalfDay = half }, Json)
    };

    private static void Decide(OrgRequest request, int approver, string? reason, DateTime nowUtc, Random rng)
    {
        request.RespondedByUserId = approver;
        request.RespondedAt = Min(request.CreatedAt.AddHours(rng.Next(1, 48)), nowUtc.AddHours(-1));
        request.ResponseReason = reason;
        request.UserNotifiedAt = Min(request.RespondedAt.Value.AddHours(2), nowUtc);
    }

    // ── Holidays ────────────────────────────────────────────────────────────

    /// <summary>German nationwide holidays: fixed dates recurring, the Easter-based ones per year; 24.12. and 31.12. half days.</summary>
    public static List<(string Name, DateOnly Date, bool Recurring, bool Half)> GermanHolidays(int fromYear, int toYear)
    {
        var list = new List<(string, DateOnly, bool, bool)>
        {
            ("Neujahr", new(fromYear, 1, 1), true, false), ("Tag der Arbeit", new(fromYear, 5, 1), true, false),
            ("Tag der Deutschen Einheit", new(fromYear, 10, 3), true, false), ("Heiligabend", new(fromYear, 12, 24), true, true),
            ("1. Weihnachtstag", new(fromYear, 12, 25), true, false), ("2. Weihnachtstag", new(fromYear, 12, 26), true, false),
            ("Silvester", new(fromYear, 12, 31), true, true)
        };
        var fixedDays = list.Select(h => (h.Item2.Month, h.Item2.Day)).ToHashSet();
        for (var y = fromYear; y <= toYear; y++)
        {
            var easter = EasterSunday(y);
            foreach (var (name, offset) in new[] { ("Karfreitag", -2), ("Ostermontag", 1), ("Christi Himmelfahrt", 39), ("Pfingstmontag", 50) })
            {
                var day = easter.AddDays(offset);
                if (!fixedDays.Contains((day.Month, day.Day))) list.Add((name, day, false, false));
            }
        }
        return list;
    }

    /// <summary>Gregorian Easter Sunday (anonymous Gregorian algorithm).</summary>
    public static DateOnly EasterSunday(int y)
    {
        int a = y % 19, b = y / 100, c = y % 100, d = b / 4, e = b % 4, f = (b + 8) / 25, g = (b - f + 1) / 3;
        var h = (19 * a + b - d - g + 15) % 30;
        int i = c / 4, k = c % 4;
        var l = (32 + 2 * e + 2 * i - h - k) % 7;
        var m = (a + 11 * h + 22 * l) / 451;
        var month = (h + l - 7 * m + 114) / 31;
        var day = (h + l - 7 * m + 114) % 31 + 1;
        return new DateOnly(y, month, day);
    }

    // ── Npgsql helpers ──────────────────────────────────────────────────────

    private static DateTime Min(DateTime a, DateTime b) => a < b ? a : b;

    private static string Iso(DateTime utc) => utc.ToString("yyyy-MM-ddTHH:mm:ssZ", CultureInfo.InvariantCulture);

    private static async Task ExecuteAsync(NpgsqlConnection conn, string sql, CancellationToken ct)
    {
        await using var cmd = new NpgsqlCommand(sql, conn);
        await cmd.ExecuteNonQueryAsync(ct);
    }

    /// <summary>
    /// Takes <paramref name="count"/> consecutive ids of the table's identity sequence, so rows written by COPY can be
    /// referenced. Atomic only because SeedAsync holds a SHARE ROW EXCLUSIVE lock on the table: no other session
    /// inserts (and so draws from the sequence) between the nextval and the setval.
    /// </summary>
    private static async Task<int> ReserveIdsAsync(NpgsqlConnection conn, string table, int count, CancellationToken ct)
    {
        if (count == 0) return 0;
        await using var cmd = new NpgsqlCommand(
            $"""SELECT setval(pg_get_serial_sequence('"{table}"', 'Id'), nextval(pg_get_serial_sequence('"{table}"', 'Id')) + @n - 1) - @n + 1""", conn);
        cmd.Parameters.AddWithValue("n", (long)count);
        return Convert.ToInt32(await cmd.ExecuteScalarAsync(ct), CultureInfo.InvariantCulture);
    }

    /// <summary>
    /// Binary COPY into <paramref name="table"/>. Postgres refuses COPY FROM into a table whose row-level security
    /// applies to the role (even with the bypass setting), so a role that does not bypass RLS copies into a
    /// temporary table and moves the rows over with one INSERT … SELECT, which the policies' bypass admits.
    /// </summary>
    private static async Task<int> CopyAsync<T>(NpgsqlConnection conn, string table, string[] columns, IEnumerable<T> rows,
        Action<NpgsqlBinaryImporter, T> write, CancellationToken ct)
    {
        var list = string.Join(", ", columns.Select(c => $"\"{c}\""));
        var bypass = await RoleBypassesRlsAsync(conn, ct);
        var target = bypass ? $"\"{table}\"" : $"\"perf_stage_{table}\"";
        if (!bypass)
            await ExecuteAsync(conn, $"CREATE TEMP TABLE {target} ON COMMIT DROP AS SELECT {list} FROM \"{table}\" WITH NO DATA", ct);

        var count = 0;
        await using (var importer = await conn.BeginBinaryImportAsync($"COPY {target} ({list}) FROM STDIN (FORMAT BINARY)", ct))
        {
            foreach (var row in rows)
            {
                await importer.StartRowAsync(ct);
                write(importer, row);
                count++;
            }
            await importer.CompleteAsync(ct);
        }
        if (!bypass)
            await ExecuteAsync(conn, $"INSERT INTO \"{table}\" ({list}) SELECT {list} FROM {target}", ct);
        return count;
    }

    private static async Task<bool> RoleBypassesRlsAsync(NpgsqlConnection conn, CancellationToken ct)
    {
        await using var cmd = new NpgsqlCommand("SELECT rolsuper OR rolbypassrls FROM pg_roles WHERE rolname = current_user", conn);
        return await cmd.ExecuteScalarAsync(ct) is true;
    }

    private static void WriteNullable<T>(NpgsqlBinaryImporter w, T? value, NpgsqlDbType type) where T : struct
    {
        if (value.HasValue) w.Write(value.Value, type);
        else w.WriteNull();
    }

    private static void WriteNullable(NpgsqlBinaryImporter w, string? value, NpgsqlDbType type)
    {
        if (value != null) w.Write(value, type);
        else w.WriteNull();
    }
    // ── Removal ─────────────────────────────────────────────────────────────

    /// <summary>
    /// Hard-deletes a perf organization (slug <c>perf-…</c> only) with its <c>perf-*@{slug}.test</c> users and every row
    /// of either, in one transaction. Rows are found through the foreign keys to Users and Organizations, so a table
    /// added later is covered too. Returns false when there is no such organization.
    /// </summary>
    public static async Task<bool> DropAsync(NpgsqlConnection conn, string slug, CancellationToken ct = default)
    {
        if (!slug.StartsWith("perf-", StringComparison.Ordinal))
            throw new ArgumentException("Only perf-… organizations can be dropped.", nameof(slug));
        if (conn.State != System.Data.ConnectionState.Open) await conn.OpenAsync(ct);
        await ExecuteAsync(conn, "SELECT set_config('app.bypass_rls', 'on', false)", ct);
        await using var tx = await conn.BeginTransactionAsync(ct);

        int orgId;
        await using (var find = new NpgsqlCommand("""SELECT "Id" FROM "Organizations" WHERE "Slug" = @slug""", conn))
        {
            find.Parameters.AddWithValue("slug", slug);
            if (await find.ExecuteScalarAsync(ct) is not int id) return false;
            orgId = id;
        }
        var users = new List<int>();
        await using (var find = new NpgsqlCommand("""SELECT "Id" FROM "Users" WHERE "Email" LIKE 'perf-%@' || @slug || '.test'""", conn))
        {
            find.Parameters.AddWithValue("slug", slug);
            await using var reader = await find.ExecuteReaderAsync(ct);
            while (await reader.ReadAsync(ct)) users.Add(reader.GetInt32(0));
        }

        // Foreign key columns pointing at Users / Organizations, per table
        var fks = new List<(string Table, string Column, string Target)>();
        await using (var cmd = new NpgsqlCommand("""
            SELECT c.conrelid::regclass::text, a.attname, c.confrelid::regclass::text
            FROM pg_constraint c JOIN pg_attribute a ON a.attrelid = c.conrelid AND a.attnum = c.conkey[1]
            WHERE c.contype = 'f' AND c.confrelid IN ('"Users"'::regclass, '"Organizations"'::regclass)
            """, conn))
        await using (var reader = await cmd.ExecuteReaderAsync(ct))
            while (await reader.ReadAsync(ct)) fks.Add((reader.GetString(0).Trim('"'), reader.GetString(1), reader.GetString(2).Trim('"')));

        // Children before parents; unknown tables first (they only point at users/organizations)
        string[] order =
        [
            "TerminalPunches", "TimeEntryChanges", "CoreHoursChecks", "Notifications", "PushSubscriptions", "AbsenceDays", "TimeEntries",
            "OrgRequests", "WorkSchedules", "BadgeCards", "ApiKeys", "ClosedPeriodEvents", "ClosedPeriods", "OrgInvitations", "GroupMembers",
            "Groups", "Holidays", "PauseRules", "AbsenceTypeDefinitions", "UserOrganizations", "RefreshTokens", "UserTokens"
        ];
        var tables = fks.Select(f => f.Table).Distinct().Where(t => t is not ("Users" or "Organizations") && !order.Contains(t)).Concat(order);
        await ExecuteAsync(conn, $"""UPDATE "Groups" SET "ParentGroupId" = NULL WHERE "OrganizationId" = {orgId}""", ct);
        foreach (var table in tables)
        {
            var conditions = fks.Where(f => f.Table == table)
                .Select(f => f.Target == "Organizations" ? $"\"{f.Column}\" = @org" : $"\"{f.Column}\" = ANY(@users)").ToList();
            if (table == "GroupMembers") conditions.Add("""
                "GroupId" IN (SELECT "Id" FROM "Groups" WHERE "OrganizationId" = @org)
                """);
            if (conditions.Count == 0) continue;
            await using var delete = new NpgsqlCommand($"DELETE FROM \"{table}\" WHERE {string.Join(" OR ", conditions)}", conn);
            delete.Parameters.AddWithValue("org", orgId);
            delete.Parameters.AddWithValue("users", users.ToArray());
            await delete.ExecuteNonQueryAsync(ct);
        }
        await using (var delete = new NpgsqlCommand("""DELETE FROM "Organizations" WHERE "Id" = @org; DELETE FROM "Users" WHERE "Id" = ANY(@users)""", conn))
        {
            delete.Parameters.AddWithValue("org", orgId);
            delete.Parameters.AddWithValue("users", users.ToArray());
            await delete.ExecuteNonQueryAsync(ct);
        }
        await tx.CommitAsync(ct);
        return true;
    }
}

/// <summary>
/// <c>dotnet TimeTracking.Api.dll perf-seed --employees 100 --years 10 [--seed 1] [--org-slug …] [--end-date yyyy-MM-dd]
/// [--password …] [--core-hours on|off] [--force]</c> migrates the Postgres database of <c>DATABASE_URL</c> /
/// <c>ConnectionStrings:DefaultConnection</c> and seeds one <see cref="PerfSeeder"/> organization (a random password
/// unless given, printed once); <c>perf-seed --drop --org-slug perf-…</c> removes one again.
/// Guards: it prints the target host/database first; without <c>--force</c> it runs only in Development/Testing and
/// only on a database whose organizations are all perf-… or the demo org; it never seeds over an existing slug.
/// </summary>
public static class PerfSeedCommand
{
    /// <summary>The demo organization (DbSeeder) a test instance may hold next to perf organizations.</summary>
    public const string DemoSlug = "mueller-software";

    public sealed record Command(PerfSeedOptions Options, bool Force, bool Drop);

    public static async Task<int> RunAsync(string[] args, IConfiguration config, IHostEnvironment env)
    {
        var (command, error) = Parse(args);
        if (error != null)
        {
            Console.Error.WriteLine(error);
            Console.Error.WriteLine("Usage: perf-seed --employees 100 --years 10 [--seed 1] [--org-slug perf-100-10] [--end-date yyyy-MM-dd] " +
                                    "[--password …] [--core-hours on|off] [--force]\n       perf-seed --drop --org-slug perf-100-10 [--force]");
            return 2;
        }
        var (options, force, drop) = command!;
        var connectionString = DatabaseUrl.ToConnectionString(Environment.GetEnvironmentVariable("DATABASE_URL"))
                               ?? config.GetConnectionString("DefaultConnection");
        if (string.IsNullOrEmpty(connectionString))
        {
            Console.Error.WriteLine("perf-seed needs PostgreSQL: set DATABASE_URL or ConnectionStrings:DefaultConnection.");
            return 2;
        }
        var target = new NpgsqlConnectionStringBuilder(connectionString);
        Console.WriteLine($"Target: {target.Host}:{target.Port} database '{target.Database}' as '{target.Username}' ({env.EnvironmentName}).");
        if (!env.IsDevelopment() && !env.IsEnvironment("Testing") && !force)
        {
            Console.Error.WriteLine($"perf-seed writes synthetic users and history: refused in {env.EnvironmentName} without --force.");
            return 2;
        }

        await using var conn = new NpgsqlConnection(connectionString);
        await conn.OpenAsync();
        if (drop)
        {
            if (!await PerfSeeder.DropAsync(conn, options.Slug))
            {
                Console.Error.WriteLine($"Organization '{options.Slug}' not found.");
                return 1;
            }
            Console.WriteLine($"Dropped '{options.Slug}' with its perf users and all their rows.");
            return 0;
        }

        // Never next to real data unless forced: only perf-… organizations and the demo org may be there
        var others = await OtherOrganizationsAsync(conn);
        if (others.Count > 0 && !force)
        {
            Console.Error.WriteLine($"The database holds other organizations ({string.Join(", ", others.Take(5))}{(others.Count > 5 ? ", …" : "")}): refused without --force.");
            return 2;
        }

        // Migrate like the app does on startup (system work, RLS bypass)
        var rls = new RlsConnectionInterceptor(new RlsContext(new HttpContextAccessor()));
        await using (var db = new TimeTrackingDbContext(new DbContextOptionsBuilder<TimeTrackingDbContext>()
                         .UseNpgsql(connectionString).AddInterceptors(rls).Options))
            await db.Database.MigrateAsync();

        try
        {
            var result = await PerfSeeder.SeedAsync(conn, options);
            Console.WriteLine($"Seeded '{result.Slug}' (organization {result.OrganizationId}, core hours {(options.CoreHours ? "on" : "off")}) in {result.Elapsed.TotalSeconds:0.0} s:");
            foreach (var (table, rows) in result.Rows) Console.WriteLine($"  {table,-18} {rows,9:N0}");
            Console.WriteLine($"Logins: {PerfSeedResult.OwnerEmail(result.Slug)}, {PerfSeedResult.AdminEmail(result.Slug, 1)}…{result.AdminIds.Count}, " +
                              $"{PerfSeedResult.MemberEmail(result.Slug, 1)}…{result.MemberIds.Count}");
            Console.WriteLine($"Password (shown once): {result.Password}");
            Console.WriteLine($"Remove with: perf-seed --drop --org-slug {result.Slug}");
            return 0;
        }
        catch (InvalidOperationException ex)
        {
            Console.Error.WriteLine(ex.Message);
            return 1;
        }
    }

    /// <summary>Slugs of the organizations that are neither perf-… nor the demo org (none on a fresh database).</summary>
    private static async Task<List<string>> OtherOrganizationsAsync(NpgsqlConnection conn)
    {
        var slugs = new List<string>();
        await using (var exists = new NpgsqlCommand("""SELECT to_regclass('"Organizations"') IS NOT NULL""", conn))
            if (await exists.ExecuteScalarAsync() is not true) return slugs;
        await using var cmd = new NpgsqlCommand(
            """SELECT "Slug" FROM "Organizations" WHERE "Slug" NOT LIKE 'perf-%' AND "Slug" <> @demo ORDER BY "Id" """, conn);
        cmd.Parameters.AddWithValue("demo", DemoSlug);
        await using var reader = await cmd.ExecuteReaderAsync();
        while (await reader.ReadAsync()) slugs.Add(reader.GetString(0));
        return slugs;
    }

    /// <summary>The options of <c>perf-seed …</c>; an error text for anything it cannot read.</summary>
    public static (Command? Command, string? Error) Parse(string[] args)
    {
        var options = new PerfSeedOptions();
        var (force, drop) = (false, false);
        for (var i = 0; i < args.Length; i++)
        {
            var arg = args[i];
            if (arg == "perf-seed") continue;
            if (arg == "--force") { force = true; continue; }
            if (arg == "--drop") { drop = true; continue; }
            if (i + 1 >= args.Length) return (null, $"Missing value for {arg}.");
            var value = args[++i];
            switch (arg)
            {
                case "--employees" when int.TryParse(value, out var n) && n is >= 3 and <= 10_000:
                    options = options with { Employees = n }; break;
                case "--years" when int.TryParse(value, out var n) && n is >= 1 and <= 30:
                    options = options with { Years = n }; break;
                case "--seed" when int.TryParse(value, out var n):
                    options = options with { Seed = n }; break;
                case "--org-slug" when System.Text.RegularExpressions.Regex.IsMatch(value, "^[a-z0-9-]{3,100}$"):
                    options = options with { OrgSlug = value }; break;
                case "--end-date" when DateOnly.TryParseExact(value, "yyyy-MM-dd", CultureInfo.InvariantCulture, DateTimeStyles.None, out var d):
                    options = options with { EndDate = d }; break;
                case "--password" when value.Length >= 8:
                    options = options with { Password = value }; break;
                case "--core-hours" when value is "on" or "off":
                    options = options with { CoreHours = value == "on" }; break;
                default:
                    return (null, $"Invalid argument: {arg} {value}");
            }
        }
        if (drop && (options.OrgSlug == null || !options.Slug.StartsWith("perf-", StringComparison.Ordinal)))
            return (null, "--drop needs --org-slug perf-…");
        return (new Command(options, force, drop), null);
    }
}
