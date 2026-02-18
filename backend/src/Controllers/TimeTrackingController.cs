using System.Security.Claims;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using TimeTracking.Api.Data;
using TimeTracking.Api.Models;
using TimeTracking.Api.Models.Dtos;

namespace TimeTracking.Api.Controllers;

[ApiController]
[Route("api/[controller]")]
[Authorize]
public class TimeTrackingController : ControllerBase
{
    private readonly TimeTrackingDbContext _context;

    public TimeTrackingController(TimeTrackingDbContext context)
    {
        _context = context;
    }

    private int? GetCurrentUserId()
    {
        var claim = User.FindFirst(ClaimTypes.NameIdentifier);
        return claim != null && int.TryParse(claim.Value, out var id) ? id : null;
    }

    private static TimeEntryResponse ToResponse(TimeEntry entry)
    {
        var duration = entry.EndTime.HasValue
            ? (entry.EndTime.Value - entry.StartTime).TotalMinutes
            : entry.IsRunning
                ? (DateTime.UtcNow - entry.StartTime).TotalMinutes
                : (double?)null;

        var netDuration = duration.HasValue
            ? Math.Max(0, duration.Value - entry.PauseDurationMinutes)
            : (double?)null;

        return new TimeEntryResponse
        {
            Id = entry.Id,
            UserId = entry.UserId,
            OrganizationId = entry.OrganizationId,
            OrganizationName = entry.Organization?.Name,
            Description = entry.Description,
            StartTime = entry.StartTime,
            EndTime = entry.EndTime,
            IsRunning = entry.IsRunning,
            DurationMinutes = duration.HasValue ? Math.Round(duration.Value, 2) : null,
            PauseDurationMinutes = entry.PauseDurationMinutes,
            NetDurationMinutes = netDuration.HasValue ? Math.Round(netDuration.Value, 2) : null,
            CreatedAt = entry.CreatedAt
        };
    }

    // ── POST /api/timetracking/start ──
    /// <summary>
    /// Start a new work session. Stops any currently running session first.
    /// </summary>
    [HttpPost("start")]
    [ProducesResponseType(typeof(TimeEntryResponse), StatusCodes.Status201Created)]
    public async Task<ActionResult<TimeEntryResponse>> Start([FromBody] StartTimeEntryRequest? request)
    {
        var userId = GetCurrentUserId();
        if (userId == null) return Unauthorized();

        // Stop any currently running entry
        var running = await _context.TimeEntries
            .FirstOrDefaultAsync(e => e.UserId == userId.Value && e.IsRunning);

        if (running != null)
        {
            running.EndTime = DateTime.UtcNow;
            running.IsRunning = false;
            running.UpdatedAt = DateTime.UtcNow;
        }

        // Validate organization if provided
        int? orgId = request?.OrganizationId;
        if (orgId == null && !string.IsNullOrWhiteSpace(request?.OrganizationSlug))
        {
            var orgBySlug = await _context.Organizations
                .FirstOrDefaultAsync(o => o.Slug == request.OrganizationSlug && o.IsActive);
            if (orgBySlug == null)
                return BadRequest(new { message = "Organization not found." });
            orgId = orgBySlug.Id;
        }
        if (orgId != null)
        {
            var orgExists = await _context.Organizations
                .AnyAsync(o => o.Id == orgId && o.IsActive);
            if (!orgExists)
                return BadRequest(new { message = "Organization not found." });
        }

        var entry = new TimeEntry
        {
            UserId = userId.Value,
            OrganizationId = orgId,
            Description = request?.Description,
            StartTime = DateTime.UtcNow,
            IsRunning = true,
            CreatedAt = DateTime.UtcNow,
            UpdatedAt = DateTime.UtcNow
        };

        _context.TimeEntries.Add(entry);
        await _context.SaveChangesAsync();

        // Reload with organization name
        await _context.Entry(entry).Reference(e => e.Organization).LoadAsync();

        return CreatedAtAction(nameof(GetCurrent), ToResponse(entry));
    }

