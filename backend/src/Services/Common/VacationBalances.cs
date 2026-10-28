using Microsoft.EntityFrameworkCore;
using TimeTracking.Api.Data;
using TimeTracking.Api.Models;

namespace TimeTracking.Api.Services;

/// <summary>
/// Vacation balance per member and year — the one rule behind every remaining-vacation figure (org detail member
/// rows → dashboard, member card, member list, team calendar; the accounting vacation overview; the member vacation
/// endpoint). Used days: vacation (or a custom type counting as vacation) on the member's working days, holidays
/// excluded, half days 0.5; taken = up to <c>today</c>, planned = after it (planned counts as used).
/// <para>
/// Entitlement: the year's figure (<see cref="EntitlementFor"/>), pro rata (<see cref="ProRata"/>) by the employment dates
/// an admin set (<see cref="UserOrganization.EmploymentStart"/>/<see cref="UserOrganization.EmploymentEnd"/>, § 5 BUrlG) and
/// split by the contract periods' <see cref="WorkSchedule.VacationDaysPerYear"/>; without either it is the year's figure
/// unchanged. An admin's adjustment corrects the result where needed.
/// </para>
/// <para>
/// Carry-over (<see cref="Organization.VacationCarryOverMode"/>): a year after <see cref="Organization.VacationCarryOverSince"/>
/// (and after the member's join year) starts with the previous year's leftover. Carried days are used first. Mode
/// UntilDate: days used on or before the expiry date (MM-DD of that year, clamped to the month; per member
/// <see cref="VacationYear.ExpiresOn"/>) consume the carry-in, the rest lapses once the date has passed — but only when the
/// member was told (<see cref="VacationYear.ExpiryNoticeAt"/>, sent by <c>VacationNotices</c>); without the notice the days
/// stay, as if unlimited. Unlimited: never lapses. None: no carry-in. The leftover moving on is
/// <c>max(0, entitlement + adjustment + carryIn − lapsed − used)</c> of the whole year, capped by
/// <see cref="Organization.VacationCarryOverMaxDays"/>. Rules apply to all years ≥ since at once (they are rules, not
/// data); vacation figures are not part of the closed-month guard.
/// </para>
/// </summary>
public static class VacationBalances
{
    /// <summary>The organization's carry-over settings.</summary>
    public sealed record Rule(VacationCarryOverMode Mode, int ExpiryMonth, int ExpiryDay, double? MaxDays, int Since)
    {
        public static Rule Of(Organization org) => new(org.VacationCarryOverMode, org.VacationCarryOverExpiryMonth,
            org.VacationCarryOverExpiryDay, org.VacationCarryOverMaxDays, org.VacationCarryOverSince);

        /// <summary>The day after which carried-over days of <paramref name="year"/> lapse (day clamped to the month).</summary>
        public DateOnly ExpiryIn(int year) =>
            new(year, ExpiryMonth, Math.Min(ExpiryDay, DateTime.DaysInMonth(year, ExpiryMonth)));
    }

    /// <summary>One year of a member's vacation.</summary>
    public sealed record Balance(
        int Year,
        double Entitlement,
        double AnnualEntitlement,
        double Adjustment,
        double CarriedIn,
        double CarriedUsed,
        double Expired,
        DateOnly? ExpiresOn,
        double Taken,
        double Planned,
        double Remaining)
    {
        public double Used => Taken + Planned;
        /// <summary>What the year offers: entitlement + adjustment + carried-in days − lapsed ones.</summary>
        public double Available => Entitlement + Adjustment + CarriedIn - Expired;
    }

    /// <summary>One counted vacation day (1 or 0.5).</summary>
    public readonly record struct Day(DateOnly Date, double Days);

