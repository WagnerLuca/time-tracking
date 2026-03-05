using System.Globalization;
using System.Text;
using Microsoft.EntityFrameworkCore;
using TimeTracking.Api.Data;
using TimeTracking.Api.Models;

namespace TimeTracking.Api.Services;

/// <summary>
/// Generates CSV exports of time entries for a user.
/// </summary>
public class TimeEntryExportService : ITimeEntryExportService
{
    private readonly TimeTrackingDbContext _context;
    private readonly ILogger<TimeEntryExportService> _logger;

    public TimeEntryExportService(TimeTrackingDbContext context, ILogger<TimeEntryExportService> logger)
    {
        _context = context;
        _logger = logger;
    }

    public async Task<ServiceResult<byte[]>> ExportCsvAsync(int userId, int? organizationId, DateTime? from, DateTime? to)
    {
        var query = _context.TimeEntries
            .AsNoTracking()
            .Include(e => e.Organization)
            .Where(e => e.UserId == userId && !e.IsRunning)
            .AsQueryable();

        if (organizationId.HasValue)
            query = query.Where(e => e.OrganizationId == organizationId.Value);
        if (from.HasValue)
            query = query.Where(e => e.StartTime >= from.Value);
        if (to.HasValue)
            query = query.Where(e => e.StartTime <= to.Value);

        var entries = await query
            .OrderBy(e => e.StartTime)
            .ToListAsync();

        var sb = new StringBuilder();

        // Header row
        sb.AppendLine("Date,Start Time,End Time,Duration (h),Pause (min),Net Duration (h),Organization,Description");

        foreach (var entry in entries)
        {
            var durationMinutes = entry.EndTime.HasValue
                ? (entry.EndTime.Value - entry.StartTime).TotalMinutes
                : 0;
            var netMinutes = Math.Max(0, durationMinutes - entry.PauseDurationMinutes);

            var date = entry.StartTime.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture);
            var start = entry.StartTime.ToString("HH:mm", CultureInfo.InvariantCulture);
            var end = entry.EndTime?.ToString("HH:mm", CultureInfo.InvariantCulture) ?? "";
            var duration = (durationMinutes / 60.0).ToString("F2", CultureInfo.InvariantCulture);
            var pause = entry.PauseDurationMinutes.ToString(CultureInfo.InvariantCulture);
            var net = (netMinutes / 60.0).ToString("F2", CultureInfo.InvariantCulture);
            var orgName = EscapeCsv(entry.Organization?.Name ?? "");
            var description = EscapeCsv(entry.Description ?? "");

            sb.AppendLine($"{date},{start},{end},{duration},{pause},{net},{orgName},{description}");
        }

        _logger.LogInformation("CSV export generated for user {UserId}: {Count} entries", userId, entries.Count);