    // ── POST /api/timetracking/stop ──
    /// <summary>
    /// Stop the current running work session.
    /// </summary>
    [HttpPost("stop")]
    [ProducesResponseType(typeof(TimeEntryResponse), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public async Task<ActionResult<TimeEntryResponse>> Stop([FromBody] StopTimeEntryRequest? request)
    {
        var userId = GetCurrentUserId();
        if (userId == null) return Unauthorized();

        var running = await _context.TimeEntries
            .Include(e => e.Organization)
            .FirstOrDefaultAsync(e => e.UserId == userId.Value && e.IsRunning);

        if (running == null)
            return NotFound(new { message = "No running time entry found." });

        running.EndTime = DateTime.UtcNow;
        running.IsRunning = false;
        running.UpdatedAt = DateTime.UtcNow;

        // Allow updating description on stop
        if (request?.Description != null)
            running.Description = request.Description;

        // Apply auto-pause rules if organization has them enabled
        await ApplyPauseRules(running);

        await _context.SaveChangesAsync();

        return Ok(ToResponse(running));
    }

    /// <summary>
    /// Calculate and apply pause deduction based on organization pause rules
    /// </summary>
    private async Task ApplyPauseRules(TimeEntry entry)
    {
        if (entry.OrganizationId == null || !entry.EndTime.HasValue) return;

        var org = await _context.Organizations
            .FirstOrDefaultAsync(o => o.Id == entry.OrganizationId && o.AutoPauseEnabled);
        if (org == null) return;

        var workHours = (entry.EndTime.Value - entry.StartTime).TotalHours;

        // Get the highest-threshold matching rule
        var rule = await _context.PauseRules
            .Where(pr => pr.OrganizationId == entry.OrganizationId && pr.MinHours <= workHours)
            .OrderByDescending(pr => pr.MinHours)
            .FirstOrDefaultAsync();

        if (rule != null)
        {
            entry.PauseDurationMinutes = rule.PauseMinutes;
        }
    }

    // ── GET /api/timetracking/current ──
    /// <summary>
    /// Get the currently running work session, if any.
    /// </summary>
    [HttpGet("current")]
    [ProducesResponseType(typeof(TimeEntryResponse), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public async Task<ActionResult<TimeEntryResponse>> GetCurrent()
    {
        var userId = GetCurrentUserId();
        if (userId == null) return Unauthorized();

        var running = await _context.TimeEntries
            .Include(e => e.Organization)
            .FirstOrDefaultAsync(e => e.UserId == userId.Value && e.IsRunning);

        if (running == null)
            return NotFound(new { message = "No running time entry." });

        return Ok(ToResponse(running));
    }

    // ── GET /api/timetracking ──
    /// <summary>
    /// Get time entry history for the current user.
    /// </summary>
    [HttpGet]
    [ProducesResponseType(typeof(IEnumerable<TimeEntryResponse>), StatusCodes.Status200OK)]
    public async Task<ActionResult<IEnumerable<TimeEntryResponse>>> GetHistory(
        [FromQuery] int? organizationId,
        [FromQuery] DateTime? from,
        [FromQuery] DateTime? to,
        [FromQuery] int limit = 50,
        [FromQuery] int offset = 0)
    {
        var userId = GetCurrentUserId();
        if (userId == null) return Unauthorized();

        var query = _context.TimeEntries
            .Include(e => e.Organization)
            .Where(e => e.UserId == userId.Value)
            .AsQueryable();

        if (organizationId.HasValue)
            query = query.Where(e => e.OrganizationId == organizationId.Value);

        if (from.HasValue)
            query = query.Where(e => e.StartTime >= from.Value);

        if (to.HasValue)
            query = query.Where(e => e.StartTime <= to.Value);

        var entries = await query
            .OrderByDescending(e => e.StartTime)
            .Skip(offset)
            .Take(Math.Min(limit, 10000))
            .ToListAsync();

        return Ok(entries.Select(ToResponse));
    }

    // ── PUT /api/timetracking/{id} ──
    /// <summary>
    /// Edit a past time entry. Respects EditPastEntriesMode if in an organization.
    /// </summary>
    [HttpPut("{id}")]
    [ProducesResponseType(typeof(TimeEntryResponse), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    [ProducesResponseType(StatusCodes.Status403Forbidden)]
    public async Task<ActionResult<TimeEntryResponse>> Update(int id, [FromBody] UpdateTimeEntryRequest request)
    {
        var userId = GetCurrentUserId();
        if (userId == null) return Unauthorized();

        var entry = await _context.TimeEntries
            .Include(e => e.Organization)
            .FirstOrDefaultAsync(e => e.Id == id && e.UserId == userId.Value);

        if (entry == null)
            return NotFound(new { message = "Time entry not found." });

        if (entry.IsRunning)
            return BadRequest(new { message = "Cannot edit a running time entry. Stop it first." });

        // Check if editing is allowed for org entries
        if (entry.OrganizationId.HasValue)
        {
            var org = await _context.Organizations
                .FirstOrDefaultAsync(o => o.Id == entry.OrganizationId && o.IsActive);
            if (org != null && org.EditPastEntriesMode == RuleMode.Disabled)
                return StatusCode(403, new { message = "Editing past entries is disabled in this organization." });
            if (org != null && org.EditPastEntriesMode == RuleMode.RequiresApproval)
                return StatusCode(403, new { message = "Editing past entries requires admin approval in this organization. Please submit a request." });
        }

        if (request.StartTime.HasValue)
            entry.StartTime = request.StartTime.Value;
        if (request.EndTime.HasValue)
            entry.EndTime = request.EndTime.Value;
        if (request.Description != null)
            entry.Description = request.Description;
        if (request.OrganizationId.HasValue)
            entry.OrganizationId = request.OrganizationId.Value == 0 ? null : request.OrganizationId.Value;

        // Validate times
        if (entry.EndTime.HasValue && entry.EndTime.Value <= entry.StartTime)
            return BadRequest(new { message = "End time must be after start time." });

        entry.UpdatedAt = DateTime.UtcNow;

        // Handle pause duration editing
        if (request.PauseDurationMinutes.HasValue)
        {
            // Check if org allows editing pause
            if (entry.OrganizationId.HasValue)
            {
                var pauseOrg = await _context.Organizations
                    .FirstOrDefaultAsync(o => o.Id == entry.OrganizationId && o.IsActive);
                if (pauseOrg != null && pauseOrg.EditPauseMode == RuleMode.Disabled)
                    return StatusCode(403, new { message = "Editing pause duration is disabled in this organization." });
                if (pauseOrg != null && pauseOrg.EditPauseMode == RuleMode.RequiresApproval)
                    return StatusCode(403, new { message = "Editing pause duration requires admin approval in this organization. Please submit a request." });
            }
            entry.PauseDurationMinutes = Math.Max(0, request.PauseDurationMinutes.Value);
        }
        else
        {
            // Re-apply pause rules for the updated entry (only if pause wasn't manually set)
            entry.PauseDurationMinutes = 0;
            await ApplyPauseRules(entry);
        }

        await _context.SaveChangesAsync();

        return Ok(ToResponse(entry));
    }

    // ── DELETE /api/timetracking/{id} ──
    /// <summary>
    /// Delete a time entry.
    /// </summary>
    [HttpDelete("{id}")]
    [ProducesResponseType(StatusCodes.Status204NoContent)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public async Task<IActionResult> Delete(int id)
    {
        var userId = GetCurrentUserId();
        if (userId == null) return Unauthorized();

        var entry = await _context.TimeEntries
            .FirstOrDefaultAsync(e => e.Id == id && e.UserId == userId.Value);

        if (entry == null)
            return NotFound(new { message = "Time entry not found." });

        _context.TimeEntries.Remove(entry);
        await _context.SaveChangesAsync();

        return NoContent();
    }
}
