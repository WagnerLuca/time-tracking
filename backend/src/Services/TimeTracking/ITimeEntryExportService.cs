using TimeTracking.Api.Models.Dtos;

namespace TimeTracking.Api.Services;

/// <summary>
/// Generates CSV exports and handles CSV imports of time entries.
/// </summary>
public interface ITimeEntryExportService
{
    /// <summary>
    /// Exports a user's time entries as a CSV byte array with selectable columns.
    /// </summary>
    Task<ServiceResult<byte[]>> ExportEntriesCsvAsync(int userId, ExportRequest request);

    /// <summary>
    /// Exports a comprehensive daily report for a user in an organization as CSV with selectable columns.
    /// </summary>
    Task<ServiceResult<byte[]>> ExportDailyReportCsvAsync(int userId, ExportRequest request);

    /// <summary>
    /// Previews a CSV import by parsing the file and returning rows to review.
    /// </summary>
    Task<ServiceResult<ImportPreviewResponse>> PreviewImportAsync(int userId, string orgSlug, byte[] csvData);

    /// <summary>
    /// Confirms and executes a CSV import, creating time entries from the parsed data.
    /// </summary>
    Task<ServiceResult<ImportResultResponse>> ConfirmImportAsync(int userId, string orgSlug, List<ImportEntryRequest> entries);
}
