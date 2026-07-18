namespace TimeTracking.Api.Services;

/// <summary>
/// Generates printable PDF working-hours reports for a member, month-based or year-based.
/// </summary>
public interface IPdfReportService
{
    /// <summary>
    /// Generates a day-by-day PDF report for one calendar month.
    /// Allowed for the member themselves, org Admins/Owners, or Group Admins with export capability.
    /// </summary>
    Task<ServiceResult<byte[]>> GenerateMonthlyReportAsync(
        string slug, int callerUserId, int targetUserId, int year, int month);

    /// <summary>
    /// Generates a month-by-month PDF summary report for one calendar year.
    /// Allowed for the member themselves, org Admins/Owners, or Group Admins with export capability.
    /// </summary>
    Task<ServiceResult<byte[]>> GenerateYearlyReportAsync(
        string slug, int callerUserId, int targetUserId, int year);
}
