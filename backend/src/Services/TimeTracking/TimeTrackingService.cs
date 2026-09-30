using Microsoft.EntityFrameworkCore;
using TimeTracking.Api.Data;
using TimeTracking.Api.Models;
using TimeTracking.Api.Models.Dtos;

namespace TimeTracking.Api.Services;

public class TimeTrackingService : ITimeTrackingService
{
    private readonly TimeTrackingDbContext _context;
    private readonly ILogger<TimeTrackingService> _logger;

    public TimeTrackingService(TimeTrackingDbContext context, ILogger<TimeTrackingService> logger)
    {
        _context = context;
        _logger = logger;
    }

    public async Task<ServiceResult<TimeEntryResponse>> StartAsync(int userId, StartTimeEntryRequest? request)
    {
        // Resolve organization
        int? orgId = request?.OrganizationId;
        if (orgId == null && !string.IsNullOrWhiteSpace(request?.OrganizationSlug))
        {
            var orgBySlug = await _context.Organizations
                .AsNoTracking()
                .FirstOrDefaultAsync(o => o.Slug == request.OrganizationSlug);
            if (orgBySlug == null)
                return ServiceResult.BadRequest<TimeEntryResponse>("Organization not found.");
            orgId = orgBySlug.Id;
        }
        if (orgId != null)
        {
            var orgExists = await _context.Organizations.AnyAsync(o => o.Id == orgId);
            if (!orgExists)
                return ServiceResult.BadRequest<TimeEntryResponse>("Organization not found.");

            // Verify the user is a member of the organization (and meets its 2FA requirement)
            var membership = await _context.UserOrganizations
                .Where(uo => uo.OrganizationId == orgId && uo.UserId == userId)
                .Select(uo => new { uo.Organization.Require2fa, uo.User.TwoFactorEnabled })
                .FirstOrDefaultAsync();
            if (membership == null)
                return ServiceResult.Forbidden<TimeEntryResponse>("You are not a member of this organization.");
            if (membership.Require2fa && !membership.TwoFactorEnabled)
                return ServiceResult.Forbidden<TimeEntryResponse>("This organization requires two-factor authentication. Enable it in your account settings to continue.");
        }

        // Stop a running entry first — exactly like an explicit stop, including pause rules —
        // and persist that before inserting, so the one-running-timer index is never violated
        var running = await _context.TimeEntries
            .Include(e => e.Organization)
            .FirstOrDefaultAsync(e => e.UserId == userId && e.IsRunning);
        if (running != null)
        {
            running.EndTime = DateTime.UtcNow;
            running.IsRunning = false;
            running.UpdatedAt = DateTime.UtcNow;
            await ApplyPauseRulesAsync(running);
            await _context.SaveChangesAsync();
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
        try
        {
            await _context.SaveChangesAsync();
        }
        catch (DbUpdateException ex) when (DbErrors.IsUniqueViolation(ex))
        {
            // A concurrent start (double click, RFID double-read) won the race
            _context.Entry(entry).State = EntityState.Detached;
            return ServiceResult.Conflict<TimeEntryResponse>("A timer is already running.");
        }
        _logger.LogInformation("Time entry {EntryId} started by user {UserId}", entry.Id, userId);

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
        _logger.LogInformation("Time entry {EntryId} stopped by user {UserId}", running.Id, userId);

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

    public async Task<ServiceResult<List<TimeEntryResponse>>> GetFailuresAsync(int userId)
    {
        var failures = await _context.TimeEntries
            .AsNoTracking()
            .Include(e => e.Organization)
            .Where(e => e.UserId == userId && e.IsFailure)
            .OrderByDescending(e => e.StartTime)
            .ToListAsync();

        return ServiceResult.Ok(failures.Select(MapToResponse).ToList());
    }

    public async Task<ServiceResult<PaginatedResponse<TimeEntryResponse>>> GetHistoryAsync(
        int userId, int? organizationId, DateTime? from, DateTime? to, int limit, int offset)
    {
        (limit, offset) = PaginationDefaults.Normalize(limit, offset);

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

        var totalCount = await query.CountAsync();

        var entries = await query
            .OrderByDescending(e => e.StartTime)
            .Skip(offset)
            .Take(limit)
            .ToListAsync();

        return ServiceResult.Ok(new PaginatedResponse<TimeEntryResponse>
        {
            Items = entries.Select(MapToResponse).ToList(),
            TotalCount = totalCount,
            Limit = limit,
            Offset = offset
        });
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
                .FirstOrDefaultAsync(o => o.Id == entry.OrganizationId);

            if (org != null)
            {
                var check = OrgRuleHelper.EnforceAllowed<TimeEntryResponse>(
                    await EffectiveRuleResolver.ResolveAsync(_context, org, userId, RequestType.EditPastEntry), "Editing past entries");
                if (check != null) return check;
            }
        }

        if (request.StartTime.HasValue)  entry.StartTime = request.StartTime.Value;
        if (request.EndTime.HasValue)
        {
            entry.EndTime = request.EndTime.Value;
            entry.IsFailure = false;   // the member supplied the real end time
        }
        if (request.Description != null) entry.Description = request.Description;
        if (request.OrganizationId.HasValue)
        {
            int? newOrganizationId = request.OrganizationId.Value == 0 ? null : request.OrganizationId.Value;

            if (newOrganizationId.HasValue)
            {
                var orgExists = await _context.Organizations
                    .AsNoTracking()
                    .AnyAsync(o => o.Id == newOrganizationId.Value);
                if (!orgExists)
                    return ServiceResult.BadRequest<TimeEntryResponse>("Organization not found.");

                var isMember = await _context.UserOrganizations
                    .AsNoTracking()
                    .AnyAsync(uo => uo.OrganizationId == newOrganizationId.Value && uo.UserId == userId);
                if (!isMember)
                    return ServiceResult.Forbidden<TimeEntryResponse>("You are not a member of this organization.");
            }

            entry.OrganizationId = newOrganizationId;
        }

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
                    .FirstOrDefaultAsync(o => o.Id == entry.OrganizationId);
                if (pauseOrg != null)
                {
                    var check = OrgRuleHelper.EnforceAllowed<TimeEntryResponse>(
                        await EffectiveRuleResolver.ResolveAsync(_context, pauseOrg, userId, RequestType.EditPause), "Editing pause duration");
                    if (check != null) return check;
                }
            }
            entry.PauseDurationMinutes = Math.Max(0, request.PauseDurationMinutes.Value);
        }
        else
        {
            entry.PauseDurationMinutes = 0;
            await ApplyPauseRulesAsync(entry);
        }

