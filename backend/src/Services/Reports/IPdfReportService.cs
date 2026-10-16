using TimeTracking.Api.Models.Dtos;

namespace TimeTracking.Api.Services;

/// <summary>
/// Generates printable PDF working-hours reports for a member, month-based or year-based.
/// </summary>
public interface IPdfReportService
{
    /// <summary>
    /// Generates a day-by-day PDF report for one calendar month.
    /// Allowed for the member themselves, org Admins/Owners, or Group Admins with export capability.
    /// <paramref name="forAccounting"/>: the caller was authorized for the accounting panel — no visibility
    /// check, former members included, absence notes left out.
    /// </summary>
    Task<ServiceResult<byte[]>> GenerateMonthlyReportAsync(
        string slug, int callerUserId, int targetUserId, int year, int month, string? lang = null, bool forAccounting = false);

    /// <summary>
    /// Generates a month-by-month PDF summary report for one calendar year.
    /// Allowed for the member themselves, org Admins/Owners, or Group Admins with export capability.
    /// </summary>
    Task<ServiceResult<byte[]>> GenerateYearlyReportAsync(
        string slug, int callerUserId, int targetUserId, int year, string? lang = null);

    /// <summary>
    /// Current balance (cumulative overtime, today / this week / this month) using the report
    /// rules, so the dashboard and the PDF can never disagree. Same permissions as the reports.
    /// </summary>
    Task<ServiceResult<BalanceResponse>> GetBalanceAsync(string slug, int callerUserId, int targetUserId);
}