        return ServiceResult.Ok(Encoding.UTF8.GetBytes(sb.ToString()));
    }

    public async Task<ServiceResult<byte[]>> ExportDailyReportCsvAsync(int userId, string orgSlug, DateTime? from, DateTime? to)
    {
        // Resolve organization and check membership
        var org = await _context.Organizations
            .AsNoTracking()
            .FirstOrDefaultAsync(o => o.Slug == orgSlug);

        if (org == null)
            return ServiceResult.NotFound<byte[]>("Organization not found.");

        var membership = await _context.UserOrganizations
            .AsNoTracking()
            .FirstOrDefaultAsync(uo => uo.OrganizationId == org.Id && uo.UserId == userId);

        if (membership == null)
            return ServiceResult.Forbidden<byte[]>("You are not a member of this organization.");

        // Default date range: current month
        var rangeFrom = from ?? new DateTime(DateTime.UtcNow.Year, DateTime.UtcNow.Month, 1, 0, 0, 0, DateTimeKind.Utc);
        var rangeTo = to ?? rangeFrom.AddMonths(1).AddDays(-1);

        var dateFrom = DateOnly.FromDateTime(rangeFrom);
        var dateTo = DateOnly.FromDateTime(rangeTo);

        // Load all data in parallel-friendly queries
        var timeEntries = await _context.TimeEntries
            .AsNoTracking()
            .Where(e => e.UserId == userId
                     && e.OrganizationId == org.Id
                     && !e.IsRunning
                     && e.StartTime >= rangeFrom
                     && e.StartTime <= rangeTo.AddDays(1))
            .OrderBy(e => e.StartTime)
            .ToListAsync();

        var workSchedules = await _context.WorkSchedules
            .AsNoTracking()
            .Where(ws => ws.UserId == userId && ws.OrganizationId == org.Id)
            .OrderBy(ws => ws.ValidFrom)
            .ToListAsync();

        var holidays = await _context.Holidays
            .AsNoTracking()
            .Where(h => h.OrganizationId == org.Id
                     && ((h.Date >= dateFrom && h.Date <= dateTo) || h.IsRecurring))
            .ToListAsync();

        var absences = await _context.AbsenceDays
            .AsNoTracking()
            .Where(a => a.UserId == userId
                     && a.OrganizationId == org.Id
                     && a.Date >= dateFrom
                     && a.Date <= dateTo)
            .ToListAsync();

        // Build lookup dictionaries
        var entriesByDate = timeEntries
            .GroupBy(e => DateOnly.FromDateTime(e.StartTime))
            .ToDictionary(g => g.Key, g => g.ToList());

        var holidaysByDate = new Dictionary<DateOnly, Holiday>();
        foreach (var h in holidays)
        {
            if (h.IsRecurring)
            {
                // Check each year in the range
                for (var year = dateFrom.Year; year <= dateTo.Year; year++)
                {
                    var recurringDate = new DateOnly(year, h.Date.Month, h.Date.Day);
                    if (recurringDate >= dateFrom && recurringDate <= dateTo)
                        holidaysByDate.TryAdd(recurringDate, h);
                }
            }
            else
            {
                holidaysByDate.TryAdd(h.Date, h);
            }
        }

        var absencesByDate = absences.ToDictionary(a => a.Date, a => a);

        // Generate CSV
        var sb = new StringBuilder();
        sb.AppendLine("Date,Day,Target (h),Worked (h),Pause (min),Net Worked (h),Overtime (h),Cumulative Overtime (h),Status,Holiday,Absence Type,Absence Note,Entries");

        var cumulativeOvertime = membership.InitialOvertimeHours;
        var currentDate = dateFrom;

        while (currentDate <= dateTo)
        {
            var dayOfWeek = currentDate.DayOfWeek;
            var dayName = currentDate.DayOfWeek.ToString().Substring(0, 3);
            var targetHours = GetTargetHours(workSchedules, currentDate);

            // Get entries for this day
            var dayEntries = entriesByDate.GetValueOrDefault(currentDate) ?? new List<TimeEntry>();

            var grossMinutes = dayEntries.Sum(e => e.EndTime.HasValue
                ? (e.EndTime.Value - e.StartTime).TotalMinutes
                : 0);
            var pauseMinutes = dayEntries.Sum(e => e.PauseDurationMinutes);
            var netMinutes = Math.Max(0, grossMinutes - pauseMinutes);
            var netHours = netMinutes / 60.0;

            // Determine day status
            var isHoliday = holidaysByDate.TryGetValue(currentDate, out var holiday);
            var isAbsence = absencesByDate.TryGetValue(currentDate, out var absence);

            string status;
            double effectiveTarget = targetHours;

            if (isHoliday)
            {
                status = "Holiday";
                effectiveTarget = 0; // No target on holidays
            }
            else if (isAbsence)
            {
                status = absence!.Type switch
                {
                    AbsenceType.SickDay => "Sick",
                    AbsenceType.Vacation => "Vacation",
                    AbsenceType.Other => "Absent",
                    _ => "Absent"
                };
                effectiveTarget = 0; // No target on absence days
            }
            else if (dayOfWeek == DayOfWeek.Saturday || dayOfWeek == DayOfWeek.Sunday)
            {
                status = "Weekend";
            }
            else if (targetHours == 0)
            {
                status = "Day Off";
            }
            else if (dayEntries.Count > 0)
            {
                status = "Worked";
            }
            else
            {
                status = "Missing";
            }

            var overtime = netHours - effectiveTarget;
            cumulativeOvertime += overtime;

            // Build entries summary (start-end for each entry)
            var entrySummary = dayEntries.Count > 0
                ? string.Join(" | ", dayEntries.Select(e =>
                    $"{e.StartTime:HH:mm}-{e.EndTime?.ToString("HH:mm") ?? "?"}" +
                    (string.IsNullOrEmpty(e.Description) ? "" : $" ({e.Description})")))
                : "";

            var dateStr = currentDate.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture);
            var targetStr = effectiveTarget.ToString("F2", CultureInfo.InvariantCulture);
            var workedStr = (grossMinutes / 60.0).ToString("F2", CultureInfo.InvariantCulture);
            var pauseStr = pauseMinutes.ToString(CultureInfo.InvariantCulture);
            var netStr = netHours.ToString("F2", CultureInfo.InvariantCulture);
            var overtimeStr = overtime.ToString("+0.00;-0.00;0.00", CultureInfo.InvariantCulture);
            var cumulativeStr = cumulativeOvertime.ToString("+0.00;-0.00;0.00", CultureInfo.InvariantCulture);
            var holidayName = EscapeCsv(isHoliday ? holiday!.Name : "");
            var absenceType = isAbsence ? absence!.Type.ToString() : "";
            var absenceNote = EscapeCsv(isAbsence ? absence!.Note ?? "" : "");
            var entriesEscaped = EscapeCsv(entrySummary);

            sb.AppendLine($"{dateStr},{dayName},{targetStr},{workedStr},{pauseStr},{netStr},{overtimeStr},{cumulativeStr},{status},{holidayName},{absenceType},{absenceNote},{entriesEscaped}");

            currentDate = currentDate.AddDays(1);
        }

        _logger.LogInformation(
            "Daily report CSV generated for user {UserId} in org {OrgSlug}: {From} to {To}",
            userId, orgSlug, dateFrom, dateTo);

        return ServiceResult.Ok(Encoding.UTF8.GetBytes(sb.ToString()));
    }

    // ────────────────────────────────────────────────────
    //  Private helpers
    // ────────────────────────────────────────────────────

    /// <summary>
    /// Gets the target hours for a specific date from the applicable work schedule.
    /// </summary>
    private static double GetTargetHours(List<WorkSchedule> schedules, DateOnly date)
    {
        // Find the schedule that applies to this date (last one where ValidFrom <= date and ValidTo is null or >= date)
        var schedule = schedules
            .Where(ws => ws.ValidFrom <= date && (ws.ValidTo == null || ws.ValidTo >= date))
            .OrderByDescending(ws => ws.ValidFrom)
            .FirstOrDefault();

        if (schedule == null) return 0;

        return date.DayOfWeek switch
        {
            DayOfWeek.Monday => schedule.TargetMon,
            DayOfWeek.Tuesday => schedule.TargetTue,
            DayOfWeek.Wednesday => schedule.TargetWed,
            DayOfWeek.Thursday => schedule.TargetThu,
            DayOfWeek.Friday => schedule.TargetFri,
            _ => 0 // Saturday and Sunday have no target in the schema
        };
    }

    /// <summary>
    /// Escapes a field value for CSV format (RFC 4180).
    /// </summary>
    private static string EscapeCsv(string value)
    {
        if (string.IsNullOrEmpty(value)) return value;
        if (value.Contains('"') || value.Contains(',') || value.Contains('\n') || value.Contains('\r'))
        {
            return $"\"{value.Replace("\"", "\"\"")}\"";
        }
        return value;
    }
}
