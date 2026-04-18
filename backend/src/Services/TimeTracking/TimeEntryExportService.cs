using System.Globalization;
using System.Text;
using Microsoft.EntityFrameworkCore;
using TimeTracking.Api.Data;
using TimeTracking.Api.Models;
using TimeTracking.Api.Models.Dtos;

namespace TimeTracking.Api.Services;

/// <summary>
/// Generates CSV exports and handles CSV imports of time entries.
/// </summary>
public class TimeEntryExportService : ITimeEntryExportService
{
    private readonly TimeTrackingDbContext _context;
    private readonly ILogger<TimeEntryExportService> _logger;

    // All available columns for entries export
    private static readonly string[] AllEntryColumns =
    [
        "Date", "DayOfWeek", "StartTime", "EndTime",
        "Duration", "Pause", "NetDuration",
        "Description", "Organization"
    ];

    // All available columns for daily report export
    private static readonly string[] AllDailyColumns =
    [
        "Date", "DayOfWeek", "TargetHours", "WorkedHours",
        "Pause", "NetWorkedHours", "Overtime", "CumulativeOvertime",
        "Status", "HolidayName", "AbsenceType", "AbsenceNote",
        "Description", "Organization"
    ];

    public TimeEntryExportService(TimeTrackingDbContext context, ILogger<TimeEntryExportService> logger)
    {
        _context = context;
        _logger = logger;
    }

    public async Task<ServiceResult<byte[]>> ExportEntriesCsvAsync(int userId, ExportRequest request)
    {
        var query = _context.TimeEntries
            .AsNoTracking()
            .Include(e => e.Organization)
            .Where(e => e.UserId == userId && !e.IsRunning)
            .AsQueryable();

        if (!string.IsNullOrEmpty(request.OrganizationSlug))
        {
            var org = await _context.Organizations.AsNoTracking()
                .FirstOrDefaultAsync(o => o.Slug == request.OrganizationSlug);
            if (org == null)
                return ServiceResult.NotFound<byte[]>("Organization not found.");
            query = query.Where(e => e.OrganizationId == org.Id);
        }

        if (request.From.HasValue)
            query = query.Where(e => e.StartTime >= request.From.Value);
        if (request.To.HasValue)
            query = query.Where(e => e.StartTime <= request.To.Value.Date.AddDays(1));

        var entries = await query.OrderBy(e => e.StartTime).ToListAsync();

        var columns = ResolveColumns(request.Columns, AllEntryColumns);
        var sb = new StringBuilder();

        // Header
        sb.AppendLine(string.Join(",", columns.Select(FormatColumnHeader)));

        foreach (var entry in entries)
        {
            var durationMinutes = entry.EndTime.HasValue
                ? (entry.EndTime.Value - entry.StartTime).TotalMinutes
                : 0;
            var netMinutes = Math.Max(0, durationMinutes - entry.PauseDurationMinutes);
            var values = new List<string>();

            foreach (var col in columns)
            {
                values.Add(col switch
                {
                    "Date" => entry.StartTime.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture),
                    "DayOfWeek" => entry.StartTime.DayOfWeek.ToString()[..3],
                    "StartTime" => entry.StartTime.ToString("HH:mm", CultureInfo.InvariantCulture),
                    "EndTime" => entry.EndTime?.ToString("HH:mm", CultureInfo.InvariantCulture) ?? "",
                    "Duration" => (durationMinutes / 60.0).ToString("F2", CultureInfo.InvariantCulture),
                    "Pause" => entry.PauseDurationMinutes.ToString(CultureInfo.InvariantCulture),
                    "NetDuration" => (netMinutes / 60.0).ToString("F2", CultureInfo.InvariantCulture),
                    "Description" => EscapeCsv(entry.Description ?? ""),
                    "Organization" => EscapeCsv(entry.Organization?.Name ?? ""),
                    _ => ""
                });
            }

            sb.AppendLine(string.Join(",", values));
        }

        _logger.LogInformation("Entries CSV export for user {UserId}: {Count} entries, columns: {Columns}",
            userId, entries.Count, string.Join(";", columns));

