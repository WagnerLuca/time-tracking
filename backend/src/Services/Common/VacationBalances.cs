using Microsoft.EntityFrameworkCore;
using TimeTracking.Api.Data;
using TimeTracking.Api.Models;

namespace TimeTracking.Api.Services;

/// <summary>
/// Vacation days used per member and year — the one rule behind the member list's vacation figures
/// and the accounting vacation overview: vacation (or a custom type counting as vacation) on the
/// member's working days, holidays excluded; taken = up to <c>today</c>, planned = after it.
/// </summary>
public static class VacationBalances
{
    public sealed record Usage(double Taken, double Planned)
    {
        public double Used => Taken + Planned;
    }

    /// <summary>Remaining days of an allowance (never negative).</summary>
    public static double Remaining(double entitlement, Usage? usage) => Math.Max(0, entitlement - (usage?.Used ?? 0));

    public static async Task<Dictionary<int, Usage>> UsageAsync(
        TimeTrackingDbContext context, int orgId, IReadOnlyCollection<int> memberIds, int year, DateOnly today)
    {
        var yearStart = new DateOnly(year, 1, 1);
        var yearEnd = new DateOnly(year, 12, 31);

        var holidayDates = (await context.Holidays.AsNoTracking().Where(h => h.OrganizationId == orgId).ToListAsync())
            .Select(h => h.DateIn(year))
            .OfType<DateOnly>()
            .ToHashSet();

        var vacation = await context.AbsenceDays.AsNoTracking()
            .Where(a => a.OrganizationId == orgId
                     && memberIds.Contains(a.UserId)
                     && (a.Type == AbsenceType.Vacation || (a.CustomType != null && a.CustomType.CountsAsVacation))
                     && a.Date >= yearStart && a.Date <= yearEnd)
            .ToListAsync();

        // Only the member's working days count (a vacation day on a day off costs nothing)
        var schedulesByUser = (await context.WorkSchedules.AsNoTracking()
                .Where(ws => ws.OrganizationId == orgId && memberIds.Contains(ws.UserId))
                .ToListAsync())
            .ToLookup(ws => ws.UserId);

        return vacation
            .Where(a => !holidayDates.Contains(a.Date) && WorkingDays.IsWorkingDay(schedulesByUser[a.UserId], a.Date))
            .GroupBy(a => a.UserId)
            .ToDictionary(
                g => g.Key,
                g => new Usage(
                    Taken: g.Where(a => a.Date <= today).Sum(a => a.IsHalfDay ? 0.5 : 1.0),
                    Planned: g.Where(a => a.Date > today).Sum(a => a.IsHalfDay ? 0.5 : 1.0)));
    }
}