    /// <summary>
    /// The source data of one year: its entitlement (pro rata) and the full-year figure it came from, adjustment, counted
    /// vacation days, the member's own expiry date (null = the rule's) and whether they were told about the expiry.
    /// </summary>
    public sealed record YearInput(int Year, double Entitlement, double Adjustment, IReadOnlyList<Day> Days,
        DateOnly? ExpiresOn = null, bool NoticeSent = true, double? Annual = null);

    /// <summary>
    /// The balance of the last year in <paramref name="years"/> (ascending, consecutive; the first one gets no carry-in).
    /// Pure, so the rules are testable without a database.
    /// </summary>
    public static Balance Chain(Rule rule, IReadOnlyList<YearInput> years, DateOnly today)
    {
        double carryIn = 0;
        Balance? balance = null;
        for (var i = 0; i < years.Count; i++)
        {
            var y = years[i];
            var used = y.Days.Sum(d => d.Days);
            double carryUsed, lapsedAtYearEnd, expired;
            DateOnly? expiresOn = null;
            switch (rule.Mode)
            {
                case VacationCarryOverMode.UntilDate when carryIn > 0 && y.NoticeSent:
                    var e = y.ExpiresOn ?? rule.ExpiryIn(y.Year);
                    carryUsed = Math.Min(carryIn, y.Days.Where(d => d.Date <= e).Sum(d => d.Days));
                    lapsedAtYearEnd = carryIn - carryUsed;
                    expired = today > e ? lapsedAtYearEnd : 0;
                    expiresOn = e;
                    break;
                case VacationCarryOverMode.Unlimited or VacationCarryOverMode.UntilDate when carryIn > 0:
                    carryUsed = Math.Min(carryIn, used);
                    lapsedAtYearEnd = expired = 0;
                    break;
                default:
                    carryUsed = lapsedAtYearEnd = expired = 0;
                    break;
            }

            var taken = y.Days.Where(d => d.Date <= today).Sum(d => d.Days);
            var available = y.Entitlement + y.Adjustment + carryIn - expired;
            balance = new Balance(y.Year, y.Entitlement, y.Annual ?? y.Entitlement, y.Adjustment, carryIn, carryUsed, expired, expiresOn,
                Taken: taken, Planned: used - taken, Remaining: Math.Max(0, available - used));

            var carryOut = Math.Max(0, y.Entitlement + y.Adjustment + carryIn - lapsedAtYearEnd - used);
            carryIn = rule.Mode == VacationCarryOverMode.None ? 0 : rule.MaxDays is { } max ? Math.Min(max, carryOut) : carryOut;
        }
        return balance ?? throw new ArgumentException("At least one year is needed.", nameof(years));
    }

    /// <summary>
    /// The entitlement of <paramref name="year"/>: its own row's, else the latest earlier row's, else the member's
    /// allowance (<see cref="UserOrganization.VacationDaysPerYear"/>). <paramref name="rows"/> are the member's rows.
    /// </summary>
    public static double EntitlementFor(int year, IEnumerable<VacationYear> rows, double allowance) =>
        rows.Where(r => r.Year <= year && r.Entitlement != null).MaxBy(r => r.Year)?.Entitlement ?? allowance;

    /// <summary>
    /// Vacation days one vacation row costs: none on a day off or a full holiday, half a day on a half holiday (the other
    /// half is off anyway) or for a half-day absence, else a whole day.
    /// </summary>
    public static double CountedDays(DayOff? holiday, bool halfDayAbsence, bool workingDay) =>
        !workingDay || holiday is { IsHalfDay: false } ? 0 : holiday != null || halfDayAbsence ? 0.5 : 1.0;

