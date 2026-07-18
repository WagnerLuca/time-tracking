using Asp.Versioning;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.RateLimiting;
using TimeTracking.Api.Filters;
using TimeTracking.Api.Services;

namespace TimeTracking.Api.Controllers;

/// <summary>
/// Generates printable PDF working-hours reports for organization members.
/// </summary>
[ApiController]
[ApiVersion("1.0")]
[Route("api/v{version:apiVersion}/organizations")]
[Authorize]
[EnableRateLimiting("General")]
public class ReportsController : OrganizationBaseController
{
    private readonly IPdfReportService _reportService;

    public ReportsController(IPdfReportService reportService)
    {
        _reportService = reportService;
    }

    /// <summary>
    /// Downloads a day-by-day PDF report for one calendar month.
    /// Allowed for the member themselves, org Admins/Owners, or a Group Admin with export capability.
    /// </summary>
    /// <param name="slug">Organization URL slug.</param>
    /// <param name="memberId">Target member user ID.</param>
    /// <param name="year">Report year.</param>
    /// <param name="month">Report month (1-12).</param>
    [HttpGet("{slug}/members/{memberId}/reports/monthly")]
    [RequireOrgRole]
    [ProducesResponseType(typeof(FileContentResult), StatusCodes.Status200OK)]
    public async Task<IActionResult> GetMonthlyReport(string slug, int memberId, [FromQuery] int year, [FromQuery] int month)
    {
        var callerId = GetCurrentUserId();
        if (callerId == null) return Unauthorized();

        var result = await _reportService.GenerateMonthlyReportAsync(slug, callerId.Value, memberId, year, month);
        if (!result.IsSuccess) return ToResponse(result);

        var fileName = $"time-report-{slug}-member{memberId}-{year:0000}-{month:00}.pdf";
        return File(result.Data!, "application/pdf", fileName);
    }

    /// <summary>
    /// Downloads a month-by-month PDF summary report for one calendar year.
    /// Allowed for the member themselves, org Admins/Owners, or a Group Admin with export capability.
    /// </summary>
    /// <param name="slug">Organization URL slug.</param>
    /// <param name="memberId">Target member user ID.</param>
    /// <param name="year">Report year.</param>
    [HttpGet("{slug}/members/{memberId}/reports/yearly")]
    [RequireOrgRole]
    [ProducesResponseType(typeof(FileContentResult), StatusCodes.Status200OK)]
    public async Task<IActionResult> GetYearlyReport(string slug, int memberId, [FromQuery] int year)
    {
        var callerId = GetCurrentUserId();
        if (callerId == null) return Unauthorized();

        var result = await _reportService.GenerateYearlyReportAsync(slug, callerId.Value, memberId, year);
        if (!result.IsSuccess) return ToResponse(result);

        var fileName = $"time-report-{slug}-member{memberId}-{year:0000}.pdf";
        return File(result.Data!, "application/pdf", fileName);
    }
}
