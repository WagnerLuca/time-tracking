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
        "Description", "Organization", "Status"
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

    public async Task<ServiceResult<byte[]>> ExportEntriesCsvAsync(int callerUserId, ExportRequest request)
    {
        var (userId, error) = await ResolveEffectiveUserIdAsync(callerUserId, request);
        if (error != null) return ServiceResult.Fail<byte[]>(error.Value.Type, error.Value.Message);

        var query = _context.TimeEntries
            .AsNoTracking()
            .Include(e => e.Organization)
            .Where(e => e.UserId == userId && !e.IsRunning)
            .AsQueryable();

        Organization? filterOrg = null;
        if (!string.IsNullOrEmpty(request.OrganizationSlug))
        {
            filterOrg = await _context.Organizations.AsNoTracking()
                .FirstOrDefaultAsync(o => o.Slug == request.OrganizationSlug);
            if (filterOrg == null)
                return ServiceResult.NotFound<byte[]>("Organization not found.");
            var denied = await MemberGateAsync(filterOrg, callerUserId);
            if (denied != null) return ServiceResult.Fail<byte[]>(denied.Value.Type, denied.Value.Message);
            query = query.Where(e => e.OrganizationId == filterOrg.Id);
        }

        var rangeError = EntryTimes.ValidateRange(
            request.From.HasValue ? DateOnly.FromDateTime(request.From.Value) : null,
            request.To.HasValue ? DateOnly.FromDateTime(request.To.Value) : null);
        if (rangeError != null) return ServiceResult.BadRequest<byte[]>(rangeError);

        // From/To are local calendar days of the organization (inclusive)
        var rangeClock = OrgClock.For(filterOrg);
        if (request.From.HasValue)
        {
            var fromUtc = rangeClock.StartOfDayUtc(DateOnly.FromDateTime(request.From.Value));
            query = query.Where(e => e.StartTime >= fromUtc);
        }
        if (request.To.HasValue)
        {
            var toUtc = rangeClock.StartOfDayUtc(DateOnly.FromDateTime(request.To.Value).AddDays(1));
            query = query.Where(e => e.StartTime < toUtc);
        }

        var entries = await query.OrderBy(e => e.StartTime).ToListAsync();

        var columns = ResolveColumns(request.Columns, AllEntryColumns);
        var text = ReportText.For(request.Lang);
        var sb = new StringBuilder();

        // Header
        sb.AppendLine(string.Join(",", columns.Select(c => text["col." + c])));

        foreach (var entry in entries)
        {
            var clock = entry.Organization != null ? OrgClock.For(entry.Organization) : rangeClock;
            var localStart = clock.ToLocal(entry.StartTime);
            var localEnd = entry.EndTime.HasValue ? clock.ToLocal(entry.EndTime.Value) : (DateTime?)null;
            // Failure entries have no trustworthy duration until corrected
            var durationMinutes = entry.EndTime.HasValue && !entry.IsFailure
                ? (entry.EndTime.Value - entry.StartTime).TotalMinutes
                : 0;
            var netMinutes = Math.Max(0, durationMinutes - entry.PauseDurationMinutes);
            var values = new List<string>();

            foreach (var col in columns)
            {
                values.Add(col switch
                {
                    "Date" => localStart.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture),
                    "DayOfWeek" => text.Day(localStart.DayOfWeek),
                    "StartTime" => localStart.ToString("HH:mm", CultureInfo.InvariantCulture),
                    "EndTime" => localEnd?.ToString("HH:mm", CultureInfo.InvariantCulture) ?? "",
                    "Duration" => (durationMinutes / 60.0).ToString("F2", CultureInfo.InvariantCulture),
                    "Pause" => entry.PauseDurationMinutes.ToString(CultureInfo.InvariantCulture),
                    "NetDuration" => (netMinutes / 60.0).ToString("F2", CultureInfo.InvariantCulture),
                    "Description" => EscapeCsv(entry.Description ?? ""),
                    "Organization" => EscapeCsv(entry.Organization?.Name ?? ""),
                    "Status" => entry.IsFailure ? text.Status("Needs Correction") : "",
                    _ => ""
                });
            }

            sb.AppendLine(string.Join(",", values));
        }

        _logger.LogInformation("Entries CSV export for user {UserId}: {Count} entries, columns: {Columns}",
            userId, entries.Count, string.Join(";", columns));

        return ServiceResult.Ok(Encoding.UTF8.GetBytes(sb.ToString()));
    }

    public async Task<ServiceResult<byte[]>> ExportDailyReportCsvAsync(int callerUserId, ExportRequest request)
    {
        if (string.IsNullOrEmpty(request.OrganizationSlug))
            return ServiceResult.BadRequest<byte[]>("Organization slug is required for daily report export.");

        var (userId, error) = await ResolveEffectiveUserIdAsync(callerUserId, request);
        if (error != null) return ServiceResult.Fail<byte[]>(error.Value.Type, error.Value.Message);

        var org = await _context.Organizations.AsNoTracking()
            .FirstOrDefaultAsync(o => o.Slug == request.OrganizationSlug);
        if (org == null)
            return ServiceResult.NotFound<byte[]>("Organization not found.");
        var denied = await MemberGateAsync(org, callerUserId);
        if (denied != null) return ServiceResult.Fail<byte[]>(denied.Value.Type, denied.Value.Message);

        var membership = await _context.UserOrganizations.AsNoTracking()
            .FirstOrDefaultAsync(uo => uo.OrganizationId == org.Id && uo.UserId == userId);
        if (membership == null)
            return ServiceResult.Forbidden<byte[]>("You are not a member of this organization.");

        var localToday = OrgClock.For(org).Today();
        var dateFrom = request.From.HasValue ? DateOnly.FromDateTime(request.From.Value) : new DateOnly(localToday.Year, localToday.Month, 1);
        var dateTo = request.To.HasValue ? DateOnly.FromDateTime(request.To.Value) : dateFrom.AddMonths(1).AddDays(-1);
        var rangeError = EntryTimes.ValidateRange(dateFrom, dateTo);
        if (rangeError != null) return ServiceResult.BadRequest<byte[]>(rangeError);

        var rows = await DailyReportAggregator.BuildAsync(
            _context, userId, org.Id, dateFrom, dateTo, membership.InitialOvertimeHours);
        // Cumulative overtime is the same running total as the balance and the PDF (from the first
        // tracked entry plus initial overtime), not one restarted at dateFrom. Days before the first
        // entry are listed but do not move the balance yet.
        var (running, _) = await DailyReportAggregator.BuildWithRunningTotalAsync(
            _context, userId, org.Id, dateFrom, dateTo, membership.InitialOvertimeHours);
        var runningByDate = running.ToDictionary(r => r.Date, r => r.CumulativeOvertime);
        rows = rows.Select(r => r with
        {
            CumulativeOvertime = runningByDate.TryGetValue(r.Date, out var c) ? c : membership.InitialOvertimeHours
        }).ToList();

        var columns = ResolveColumns(request.Columns, AllDailyColumns);
        var text = ReportText.For(request.Lang);
        var sb = new StringBuilder();
        sb.AppendLine(string.Join(",", columns.Select(c => text["col." + c])));

        foreach (var row in rows)
        {
            var values = new List<string>();
            foreach (var col in columns)
            {
                values.Add(col switch
                {
                    "Date" => row.Date.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture),
                    "DayOfWeek" => text.Day(row.DayOfWeek),
                    "TargetHours" => row.TargetHours.ToString("F2", CultureInfo.InvariantCulture),
                    "WorkedHours" => row.WorkedHours.ToString("F2", CultureInfo.InvariantCulture),
                    "Pause" => row.PauseMinutes.ToString(CultureInfo.InvariantCulture),
                    "NetWorkedHours" => row.NetWorkedHours.ToString("F2", CultureInfo.InvariantCulture),
                    "Overtime" => row.Overtime.ToString("+0.00;-0.00;0.00", CultureInfo.InvariantCulture),
                    "CumulativeOvertime" => row.CumulativeOvertime.ToString("+0.00;-0.00;0.00", CultureInfo.InvariantCulture),
                    "Status" => EscapeCsv(text.Status(row.Status)),
                    "HolidayName" => EscapeCsv(row.HolidayName ?? ""),
                    "AbsenceType" => row.AbsenceType is { } absenceType ? text.Absence(absenceType) : "",
                    "AbsenceNote" => EscapeCsv(row.AbsenceNote ?? ""),
                    "Description" => EscapeCsv(text.EntrySummary(row.EntrySummary)),
                    "Organization" => EscapeCsv(org.Name),
                    _ => ""
                });
            }

            sb.AppendLine(string.Join(",", values));
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
        var denied = await MemberGateAsync(org, userId);
        if (denied != null) return ServiceResult.Fail<ImportPreviewResponse>(denied.Value.Type, denied.Value.Message);

        if (await EffectiveRuleResolver.ResolveAsync(_context, org, userId, RequestType.CsvImport) == RuleMode.Disabled)
            return ServiceResult.Forbidden<ImportPreviewResponse>("CSV import is disabled in this organization.");

        var csvText = Encoding.UTF8.GetString(csvData);
        var lines = csvText.Split(["\r\n", "\n"], StringSplitOptions.None)
            .Where(l => !string.IsNullOrWhiteSpace(l)).ToList();

        if (lines.Count < 2)
            return ServiceResult.BadRequest<ImportPreviewResponse>("CSV file must have a header row and at least one data row.");
        if (lines.Count - 1 > EntryTimes.MaxImportRows)
            return ServiceResult.BadRequest<ImportPreviewResponse>($"A CSV file can hold at most {EntryTimes.MaxImportRows} entries. Split it into several files.");

        var headers = ParseCsvLine(lines[0]).Select(h => h.Trim().ToLowerInvariant()).ToList();

        // English and German headers, so both export languages import again
        var dateIdx = FindColumnIndex(headers, "date", "datum");
        var startIdx = FindColumnIndex(headers, "start time", "starttime", "start", "beginn");
        var endIdx = FindColumnIndex(headers, "end time", "endtime", "end", "ende");
        var pauseIdx = FindColumnIndex(headers, "pause", "pause (min)", "pause minutes");
        var descIdx = FindColumnIndex(headers, "description", "notes", "desc", "beschreibung");

        if (dateIdx < 0 || startIdx < 0 || endIdx < 0)
            return ServiceResult.BadRequest<ImportPreviewResponse>(
                "CSV must contain 'Date', 'Start Time', and 'End Time' columns.");

        var existingEntries = await _context.TimeEntries.AsNoTracking()
            .Where(e => e.UserId == userId && e.OrganizationId == org.Id && !e.IsRunning)
            .Select(e => new { e.StartTime, e.EndTime })
            .ToListAsync();

        var importClock = OrgClock.For(org);
        var existingSet = existingEntries
            .Select(e => importClock.ToLocal(e.StartTime))
            .Select(local => $"{local:yyyy-MM-dd}|{local:HH:mm}")
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

            var warning = ParseImportRow(importClock, date, start, end, pauseMins, desc, out _, out _);

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
        var denied = await MemberGateAsync(org, userId);
        if (denied != null) return ServiceResult.Fail<ImportResultResponse>(denied.Value.Type, denied.Value.Message);
        if (entries.Count > EntryTimes.MaxImportRows)
            return ServiceResult.BadRequest<ImportResultResponse>($"An import can hold at most {EntryTimes.MaxImportRows} entries.");

        var csvImportMode = await EffectiveRuleResolver.ResolveAsync(_context, org, userId, RequestType.CsvImport);
        if (csvImportMode == RuleMode.Disabled)
            return ServiceResult.Forbidden<ImportResultResponse>("CSV import is disabled in this organization.");
        if (csvImportMode == RuleMode.RequiresApproval)
            return ServiceResult.Forbidden<ImportResultResponse>(
                "CSV import requires admin approval in this organization. Please submit a request.");

        var imported = 0;
        var skipped = 0;
        var errors = new List<string>();

        var clock = OrgClock.For(org);
        foreach (var entry in entries)
        {
            var problem = ParseImportRow(clock, entry.Date, entry.StartTime, entry.EndTime, entry.PauseMinutes, entry.Description,
                out var startUtc, out var endUtc);
            if (problem != null)
            {
                errors.Add($"{entry.Date} {entry.StartTime}: {problem}");
                skipped++;
                continue;
            }

            var timeEntry = new TimeEntry
            {
                UserId = userId,
                OrganizationId = org.Id,
                StartTime = startUtc,
                EndTime = endUtc,
                IsRunning = false,
                PauseDurationMinutes = entry.PauseMinutes,
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

    // ── Private helpers ──

    /// <summary>
    /// Resolves which user's data to export: the caller themselves by default, or
    /// <see cref="ExportRequest.TargetUserId"/> when set and the caller has export
    /// permission for that member (self, org Admin/Owner, or a Group Admin of a
    /// shared group with the export-data capability). Requires an organization slug
    /// whenever a different target is requested, since permissions are org-scoped.
    /// </summary>
    private async Task<(int UserId, (ServiceErrorType Type, string Message)? Error)> ResolveEffectiveUserIdAsync(
        int callerUserId, ExportRequest request)
    {
        if (!request.TargetUserId.HasValue || request.TargetUserId.Value == callerUserId)
            return (callerUserId, null);

        if (string.IsNullOrEmpty(request.OrganizationSlug))
            return (callerUserId, (ServiceErrorType.BadRequest, "Organization slug is required to export another member's data."));

        var org = await _context.Organizations.AsNoTracking()
            .FirstOrDefaultAsync(o => o.Slug == request.OrganizationSlug);
        if (org == null)
            return (callerUserId, (ServiceErrorType.NotFound, "Organization not found."));

        var isMember = await _context.UserOrganizations.AsNoTracking()
            .AnyAsync(uo => uo.OrganizationId == org.Id && uo.UserId == request.TargetUserId.Value);
        if (!isMember)
            return (callerUserId, (ServiceErrorType.NotFound, "Member not found in this organization."));

        var allowed = await EffectiveRuleResolver.CanExportDataForMemberAsync(_context, org.Id, callerUserId, request.TargetUserId.Value);
        if (!allowed)
            return (callerUserId, (ServiceErrorType.Forbidden, "You do not have permission to export this member's data."));

        return (request.TargetUserId.Value, null);
    }

    /// <summary>Active member who meets the organization's 2FA requirement, or the reason why not.</summary>
    private async Task<(ServiceErrorType Type, string Message)?> MemberGateAsync(Organization org, int userId)
    {
        var member = await _context.UserOrganizations.AsNoTracking()
            .Where(uo => uo.OrganizationId == org.Id && uo.UserId == userId)
            .Select(uo => new { uo.User.TwoFactorEnabled })
            .FirstOrDefaultAsync();
        if (member == null) return (ServiceErrorType.Forbidden, "You are not a member of this organization.");
        if (org.Require2fa && !member.TwoFactorEnabled)
            return (ServiceErrorType.Forbidden, "This organization requires two-factor authentication. Enable it in your account settings to continue.");
        return null;
    }

    /// <summary>
    /// Parses one CSV row (organization-local wall-clock times) and checks it like a normal entry.
    /// Returns the problem, or null with the UTC times set.
    /// </summary>
    internal static string? ParseImportRow(OrgClock clock, string? date, string? start, string? end, int pauseMinutes, string? description,
        out DateTime startUtc, out DateTime endUtc)
    {
        startUtc = endUtc = default;
        if (!DateTime.TryParse($"{date} {start}", CultureInfo.InvariantCulture, DateTimeStyles.None, out var localStart))
            return "Invalid date/start time format";
        if (!DateTime.TryParse($"{date} {end}", CultureInfo.InvariantCulture, DateTimeStyles.None, out var localEnd))
            return "Invalid end time format";
        if (pauseMinutes is < 0 or > 1440) return "Pause must be between 0 and 1440 minutes.";
        if (description?.Length > 500) return "Description is longer than 500 characters.";

        startUtc = clock.ToUtc(localStart);
        endUtc = clock.ToUtc(localEnd);
        return EntryTimes.Validate(startUtc, endUtc);
    }

    private static List<string> ResolveColumns(List<string>? requested, string[] available)
    {
        if (requested == null || requested.Count == 0)
            return [.. available];
        return requested.Where(c => available.Contains(c, StringComparer.OrdinalIgnoreCase)).ToList();
    }

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
            DayOfWeek.Saturday => schedule.TargetSat,
            DayOfWeek.Sunday => schedule.TargetSun,
            _ => 0
        };
    }

    private static string EscapeCsv(string value)
    {
        if (string.IsNullOrEmpty(value)) return value;
        // Spreadsheets execute cells starting with these characters as formulas (CSV injection)
        if (value[0] is '=' or '+' or '-' or '@' or '\t' or '\r')
            value = "'" + value;
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