    /// <summary>
    /// The entitlement of <paramref name="year"/> from its full-year figure <paramref name="annual"/>, as § 5 BUrlG says
    /// (waiting period six months, § 4): the whole year, unless the waiting period is not completed within it (joined on
    /// 2 July or later), the member leaves before it is completed, or leaves after it in the first half of the year — then
    /// 1/12 per full month of membership in the year (months counted from the first member day of the year). Within that,
    /// every day weighs with the vacation days of the contract period valid on it (<see cref="WorkSchedule.VacationDaysPerYear"/>,
    /// else <paramref name="annual"/>), so a change during the year (e.g. to a 3-day week) never reduces days already earned.
    /// Rounding (§ 5 (2)): a fraction of at least half a day is rounded up to a full day, a smaller one stays.
    /// <paramref name="start"/>/<paramref name="end"/>: first and last day of employment (null = before / still employed).
    /// </summary>
    public static double ProRata(int year, double annual, DateOnly? start, DateOnly? end, IReadOnlyList<WorkSchedule> schedules)
    {
        var (yearStart, yearEnd) = (new DateOnly(year, 1, 1), new DateOnly(year, 12, 31));
        var joined = start ?? DateOnly.MinValue;
        var lastDay = end;
        if (joined > yearEnd || lastDay < yearStart || lastDay < joined) return 0;
        var waitingDone = joined == DateOnly.MinValue ? joined : joined.AddMonths(6).AddDays(-1);
        var full = waitingDone <= yearEnd
            && (lastDay == null || (lastDay >= waitingDone && lastDay > new DateOnly(year, 6, 30)));
        if (full && schedules.All(s => s.VacationDaysPerYear == null)) return annual;

        // Days outside the membership weigh like its nearest day (only the whole-year case reaches them)
        var first = joined > yearStart ? joined : yearStart;
        var last = lastDay is { } l && l < yearEnd ? l : yearEnd;
        double Rate(DateOnly d) => ScheduleOn(schedules, d < first ? first : d > last ? last : d)?.VacationDaysPerYear ?? annual;
        double Weighted(DateOnly from, DateOnly to)
        {
            double sum = 0;
            for (var d = from; d <= to; d = d.AddDays(1)) sum += Rate(d);
            return sum / (to.DayNumber - from.DayNumber + 1);
        }

        double days;
        if (full)
            days = Weighted(yearStart, yearEnd);
        else
        {
            days = 0;
            for (var k = 0; first.AddMonths(k + 1).AddDays(-1) <= last; k++)
                days += Weighted(first.AddMonths(k), first.AddMonths(k + 1).AddDays(-1)) / 12;
        }
        days = Math.Round(days, 6);
        var fraction = days - Math.Floor(days);
        return fraction >= 0.5 ? Math.Ceiling(days) : Math.Round(days, 2);
    }

    private static WorkSchedule? ScheduleOn(IReadOnlyList<WorkSchedule> schedules, DateOnly date) =>
        schedules.Where(s => s.ValidFrom <= date && (s.ValidTo == null || s.ValidTo >= date)).MaxBy(s => s.ValidFrom);

    /// <summary>The first year that gets a carry-in for a member who joined in <paramref name="joinYear"/>.</summary>
    private static int ChainStart(Rule rule, int joinYear) => Math.Max(rule.Since, joinYear);

