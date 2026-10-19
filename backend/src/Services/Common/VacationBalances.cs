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
/// Carry-over (<see cref="Organization.VacationCarryOverMode"/>): a year after <see cref="Organization.VacationCarryOverSince"/>
/// (and after the member's join year) starts with the previous year's leftover. Carried days are used first. Mode
/// UntilDate: days used on or before the expiry date (MM-DD of that year, clamped to the month) consume the carry-in,
/// the rest lapses once the date has passed. Unlimited: never lapses. None: no carry-in. The leftover moving on is
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

    /// <summary>The source data of one year: its entitlement, adjustment and counted vacation days.</summary>
    public sealed record YearInput(int Year, double Entitlement, double Adjustment, IReadOnlyList<Day> Days);

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
                case VacationCarryOverMode.UntilDate when carryIn > 0:
                    var e = rule.ExpiryIn(y.Year);
                    carryUsed = Math.Min(carryIn, y.Days.Where(d => d.Date <= e).Sum(d => d.Days));
                    lapsedAtYearEnd = carryIn - carryUsed;
                    expired = today > e ? lapsedAtYearEnd : 0;
                    expiresOn = e;
                    break;
                case VacationCarryOverMode.Unlimited when carryIn > 0:
                    carryUsed = Math.Min(carryIn, used);
                    lapsedAtYearEnd = expired = 0;
                    break;
                default:
                    carryUsed = lapsedAtYearEnd = expired = 0;
                    break;
            }

            var taken = y.Days.Where(d => d.Date <= today).Sum(d => d.Days);
            var available = y.Entitlement + y.Adjustment + carryIn - expired;
            balance = new Balance(y.Year, y.Entitlement, y.Adjustment, carryIn, carryUsed, expired, expiresOn,
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

    /// <summary>The first year that gets a carry-in for a member who joined in <paramref name="joinYear"/>.</summary>
    private static int ChainStart(Rule rule, int joinYear) => Math.Max(rule.Since, joinYear);

    /// <summary>
    /// Every given member's balance for <paramref name="year"/> (members without data get their entitlement).
    /// Five queries however many members and years: memberships, holidays, schedules, vacation days of
    /// [since, year], vacation rows.
    /// </summary>
    public static async Task<Dictionary<int, Balance>> YearsAsync(
        TimeTrackingDbContext context, Organization org, IReadOnlyCollection<int> memberIds, int year, DateOnly today)
    {
        var rule = Rule.Of(org);
        var clock = OrgClock.For(org);
        var ids = memberIds.Distinct().ToList();
        if (ids.Count == 0) return new Dictionary<int, Balance>();

        var members = (await context.UserOrganizations.IgnoreQueryFilters().AsNoTracking()
                .Where(uo => uo.OrganizationId == org.Id && ids.Contains(uo.UserId))
                .Select(uo => new { uo.UserId, uo.VacationDaysPerYear, uo.JoinedAt })
                .ToListAsync())
            .ToDictionary(m => m.UserId);

        // Carry-in reaches back to `since` at most; a year before it (or mode None) stands alone
        var chained = rule.Mode != VacationCarryOverMode.None && year > rule.Since;
        var firstYear = chained ? rule.Since : year;
        var (from, to) = (new DateOnly(firstYear, 1, 1), new DateOnly(year, 12, 31));

        var holidays = await context.Holidays.AsNoTracking().Where(h => h.OrganizationId == org.Id).ToListAsync();
        var holidayDates = Enumerable.Range(firstYear, year - firstYear + 1)
            .SelectMany(y => holidays.Select(h => h.DateIn(y)))
            .OfType<DateOnly>()
            .ToHashSet();

        // Only the member's working days count (a vacation day on a day off costs nothing)
        var schedulesByUser = (await context.WorkSchedules.AsNoTracking()
                .Where(ws => ws.OrganizationId == org.Id && ids.Contains(ws.UserId))
                .ToListAsync())
            .ToLookup(ws => ws.UserId);

        var daysByUser = (await context.AbsenceDays.AsNoTracking()
                .Where(a => a.OrganizationId == org.Id
                         && ids.Contains(a.UserId)
                         && (a.Type == AbsenceType.Vacation || (a.CustomType != null && a.CustomType.CountsAsVacation))
                         && a.Date >= from && a.Date <= to)
                .Select(a => new { a.UserId, a.Date, a.IsHalfDay })
                .ToListAsync())
            .Where(a => !holidayDates.Contains(a.Date) && WorkingDays.IsWorkingDay(schedulesByUser[a.UserId], a.Date))
            .ToLookup(a => a.UserId, a => new Day(a.Date, a.IsHalfDay ? 0.5 : 1.0));

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
                .Select(y => new YearInput(y, EntitlementFor(y, rows, allowance),
                    rows.FirstOrDefault(r => r.Year == y)?.Adjustment ?? 0, days[y].ToList()))
                .ToList();
            result[id] = Chain(rule, inputs, today);
        }
        return result;
    }
}
