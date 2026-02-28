using Microsoft.EntityFrameworkCore;
using TimeTracking.Api.Data;
using TimeTracking.Api.Models;
using TimeTracking.Api.Models.Dtos;

namespace TimeTracking.Api.Services;

public class TimeTrackingService : ITimeTrackingService
{
    private readonly TimeTrackingDbContext _context;

    public TimeTrackingService(TimeTrackingDbContext context)
    {
        _context = context;
    }

    public async Task<ServiceResult<TimeEntryResponse>> StartAsync(int userId, StartTimeEntryRequest? request)
    {
        // Stop any currently running entry
        var running = await _context.TimeEntries
            .FirstOrDefaultAsync(e => e.UserId == userId && e.IsRunning);

        if (running != null)
        {
            running.EndTime = DateTime.UtcNow;
            running.IsRunning = false;
            running.UpdatedAt = DateTime.UtcNow;
        }

        // Resolve organization
        int? orgId = request?.OrganizationId;
        if (orgId == null && !string.IsNullOrWhiteSpace(request?.OrganizationSlug))
        {
            var orgBySlug = await _context.Organizations
                .AsNoTracking()
                .FirstOrDefaultAsync(o => o.Slug == request.OrganizationSlug && o.IsActive);
            if (orgBySlug == null)
                return ServiceResult.BadRequest<TimeEntryResponse>("Organization not found.");
            orgId = orgBySlug.Id;
        }
        if (orgId != null)
        {
            var orgExists = await _context.Organizations.AnyAsync(o => o.Id == orgId && o.IsActive);
            if (!orgExists)
                return ServiceResult.BadRequest<TimeEntryResponse>("Organization not found.");
        }

        var entry = new TimeEntry
        {
            UserId = userId,
            OrganizationId = orgId,
            Description = request?.Description,
            StartTime = DateTime.UtcNow,
            IsRunning = true,
            CreatedAt = DateTime.UtcNow,
            UpdatedAt = DateTime.UtcNow
        };

        _context.TimeEntries.Add(entry);
        await _context.SaveChangesAsync();

        // Reload organization name for response
        await _context.Entry(entry).Reference(e => e.Organization).LoadAsync();

        return ServiceResult.Ok(MapToResponse(entry));
    }

    public async Task<ServiceResult<TimeEntryResponse>> StopAsync(int userId, StopTimeEntryRequest? request)
    {
        var running = await _context.TimeEntries
            .Include(e => e.Organization)
            .FirstOrDefaultAsync(e => e.UserId == userId && e.IsRunning);

        if (running == null)
            return ServiceResult.NotFound<TimeEntryResponse>("No running time entry found.");

        running.EndTime = DateTime.UtcNow;
        running.IsRunning = false;
        running.UpdatedAt = DateTime.UtcNow;

        if (request?.Description != null)
            running.Description = request.Description;

        await ApplyPauseRulesAsync(running);
        await _context.SaveChangesAsync();

        return ServiceResult.Ok(MapToResponse(running));
    }

    public async Task<ServiceResult<TimeEntryResponse>> GetCurrentAsync(int userId)
    {
        var running = await _context.TimeEntries
            .AsNoTracking()
            .Include(e => e.Organization)
            .FirstOrDefaultAsync(e => e.UserId == userId && e.IsRunning);

        if (running == null)
            return ServiceResult.NotFound<TimeEntryResponse>("No running time entry.");

        return ServiceResult.Ok(MapToResponse(running));
    }

    public async Task<ServiceResult<List<TimeEntryResponse>>> GetHistoryAsync(
        int userId, int? organizationId, DateTime? from, DateTime? to, int limit, int offset)
    {
        var query = _context.TimeEntries
            .AsNoTracking()
            .Include(e => e.Organization)
            .Where(e => e.UserId == userId)
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

        return ServiceResult.Ok(entries.Select(MapToResponse).ToList());
    }