    /// <summary>
    /// Every given member's balance for <paramref name="year"/> (members without data get their entitlement).
    /// Six queries however many members and years: memberships, holidays, schedules, frozen days of closed months (not when
    /// the caller knows there is no closed month: <paramref name="anyClosedMonth"/>), vacation days of [since, year], vacation rows.
    /// </summary>
    public static async Task<Dictionary<int, Balance>> YearsAsync(
        TimeTrackingDbContext context, Organization org, IReadOnlyCollection<int> memberIds, int year, DateOnly today,
        bool anyClosedMonth = true)
    {
        var rule = Rule.Of(org);
        var clock = OrgClock.For(org);
        var ids = memberIds.Distinct().ToList();
        if (ids.Count == 0) return new Dictionary<int, Balance>();

        var members = (await context.UserOrganizations.IgnoreQueryFilters().AsNoTracking()
                .Where(uo => uo.OrganizationId == org.Id && ids.Contains(uo.UserId))
                .Select(uo => new { uo.UserId, uo.VacationDaysPerYear, uo.JoinedAt, uo.EmploymentStart, uo.EmploymentEnd })
                .ToListAsync())
            .ToDictionary(m => m.UserId);

        // Carry-in reaches back to `since` at most; a year before it (or mode None) stands alone
        var chained = rule.Mode != VacationCarryOverMode.None && year > rule.Since;
        var firstYear = chained ? rule.Since : year;
        var (from, to) = (new DateOnly(firstYear, 1, 1), new DateOnly(year, 12, 31));

        var holidays = await HolidayCalendar.LoadAsync(context, org, ids, from, to);

        // Only the member's working days count (a vacation day on a day off costs nothing)
        var schedulesByUser = (await context.WorkSchedules.AsNoTracking()
                .Where(ws => ws.OrganizationId == org.Id && ids.Contains(ws.UserId))
                .ToListAsync())
            .GroupBy(ws => ws.UserId).ToDictionary(g => g.Key, g => g.ToList());
        List<WorkSchedule> SchedulesOf(int userId) => schedulesByUser.GetValueOrDefault(userId) ?? [];

        // Closed months count as frozen (FrozenFigures): their used days as when they were closed
        var frozen = anyClosedMonth ? await FrozenFigures.AbsencesAsync(context, org.Id, ids, from, to) : ([], []);
        var daysByUser = (await context.AbsenceDays.AsNoTracking()
                .Where(a => a.OrganizationId == org.Id
                         && ids.Contains(a.UserId)
                         && (a.Type == AbsenceType.Vacation || (a.CustomType != null && a.CustomType.CountsAsVacation))
                         && a.Date >= from && a.Date <= to)
                .Select(a => new { a.UserId, a.Date, a.IsHalfDay })
                .ToListAsync())
            .Where(a => !frozen.Months.Contains((a.UserId, ClosedPeriods.MonthOf(a.Date))))
            .Select(a => (a.UserId, Day: new Day(a.Date, CountedDays(holidays.On(a.UserId, a.Date, SchedulesOf(a.UserId)), a.IsHalfDay,
                WorkingDays.IsWorkingDay(SchedulesOf(a.UserId), a.Date)))))
            .Where(a => a.Day.Days > 0)
            .Concat(frozen.Absences.Where(a => a.VacationUsed > 0).Select(a => (a.UserId, Day: new Day(a.Date, a.VacationUsed))))
            .ToLookup(a => a.UserId, a => a.Day);

        var rowsByUser = (await context.VacationYears.AsNoTracking()
                .Where(v => v.OrganizationId == org.Id && ids.Contains(v.UserId) && v.Year <= year)
                .ToListAsync())
            .ToLookup(v => v.UserId);

        var result = new Dictionary<int, Balance>();
        foreach (var id in ids)
        {
            var allowance = members.TryGetValue(id, out var m) ? m.VacationDaysPerYear : 0;
            var joinYear = m != null ? clock.LocalDate(m.JoinedAt).Year : year;
            var start = chained ? Math.Min(year, ChainStart(rule, joinYear)) : year;
            var rows = rowsByUser[id].ToList();
            var days = daysByUser[id].ToLookup(d => d.Date.Year);
            var inputs = Enumerable.Range(start, year - start + 1)
                .Select(y =>
                {
                    var row = rows.FirstOrDefault(r => r.Year == y);
                    var annual = EntitlementFor(y, rows, allowance);
                    var entitlement = ProRata(y, annual, m?.EmploymentStart, m?.EmploymentEnd, SchedulesOf(id));
                    return new YearInput(y, entitlement, row?.Adjustment ?? 0, days[y].ToList(),
                        row?.ExpiresOn, row?.ExpiryNoticeAt != null, annual);
                })
                .ToList();
            result[id] = Chain(rule, inputs, today);
        }
        return result;
    }
}
