namespace TimeTracking.Api.Services;

/// <summary>
/// Generates CSV exports of time entries.
/// </summary>
public interface ITimeEntryExportService
{
    /// <summary>
    /// Exports a user's time entries as a simple CSV byte array (no org context).
    /// </summary>
    Task<ServiceResult<byte[]>> ExportCsvAsync(int userId, int? organizationId, DateTime? from, DateTime? to);

    /// <summary>
    /// Exports a comprehensive daily report for a user in an organization as CSV.
    /// Groups by day, includes target hours, overtime tracking, holidays, and absences.
    /// </summary>
    Task<ServiceResult<byte[]>> ExportDailyReportCsvAsync(int userId, string orgSlug, DateTime? from, DateTime? to);
}
