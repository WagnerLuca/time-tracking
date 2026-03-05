using Asp.Versioning;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using TimeTracking.Api.Models.Dtos;
using TimeTracking.Api.Services;

namespace TimeTracking.Api.Controllers;

/// <summary>
/// Manages personal time entries: start, stop, history, update and delete.
/// </summary>
[ApiController]
[ApiVersion("1.0")]
[Route("api/v{version:apiVersion}/[controller]")]
[Authorize]
public class TimeTrackingController : OrganizationBaseController
{
    private readonly ITimeTrackingService _service;
    private readonly ITimeEntryExportService _exportService;
    private readonly ILogger<TimeTrackingController> _logger;

    public TimeTrackingController(ITimeTrackingService service, ITimeEntryExportService exportService, ILogger<TimeTrackingController> logger)
    {
        _service = service;
        _exportService = exportService;
        _logger = logger;
    }

    /// <summary>Start a new time entry (clock in).</summary>
    [HttpPost("start")]
    [ProducesResponseType(typeof(TimeEntryResponse), StatusCodes.Status201Created)]
    public async Task<IActionResult> Start([FromBody] StartTimeEntryRequest? request)
    {
        var userId = GetCurrentUserId();
        if (userId == null) return Unauthorized();
        return ToCreatedResponse(await _service.StartAsync(userId.Value, request));
    }

    /// <summary>Stop the currently running time entry (clock out).</summary>
    [HttpPost("stop")]
    [ProducesResponseType(typeof(TimeEntryResponse), StatusCodes.Status200OK)]
    public async Task<IActionResult> Stop([FromBody] StopTimeEntryRequest? request)
    {
        var userId = GetCurrentUserId();
        if (userId == null) return Unauthorized();
        return ToResponse(await _service.StopAsync(userId.Value, request));
    }

    /// <summary>Get the currently running time entry, if any.</summary>
    [HttpGet("current")]
    [ProducesResponseType(typeof(TimeEntryResponse), StatusCodes.Status200OK)]
    public async Task<IActionResult> GetCurrent()
    {
        var userId = GetCurrentUserId();
        if (userId == null) return Unauthorized();
        return ToResponse(await _service.GetCurrentAsync(userId.Value));
    }

    /// <summary>Get past time entries with optional filters and pagination.</summary>
    /// <param name="organizationId">Filter by organization ID.</param>
    /// <param name="from">Start of date range.</param>
    /// <param name="to">End of date range.</param>
    /// <param name="limit">Max items per page (default 50, max 200).</param>
    /// <param name="offset">Number of items to skip.</param>
    [HttpGet]
    [ProducesResponseType(typeof(PaginatedResponse<TimeEntryResponse>), StatusCodes.Status200OK)]
    public async Task<IActionResult> GetHistory(
        [FromQuery] int? organizationId,
        [FromQuery] DateTime? from,
        [FromQuery] DateTime? to,
        [FromQuery] int limit = 50,
        [FromQuery] int offset = 0)
    {
        var userId = GetCurrentUserId();
        if (userId == null) return Unauthorized();
        return ToResponse(await _service.GetHistoryAsync(userId.Value, organizationId, from, to, limit, offset));
    }

    /// <summary>Update an existing time entry.</summary>
    /// <param name="id">Time entry ID.</param>
    [HttpPut("{id}")]
    [ProducesResponseType(typeof(TimeEntryResponse), StatusCodes.Status200OK)]
    public async Task<IActionResult> Update(int id, [FromBody] UpdateTimeEntryRequest request)
    {
        var userId = GetCurrentUserId();
        if (userId == null) return Unauthorized();
        return ToResponse(await _service.UpdateAsync(userId.Value, id, request));
    }

    /// <summary>Delete a time entry.</summary>
    /// <param name="id">Time entry ID.</param>
    [HttpDelete("{id}")]
    [ProducesResponseType(StatusCodes.Status204NoContent)]
    public async Task<IActionResult> Delete(int id)
    {
        var userId = GetCurrentUserId();
        if (userId == null) return Unauthorized();
        return ToResponse(await _service.DeleteAsync(userId.Value, id));
    }

    /// <summary>Export time entries as CSV file.</summary>
    /// <param name="organizationId">Filter by organization ID.</param>
    /// <param name="from">Start of date range.</param>
    /// <param name="to">End of date range.</param>
    [HttpGet("export")]
    [ProducesResponseType(typeof(FileContentResult), StatusCodes.Status200OK)]
    public async Task<IActionResult> ExportCsv(
        [FromQuery] int? organizationId,
        [FromQuery] DateTime? from,
        [FromQuery] DateTime? to)
    {
        var userId = GetCurrentUserId();
        if (userId == null) return Unauthorized();

        var result = await _exportService.ExportCsvAsync(userId.Value, organizationId, from, to);
        if (!result.IsSuccess)
            return ToResponse(result);

        var fileName = $"time-entries-{DateTime.UtcNow:yyyy-MM-dd}.csv";
        return File(result.Data!, "text/csv", fileName);
    }

    /// <summary>Export a comprehensive daily report for an organization as CSV.</summary>
    /// <remarks>
    /// Generates a day-by-day summary including target hours from work schedules,
    /// actual worked hours, overtime tracking (cumulative from initial overtime),
    /// holidays, absences (sick/vacation), and individual time entry details.
    /// Defaults to the current month if no date range is specified.
    /// </remarks>
    /// <param name="orgSlug">Organization slug.</param>
    /// <param name="from">Start of date range (default: first day of current month).</param>
    /// <param name="to">End of date range (default: last day of current month).</param>
    [HttpGet("export/{orgSlug}/daily-report")]
    [ProducesResponseType(typeof(FileContentResult), StatusCodes.Status200OK)]
    public async Task<IActionResult> ExportDailyReport(
        string orgSlug,
        [FromQuery] DateTime? from,
        [FromQuery] DateTime? to)
    {
        var userId = GetCurrentUserId();
        if (userId == null) return Unauthorized();

        var result = await _exportService.ExportDailyReportCsvAsync(userId.Value, orgSlug, from, to);
        if (!result.IsSuccess)
            return ToResponse(result);

        var fileName = $"daily-report-{orgSlug}-{DateTime.UtcNow:yyyy-MM-dd}.csv";
        return File(result.Data!, "text/csv", fileName);
    }
}