        return ServiceResult.Ok(Encoding.UTF8.GetBytes(sb.ToString()));
    }

    public async Task<ServiceResult<byte[]>> ExportDailyReportCsvAsync(int userId, ExportRequest request)
    {
        if (string.IsNullOrEmpty(request.OrganizationSlug))
            return ServiceResult.BadRequest<byte[]>("Organization slug is required for daily report export.");

        var org = await _context.Organizations.AsNoTracking()
            .FirstOrDefaultAsync(o => o.Slug == request.OrganizationSlug);
        if (org == null)
            return ServiceResult.NotFound<byte[]>("Organization not found.");

        var membership = await _context.UserOrganizations.AsNoTracking()
            .FirstOrDefaultAsync(uo => uo.OrganizationId == org.Id && uo.UserId == userId);
        if (membership == null)
            return ServiceResult.Forbidden<byte[]>("You are not a member of this organization.");

        var rangeFrom = request.From ?? new DateTime(DateTime.UtcNow.Year, DateTime.UtcNow.Month, 1, 0, 0, 0, DateTimeKind.Utc);
        var rangeTo = request.To ?? rangeFrom.AddMonths(1).AddDays(-1);

        var dateFrom = DateOnly.FromDateTime(rangeFrom);
        var dateTo = DateOnly.FromDateTime(rangeTo);

        var timeEntries = await _context.TimeEntries.AsNoTracking()
            .Where(e => e.UserId == userId && e.OrganizationId == org.Id && !e.IsRunning
                     && e.StartTime >= rangeFrom && e.StartTime <= rangeTo.AddDays(1))
            .OrderBy(e => e.StartTime)
            .ToListAsync();

        var workSchedules = await _context.WorkSchedules.AsNoTracking()
            .Where(ws => ws.UserId == userId && ws.OrganizationId == org.Id)
            .OrderBy(ws => ws.ValidFrom)
            .ToListAsync();

        var holidays = await _context.Holidays.AsNoTracking()
            .Where(h => h.OrganizationId == org.Id
                     && ((h.Date >= dateFrom && h.Date <= dateTo) || h.IsRecurring))
            .ToListAsync();

        var absences = await _context.AbsenceDays.AsNoTracking()
            .Where(a => a.UserId == userId && a.OrganizationId == org.Id
                     && a.Date >= dateFrom && a.Date <= dateTo)
            .ToListAsync();

        var entriesByDate = timeEntries
            .GroupBy(e => DateOnly.FromDateTime(e.StartTime))
            .ToDictionary(g => g.Key, g => g.ToList());

        var holidaysByDate = new Dictionary<DateOnly, Holiday>();
        foreach (var h in holidays)
        {
            if (h.IsRecurring)
            {
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

        var columns = ResolveColumns(request.Columns, AllDailyColumns);
        var sb = new StringBuilder();
        sb.AppendLine(string.Join(",", columns.Select(FormatColumnHeader)));

        var cumulativeOvertime = membership.InitialOvertimeHours;
        var currentDate = dateFrom;

        while (currentDate <= dateTo)
        {
            var dayOfWeek = currentDate.DayOfWeek;
            var targetHours = GetTargetHours(workSchedules, currentDate);
            var dayEntries = entriesByDate.GetValueOrDefault(currentDate) ?? [];

            var grossMinutes = dayEntries.Sum(e => e.EndTime.HasValue
                ? (e.EndTime.Value - e.StartTime).TotalMinutes : 0);
            var pauseMinutes = dayEntries.Sum(e => e.PauseDurationMinutes);
            var netMinutes = Math.Max(0, grossMinutes - pauseMinutes);
            var netHours = netMinutes / 60.0;

            var isHoliday = holidaysByDate.TryGetValue(currentDate, out var holiday);
            var isAbsence = absencesByDate.TryGetValue(currentDate, out var absence);

            // Compute effective target by subtracting holiday and absence credits
            var holidayCredit = isHoliday ? (holiday!.IsHalfDay ? targetHours / 2.0 : targetHours) : 0;
            var absenceCredit = isAbsence ? (absence!.IsHalfDay ? targetHours / 2.0 : targetHours) : 0;
            double effectiveTarget = Math.Max(0, targetHours - holidayCredit - absenceCredit);
            string status;

            if (isHoliday && isAbsence)
            {
                status = absence!.Type switch
                {
                    AbsenceType.SickDay => "Sick",
                    AbsenceType.Vacation => "Vacation",
                    _ => "Absent"
                };
            }
            else if (isHoliday) { status = holiday!.IsHalfDay ? "Half Holiday" : "Holiday"; }
            else if (isAbsence)
            {
                status = absence!.Type switch
                {
                    AbsenceType.SickDay => "Sick",
                    AbsenceType.Vacation => "Vacation",
                    _ => "Absent"
                };
            }
            else if (dayOfWeek is DayOfWeek.Saturday or DayOfWeek.Sunday) { status = "Weekend"; }
            else if (targetHours == 0) { status = "Day Off"; }
            else if (dayEntries.Count > 0) { status = "Worked"; }
            else { status = "Missing"; }

            var overtime = netHours - effectiveTarget;
            cumulativeOvertime += overtime;

            var entrySummary = dayEntries.Count > 0
                ? string.Join(" | ", dayEntries.Select(e =>
                    $"{e.StartTime:HH:mm}-{e.EndTime?.ToString("HH:mm") ?? "?"}" +
                    (string.IsNullOrEmpty(e.Description) ? "" : $" ({e.Description})")))
                : "";

            var values = new List<string>();
            foreach (var col in columns)
            {
                values.Add(col switch
                {
                    "Date" => currentDate.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture),
                    "DayOfWeek" => dayOfWeek.ToString()[..3],
                    "TargetHours" => effectiveTarget.ToString("F2", CultureInfo.InvariantCulture),
                    "WorkedHours" => (grossMinutes / 60.0).ToString("F2", CultureInfo.InvariantCulture),
                    "Pause" => pauseMinutes.ToString(CultureInfo.InvariantCulture),
                    "NetWorkedHours" => netHours.ToString("F2", CultureInfo.InvariantCulture),
                    "Overtime" => overtime.ToString("+0.00;-0.00;0.00", CultureInfo.InvariantCulture),
                    "CumulativeOvertime" => cumulativeOvertime.ToString("+0.00;-0.00;0.00", CultureInfo.InvariantCulture),
                    "Status" => status,
                    "HolidayName" => EscapeCsv(isHoliday ? holiday!.Name : ""),
                    "AbsenceType" => isAbsence ? absence!.Type.ToString() : "",
                    "AbsenceNote" => EscapeCsv(isAbsence ? absence!.Note ?? "" : ""),
                    "Description" => EscapeCsv(entrySummary),
                    "Organization" => EscapeCsv(org.Name),
                    _ => ""
                });
            }

            sb.AppendLine(string.Join(",", values));
            currentDate = currentDate.AddDays(1);
        }

        _logger.LogInformation("Daily report CSV for user {UserId} in org {OrgSlug}: {From} to {To}",
            userId, request.OrganizationSlug, dateFrom, dateTo);

        return ServiceResult.Ok(Encoding.UTF8.GetBytes(sb.ToString()));
    }

    public async Task<ServiceResult<ImportPreviewResponse>> PreviewImportAsync(
        int userId, string orgSlug, byte[] csvData)
    {
        var org = await _context.Organizations.AsNoTracking()
            .FirstOrDefaultAsync(o => o.Slug == orgSlug);
        if (org == null)
            return ServiceResult.NotFound<ImportPreviewResponse>("Organization not found.");

        var membership = await _context.UserOrganizations.AsNoTracking()
            .FirstOrDefaultAsync(uo => uo.OrganizationId == org.Id && uo.UserId == userId);
        if (membership == null)
            return ServiceResult.Forbidden<ImportPreviewResponse>("You are not a member of this organization.");

        if (org.CsvImportMode == RuleMode.Disabled)
            return ServiceResult.Forbidden<ImportPreviewResponse>("CSV import is disabled in this organization.");

        var csvText = Encoding.UTF8.GetString(csvData);
        var lines = csvText.Split(["\r\n", "\n"], StringSplitOptions.None)
            .Where(l => !string.IsNullOrWhiteSpace(l)).ToList();

        if (lines.Count < 2)
            return ServiceResult.BadRequest<ImportPreviewResponse>("CSV file must have a header row and at least one data row.");

        var headers = ParseCsvLine(lines[0]).Select(h => h.Trim().ToLowerInvariant()).ToList();

        var dateIdx = FindColumnIndex(headers, "date");
        var startIdx = FindColumnIndex(headers, "start time", "starttime", "start");
        var endIdx = FindColumnIndex(headers, "end time", "endtime", "end");
        var pauseIdx = FindColumnIndex(headers, "pause", "pause (min)", "pause minutes");
        var descIdx = FindColumnIndex(headers, "description", "notes", "desc");

        if (dateIdx < 0 || startIdx < 0 || endIdx < 0)
            return ServiceResult.BadRequest<ImportPreviewResponse>(
                "CSV must contain 'Date', 'Start Time', and 'End Time' columns.");

        var existingEntries = await _context.TimeEntries.AsNoTracking()
            .Where(e => e.UserId == userId && e.OrganizationId == org.Id && !e.IsRunning)
            .Select(e => new { e.StartTime, e.EndTime })
            .ToListAsync();

        var existingSet = existingEntries
            .Select(e => $"{e.StartTime:yyyy-MM-dd}|{e.StartTime:HH:mm}")
            .ToHashSet();

        var rows = new List<ImportPreviewRow>();
        var warnings = new List<string>();
        var duplicateCount = 0;

        for (var i = 1; i < lines.Count; i++)
        {
            var fields = ParseCsvLine(lines[i]);
            if (fields.Count <= dateIdx || fields.Count <= startIdx || fields.Count <= endIdx)
            {
                warnings.Add($"Row {i}: insufficient columns, skipped.");
                continue;
            }

            var date = fields[dateIdx].Trim();
            var start = fields[startIdx].Trim();
            var end = fields[endIdx].Trim();
            var pause = pauseIdx >= 0 && pauseIdx < fields.Count ? fields[pauseIdx].Trim() : "0";
            var desc = descIdx >= 0 && descIdx < fields.Count ? fields[descIdx].Trim() : "";

            int.TryParse(pause, out var pauseMins);

            var isDuplicate = existingSet.Contains($"{date}|{start}");
            if (isDuplicate) duplicateCount++;

            string? warning = null;
            if (!DateTime.TryParse($"{date} {start}", CultureInfo.InvariantCulture, DateTimeStyles.None, out _))
                warning = "Invalid date/start time format";
            else if (!DateTime.TryParse($"{date} {end}", CultureInfo.InvariantCulture, DateTimeStyles.None, out _))
                warning = "Invalid end time format";

            rows.Add(new ImportPreviewRow
            {
                RowNumber = i,
                Date = date,
                StartTime = start,
                EndTime = end,
                PauseMinutes = pauseMins,
                Description = desc,
                IsDuplicate = isDuplicate,
                Warning = warning
            });
        }

        return ServiceResult.Ok(new ImportPreviewResponse
        {
            Rows = rows,
            TotalRows = rows.Count,
            DuplicateCount = duplicateCount,
            Warnings = warnings.Count > 0 ? warnings : null
        });
    }

    public async Task<ServiceResult<ImportResultResponse>> ConfirmImportAsync(
        int userId, string orgSlug, List<ImportEntryRequest> entries)
    {
        var org = await _context.Organizations.AsNoTracking()
            .FirstOrDefaultAsync(o => o.Slug == orgSlug);
        if (org == null)
            return ServiceResult.NotFound<ImportResultResponse>("Organization not found.");

        var membership = await _context.UserOrganizations.AsNoTracking()
            .FirstOrDefaultAsync(uo => uo.OrganizationId == org.Id && uo.UserId == userId);
        if (membership == null)
            return ServiceResult.Forbidden<ImportResultResponse>("You are not a member of this organization.");

        if (org.CsvImportMode == RuleMode.Disabled)
            return ServiceResult.Forbidden<ImportResultResponse>("CSV import is disabled in this organization.");
        if (org.CsvImportMode == RuleMode.RequiresApproval)
            return ServiceResult.Forbidden<ImportResultResponse>(
                "CSV import requires admin approval in this organization. Please submit a request.");

        var imported = 0;
        var skipped = 0;
        var errors = new List<string>();

        foreach (var entry in entries)
        {
            if (!DateTime.TryParse($"{entry.Date} {entry.StartTime}", CultureInfo.InvariantCulture,
                    DateTimeStyles.None, out var startTime))
            {
                errors.Add($"Invalid start: {entry.Date} {entry.StartTime}");
                skipped++;
                continue;
            }
            if (!DateTime.TryParse($"{entry.Date} {entry.EndTime}", CultureInfo.InvariantCulture,
                    DateTimeStyles.None, out var endTime))
            {
                errors.Add($"Invalid end: {entry.Date} {entry.EndTime}");
                skipped++;
                continue;
            }

            if (endTime <= startTime)
            {
                errors.Add($"End time must be after start time: {entry.Date}");
                skipped++;
                continue;
            }

            var timeEntry = new TimeEntry
            {
                UserId = userId,
                OrganizationId = org.Id,
                StartTime = DateTime.SpecifyKind(startTime, DateTimeKind.Utc),
                EndTime = DateTime.SpecifyKind(endTime, DateTimeKind.Utc),
                IsRunning = false,
                PauseDurationMinutes = Math.Max(0, entry.PauseMinutes),
                Description = entry.Description,
                CreatedAt = DateTime.UtcNow,
                UpdatedAt = DateTime.UtcNow
            };

            _context.TimeEntries.Add(timeEntry);
            imported++;
        }

        if (imported > 0)
            await _context.SaveChangesAsync();

        _logger.LogInformation("CSV import for user {UserId} in org {OrgSlug}: {Imported} imported, {Skipped} skipped",
            userId, orgSlug, imported, skipped);

        return ServiceResult.Ok(new ImportResultResponse
        {
            ImportedCount = imported,
            SkippedCount = skipped,
            Errors = errors.Count > 0 ? errors : null
        });
    }

    // â”€â”€ Private helpers â”€â”€

    private static List<string> ResolveColumns(List<string>? requested, string[] available)
    {
        if (requested == null || requested.Count == 0)
            return [.. available];
        return requested.Where(c => available.Contains(c, StringComparer.OrdinalIgnoreCase)).ToList();
    }

    private static string FormatColumnHeader(string col) => col switch
    {
        "DayOfWeek" => "Day",
        "StartTime" => "Start Time",
        "EndTime" => "End Time",
        "Duration" => "Duration (h)",
        "Pause" => "Pause (min)",
        "NetDuration" => "Net Duration (h)",
        "TargetHours" => "Target (h)",
        "WorkedHours" => "Worked (h)",
        "NetWorkedHours" => "Net Worked (h)",
        "Overtime" => "Overtime (h)",
        "CumulativeOvertime" => "Cumulative Overtime (h)",
        "HolidayName" => "Holiday",
        "AbsenceType" => "Absence Type",
        "AbsenceNote" => "Absence Note",
        _ => col
    };

    private static double GetTargetHours(List<WorkSchedule> schedules, DateOnly date)
    {
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
            _ => 0
        };
    }

    private static string EscapeCsv(string value)
    {
        if (string.IsNullOrEmpty(value)) return value;
        if (value.Contains('"') || value.Contains(',') || value.Contains('\n') || value.Contains('\r'))
            return $"\"{value.Replace("\"", "\"\"")}\"";
        return value;
    }

    private static int FindColumnIndex(List<string> headers, params string[] names)
    {
        foreach (var name in names)
        {
            var idx = headers.IndexOf(name.ToLowerInvariant());
            if (idx >= 0) return idx;
        }
        return -1;
    }

    private static List<string> ParseCsvLine(string line)
    {
        var fields = new List<string>();
        var current = new StringBuilder();
        var inQuotes = false;

        for (var i = 0; i < line.Length; i++)
        {
            var c = line[i];
            if (inQuotes)
            {
                if (c == '"')
                {
                    if (i + 1 < line.Length && line[i + 1] == '"')
                    {
                        current.Append('"');
                        i++;
                    }
                    else
                    {
                        inQuotes = false;
                    }
                }
                else
                {
                    current.Append(c);
                }
            }
            else
            {
                if (c == '"') { inQuotes = true; }
                else if (c == ',')
                {
                    fields.Add(current.ToString());
                    current.Clear();
                }
                else { current.Append(c); }
            }
        }

        fields.Add(current.ToString());
        return fields;
    }
}
