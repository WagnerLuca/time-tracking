using Asp.Versioning;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.RateLimiting;
using TimeTracking.Api.Models.Dtos;
using TimeTracking.Api.Services;

namespace TimeTracking.Api.Controllers;

/// <summary>
/// Handles CSV export and import of time entries.
/// </summary>
[ApiController]
[ApiVersion("1.0")]
[Route("api/v{version:apiVersion}/[controller]")]
[Authorize]
[EnableRateLimiting("General")]
public class ExportImportController : OrganizationBaseController
{
    private readonly ITimeEntryExportService _exportService;
    private readonly PayrollExportService _payroll;

    public ExportImportController(ITimeEntryExportService exportService, PayrollExportService payroll)
    {
        _exportService = exportService;
        _payroll = payroll;
    }

    /// <summary>
    /// Payroll CSV for all active members of the organization over [from, to] (at most 731 days, from 2000):
    /// target, worked and overtime hours, overtime balance, vacation/sick/other absence days and entries needing
    /// correction. German format (<c>;</c>, decimal comma, UTF-8 BOM); <c>lang</c> = "de" (default) or "en" for the headers.
    /// Org admins only, while the organization lets admins see members' hours.
    /// </summary>
    [HttpPost("payroll")]
    [ProducesResponseType(typeof(FileContentResult), StatusCodes.Status200OK)]
    public async Task<IActionResult> ExportPayroll([FromBody] PayrollExportRequest request)
    {
        var userId = GetCurrentUserId();
        if (userId == null) return Unauthorized();
        var result = await _payroll.ExportAsync(userId.Value, request);
        if (!result.IsSuccess) return ToResponse(result);
        return File(result.Data!, "text/csv; charset=utf-8", $"payroll-{request.OrgSlug}-{request.From:yyyy-MM-dd}-{request.To:yyyy-MM-dd}.csv");
    }

    /// <summary>Export time entries or daily report as CSV file.</summary>
    [HttpPost("export")]
    [ProducesResponseType(typeof(FileContentResult), StatusCodes.Status200OK)]
    public async Task<IActionResult> ExportCsv([FromBody] ExportRequest request)
    {
        var userId = GetCurrentUserId();
        if (userId == null) return Unauthorized();

        ServiceResult<byte[]> result;
        string fileName;

        if (request.Type?.Equals("daily", StringComparison.OrdinalIgnoreCase) == true)
        {
            result = await _exportService.ExportDailyReportCsvAsync(userId.Value, request);
            fileName = $"daily-report-{request.OrganizationSlug ?? "all"}-{DateTime.UtcNow:yyyy-MM-dd}.csv";
        }
        else
        {
            result = await _exportService.ExportEntriesCsvAsync(userId.Value, request);
            fileName = $"time-entries-{DateTime.UtcNow:yyyy-MM-dd}.csv";
        }

        if (!result.IsSuccess)
            return ToResponse(result);

        return File(result.Data!, "text/csv", fileName);
    }

    /// <summary>Preview a CSV file for import (parse and check for duplicates).</summary>
    [HttpPost("import/preview")]
    [ProducesResponseType(typeof(ImportPreviewResponse), StatusCodes.Status200OK)]
    [RequestSizeLimit(5_242_880)] // 5 MB
    public async Task<IActionResult> PreviewImport(
        [FromQuery] string orgSlug,
        IFormFile file)
    {
        var userId = GetCurrentUserId();
        if (userId == null) return Unauthorized();

        if (file == null || file.Length == 0)
            return BadRequest("No file uploaded.");

        using var ms = new MemoryStream();
        await file.CopyToAsync(ms);
        var result = await _exportService.PreviewImportAsync(userId.Value, orgSlug, ms.ToArray());
        return ToResponse(result);
    }

    /// <summary>Confirm and execute a CSV import, creating time entries.</summary>
    [HttpPost("import/confirm")]
    [ProducesResponseType(typeof(ImportResultResponse), StatusCodes.Status200OK)]
    public async Task<IActionResult> ConfirmImport(
        [FromQuery] string orgSlug,
        [FromBody] List<ImportEntryRequest> entries)
    {
        var userId = GetCurrentUserId();
        if (userId == null) return Unauthorized();

        if (entries == null || entries.Count == 0)
            return BadRequest("No entries to import.");

        var result = await _exportService.ConfirmImportAsync(userId.Value, orgSlug, entries);
        return ToResponse(result);
    }
}
