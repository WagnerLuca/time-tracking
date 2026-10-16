using Asp.Versioning;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.RateLimiting;
using TimeTracking.Api.Filters;
using TimeTracking.Api.Models;
using TimeTracking.Api.Models.Dtos;
using TimeTracking.Api.Services;

namespace TimeTracking.Api.Controllers;

/// <summary>
/// The accounting panel ("Auswertungen", docs/ACCOUNTING_ROLE_PLAN.md): admins, owners and accountants.
/// Accountants see everybody's hours; admins and owners only while the organization lets admins see members' hours.
/// Closing and reopening months: <see cref="ClosedPeriodsController"/>; payroll CSV: <see cref="ExportImportController"/>.
/// </summary>
[ApiController]
[ApiVersion("1.0")]
[Route("api/v{version:apiVersion}/organizations/{slug}/accounting")]
[Authorize]
[EnableRateLimiting("General")]
[RequireOrgRole(OrganizationRole.Admin, OrAccountant = true)]
public class AccountingController(AccountingService service, IPdfReportService reports) : OrganizationBaseController
{
    /// <summary>One row per member for a month: target, worked, overtime, balance, absences, entries needing correction.</summary>
    [HttpGet("summary")]
    [ProducesResponseType(typeof(AccountingSummaryResponse), StatusCodes.Status200OK)]
    public async Task<IActionResult> GetSummary(string slug, [FromQuery] int year, [FromQuery] int month) =>
        GetCurrentUserId() is int userId ? ToResponse(await service.GetSummaryAsync(slug, userId, year, month)) : Unauthorized();

    /// <summary>The monthly summary as payroll CSV (German format; <c>lang</c> = "en" for English headers).</summary>
    [HttpGet("summary/csv")]
    [ProducesResponseType(typeof(FileContentResult), StatusCodes.Status200OK)]
    public async Task<IActionResult> GetSummaryCsv(string slug, [FromQuery] int year, [FromQuery] int month, [FromQuery] string? lang = null)
    {
        if (GetCurrentUserId() is not int userId) return Unauthorized();
        var result = await service.GetSummaryCsvAsync(slug, userId, year, month, lang);
        return result.IsSuccess ? File(result.Data!, "text/csv; charset=utf-8", $"accounting-{slug}-{year:0000}-{month:00}.csv") : ToResponse(result);
    }

    /// <summary>The monthly summary as PDF.</summary>
    [HttpGet("summary/pdf")]
    [ProducesResponseType(typeof(FileContentResult), StatusCodes.Status200OK)]
    public async Task<IActionResult> GetSummaryPdf(string slug, [FromQuery] int year, [FromQuery] int month, [FromQuery] string? lang = null)
    {
        if (GetCurrentUserId() is not int userId) return Unauthorized();
        var result = await service.GetSummaryPdfAsync(slug, userId, year, month, lang);
        return result.IsSuccess ? File(result.Data!, "application/pdf", $"accounting-{slug}-{year:0000}-{month:00}.pdf") : ToResponse(result);
    }

    /// <summary>One member's day-by-day monthly PDF (also for former members; absence notes left out).</summary>
    [HttpGet("members/{memberId}/report")]
    [ProducesResponseType(typeof(FileContentResult), StatusCodes.Status200OK)]
    public async Task<IActionResult> GetMemberReport(string slug, int memberId, [FromQuery] int year, [FromQuery] int month, [FromQuery] string? lang = null)
    {
        if (GetCurrentUserId() is not int userId) return Unauthorized();
        var access = await service.AuthorizeMemberReportAsync(slug, userId);
        if (!access.IsSuccess) return ToResponse(access);
        var result = await reports.GenerateMonthlyReportAsync(slug, userId, memberId, year, month, lang, forAccounting: true);
        return result.IsSuccess
            ? File(result.Data!, "application/pdf", $"time-report-{slug}-member{memberId}-{year:0000}-{month:00}.pdf")
            : ToResponse(result);
    }

    /// <summary>The 12 months of a year: closed (by whom, when), ended, failure entries and pending requests in them.</summary>
    [HttpGet("months")]
    [ProducesResponseType(typeof(AccountingMonthsResponse), StatusCodes.Status200OK)]
    public async Task<IActionResult> GetMonths(string slug, [FromQuery] int year) =>
        GetCurrentUserId() is int userId ? ToResponse(await service.GetMonthsAsync(slug, userId, year)) : Unauthorized();

    /// <summary>Vacation entitlement, taken, planned and remaining days plus sick/other absence days of every member for a year.</summary>
    [HttpGet("vacation")]
    [ProducesResponseType(typeof(VacationOverviewResponse), StatusCodes.Status200OK)]
    public async Task<IActionResult> GetVacation(string slug, [FromQuery] int year) =>
        GetCurrentUserId() is int userId ? ToResponse(await service.GetVacationOverviewAsync(slug, userId, year)) : Unauthorized();
}