    public async Task<ServiceResult<TimeEntryResponse>> UpdateAsync(
        int userId, int entryId, UpdateTimeEntryRequest request)
    {
        var entry = await _context.TimeEntries
            .Include(e => e.Organization)
            .FirstOrDefaultAsync(e => e.Id == entryId && e.UserId == userId);

        if (entry == null)
            return ServiceResult.NotFound<TimeEntryResponse>("Time entry not found.");

        if (entry.IsRunning)
            return ServiceResult.BadRequest<TimeEntryResponse>("Cannot edit a running time entry. Stop it first.");

        // Check org-level editing rules
        if (entry.OrganizationId.HasValue)
        {
            var org = await _context.Organizations
                .AsNoTracking()
                .FirstOrDefaultAsync(o => o.Id == entry.OrganizationId && o.IsActive);

            if (org != null && org.EditPastEntriesMode == RuleMode.Disabled)
                return ServiceResult.Forbidden<TimeEntryResponse>("Editing past entries is disabled in this organization.");
            if (org != null && org.EditPastEntriesMode == RuleMode.RequiresApproval)
                return ServiceResult.Forbidden<TimeEntryResponse>("Editing past entries requires admin approval in this organization. Please submit a request.");
        }

        if (request.StartTime.HasValue)  entry.StartTime = request.StartTime.Value;
        if (request.EndTime.HasValue)    entry.EndTime = request.EndTime.Value;
        if (request.Description != null) entry.Description = request.Description;
        if (request.OrganizationId.HasValue)
            entry.OrganizationId = request.OrganizationId.Value == 0 ? null : request.OrganizationId.Value;

        if (entry.EndTime.HasValue && entry.EndTime.Value <= entry.StartTime)
            return ServiceResult.BadRequest<TimeEntryResponse>("End time must be after start time.");

        entry.UpdatedAt = DateTime.UtcNow;

        // Handle pause duration editing
        if (request.PauseDurationMinutes.HasValue)
        {
            if (entry.OrganizationId.HasValue)
            {
                var pauseOrg = await _context.Organizations
                    .AsNoTracking()
                    .FirstOrDefaultAsync(o => o.Id == entry.OrganizationId && o.IsActive);
                if (pauseOrg != null && pauseOrg.EditPauseMode == RuleMode.Disabled)
                    return ServiceResult.Forbidden<TimeEntryResponse>("Editing pause duration is disabled in this organization.");
                if (pauseOrg != null && pauseOrg.EditPauseMode == RuleMode.RequiresApproval)
                    return ServiceResult.Forbidden<TimeEntryResponse>("Editing pause duration requires admin approval in this organization. Please submit a request.");
            }
            entry.PauseDurationMinutes = Math.Max(0, request.PauseDurationMinutes.Value);
        }
        else
        {
            entry.PauseDurationMinutes = 0;
            await ApplyPauseRulesAsync(entry);
        }

        await _context.SaveChangesAsync();
        return ServiceResult.Ok(MapToResponse(entry));
    }

    public async Task<ServiceResult> DeleteAsync(int userId, int entryId)
    {
        var entry = await _context.TimeEntries
            .FirstOrDefaultAsync(e => e.Id == entryId && e.UserId == userId);

        if (entry == null)
            return ServiceResult.NotFound("Time entry not found.");

        _context.TimeEntries.Remove(entry);
        await _context.SaveChangesAsync();

        return ServiceResult.Ok();
    }

    // ────────────────────────────────────────────────────
    //  Private helpers
    // ────────────────────────────────────────────────────

    private async Task ApplyPauseRulesAsync(TimeEntry entry)
    {
        if (entry.OrganizationId == null || !entry.EndTime.HasValue) return;

        var org = await _context.Organizations
            .AsNoTracking()
            .FirstOrDefaultAsync(o => o.Id == entry.OrganizationId && o.AutoPauseEnabled);
        if (org == null) return;

        var workHours = (entry.EndTime.Value - entry.StartTime).TotalHours;

        var rule = await _context.PauseRules
            .AsNoTracking()
            .Where(pr => pr.OrganizationId == entry.OrganizationId && pr.MinHours <= workHours)
            .OrderByDescending(pr => pr.MinHours)
            .FirstOrDefaultAsync();

        if (rule != null)
        {
            entry.PauseDurationMinutes = rule.PauseMinutes;
        }
    }

    private static TimeEntryResponse MapToResponse(TimeEntry entry)
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
}