        await _context.SaveChangesAsync();
        _logger.LogInformation("Time entry {EntryId} updated by user {UserId}", entry.Id, userId);
        return ServiceResult.Ok(MapToResponse(entry));
    }

    public async Task<ServiceResult> DeleteAsync(int userId, int entryId)
    {
        var entry = await _context.TimeEntries
            .FirstOrDefaultAsync(e => e.Id == entryId && e.UserId == userId);

        if (entry == null)
            return ServiceResult.NotFound("Time entry not found.");

        // Deleting a past entry changes the record as much as editing it, so the same rule applies
        if (entry.OrganizationId.HasValue && !entry.IsRunning)
        {
            var org = await _context.Organizations.AsNoTracking().FirstOrDefaultAsync(o => o.Id == entry.OrganizationId);
            if (org != null)
            {
                var mode = await EffectiveRuleResolver.ResolveAsync(_context, org, userId, RequestType.EditPastEntry);
                if (mode != RuleMode.Allowed)
                    return ServiceResult.Forbidden(mode == RuleMode.Disabled
                        ? "Editing past entries is disabled in this organization."
                        : "Editing past entries requires admin approval in this organization. Please submit a request.");
            }
        }

        _context.TimeEntries.Remove(entry);
        await _context.SaveChangesAsync();
        _logger.LogInformation("Time entry {EntryId} deleted by user {UserId}", entry.Id, userId);

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
        // A failure entry's real duration is unknown until corrected, so it counts as nothing
        var duration = entry.IsFailure
            ? (double?)null
            : entry.EndTime.HasValue
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
            IsFailure = entry.IsFailure,
            CreatedAt = entry.CreatedAt
        };
    }
}
