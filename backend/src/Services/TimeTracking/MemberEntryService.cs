using Microsoft.EntityFrameworkCore;
using TimeTracking.Api.Data;
using TimeTracking.Api.Models;
using TimeTracking.Api.Models.Dtos;

namespace TimeTracking.Api.Services;

/// <summary>
/// Admin corrections: org Admins/Owners (and Group Admins whose group may approve time edits,
/// for the plain members of that group) create, correct and delete members' entries in the
/// organization — independent of the member's own "edit past entries" rule, so e.g. a failure
/// entry can be fixed where members may not edit. Correcting is an admin duty, so it does not
/// depend on <see cref="Organization.MemberTimeEntryVisibility"/> (which only governs browsing
/// members' hours); every change is recorded in the entry history with the admin as author.
/// Also serves the Working Time Act compliance list.
/// </summary>
public class MemberEntryService(TimeTrackingDbContext context, ILogger<MemberEntryService> logger)
{
    public async Task<ServiceResult<TimeEntryResponse>> CreateAsync(string slug, int callerUserId, int memberId, CreateMemberEntryRequest request)
    {
        var (org, denied) = await AuthorizeAsync(slug, callerUserId, memberId);
        if (denied != null) return ServiceResult.Fail<TimeEntryResponse>(denied.Value.Type, denied.Value.Message);

        if (EntryTimes.Validate(request.StartTime, request.EndTime) is { } timesError)
            return ServiceResult.BadRequest<TimeEntryResponse>(timesError);
        if (await EntryTimes.OverlapsAsync(context, memberId, request.StartTime, request.EndTime))
            return ServiceResult.Conflict<TimeEntryResponse>("The entry overlaps an existing entry of the member.");

        var entry = new TimeEntry
        {
            UserId = memberId,
            OrganizationId = org!.Id,
            StartTime = request.StartTime,
            EndTime = request.EndTime,
            Description = request.Description,
            Source = TimeEntrySource.Admin,
            CreatedAt = DateTime.UtcNow,
            UpdatedAt = DateTime.UtcNow
        };
        if (request.PauseDurationMinutes.HasValue)
        {
            entry.PauseDurationMinutes = request.PauseDurationMinutes.Value;
            entry.PauseIsManual = true;
        }
        await PauseRules.ApplyAsync(context, entry);

        context.TimeEntries.Add(entry);
        await context.SaveChangesAsync();
        logger.LogInformation("Time entry {EntryId} created for user {MemberId} in org {OrgId} by admin {CallerId}", entry.Id, memberId, org.Id, callerUserId);
        return ServiceResult.Ok(await RespondAsync(entry, org));
    }

    public async Task<ServiceResult<TimeEntryResponse>> UpdateAsync(string slug, int callerUserId, int memberId, int entryId, UpdateMemberEntryRequest request)
    {
        var (org, denied) = await AuthorizeAsync(slug, callerUserId, memberId);
        if (denied != null) return ServiceResult.Fail<TimeEntryResponse>(denied.Value.Type, denied.Value.Message);

        var entry = await FindAsync(org!, memberId, entryId);
        if (entry == null) return ServiceResult.NotFound<TimeEntryResponse>("Time entry not found.");
        if (entry.IsRunning) return ServiceResult.BadRequest<TimeEntryResponse>("Cannot edit a running time entry.");

        var timesChanged = (request.StartTime.HasValue && request.StartTime.Value != entry.StartTime)
                           || (request.EndTime.HasValue && request.EndTime.Value != entry.EndTime);
        if (request.StartTime.HasValue) entry.StartTime = request.StartTime.Value;
        if (request.EndTime.HasValue)
        {
            entry.EndTime = request.EndTime.Value;
            entry.IsFailure = false;   // the real end time is known now
        }
        if (request.Description != null) entry.Description = request.Description;

        if (EntryTimes.Validate(entry.StartTime, entry.EndTime) is { } timesError)
            return ServiceResult.BadRequest<TimeEntryResponse>(timesError);
        if (timesChanged && await EntryTimes.OverlapsAsync(context, memberId, entry.StartTime, entry.EndTime!.Value, entry.Id))
            return ServiceResult.Conflict<TimeEntryResponse>("The entry overlaps an existing entry of the member.");

        if (request.PauseDurationMinutes.HasValue)
        {
            entry.PauseDurationMinutes = request.PauseDurationMinutes.Value;
            entry.PauseIsManual = true;
        }
        if (timesChanged || request.PauseDurationMinutes.HasValue) await PauseRules.ApplyAsync(context, entry);
        entry.UpdatedAt = DateTime.UtcNow;

        await context.SaveChangesAsync();
        logger.LogInformation("Time entry {EntryId} of user {MemberId} corrected by admin {CallerId}", entry.Id, memberId, callerUserId);
        return ServiceResult.Ok(await RespondAsync(entry, org));
    }

    public async Task<ServiceResult> DeleteAsync(string slug, int callerUserId, int memberId, int entryId)
    {
        var (org, denied) = await AuthorizeAsync(slug, callerUserId, memberId);
        if (denied != null) return ServiceResult.Fail(denied.Value.Type, denied.Value.Message);

        var entry = await FindAsync(org!, memberId, entryId);
        if (entry == null) return ServiceResult.NotFound("Time entry not found.");
        if (entry.IsRunning) return ServiceResult.BadRequest("Cannot delete a running time entry.");

        context.TimeEntries.Remove(entry);
        await context.SaveChangesAsync();
        logger.LogInformation("Time entry {EntryId} of user {MemberId} deleted by admin {CallerId}", entryId, memberId, callerUserId);
        return ServiceResult.Ok();
    }

    /// <summary>Days in [from, to] (default: the current local month) that break the Working Time Act.</summary>
    public async Task<ServiceResult<List<ComplianceDayResponse>>> GetComplianceAsync(string slug, int callerUserId, int memberId, DateOnly? from, DateOnly? to)
    {
        var org = await context.FindOrgBySlugAsync(slug);
        if (org == null) return ServiceResult.NotFound<List<ComplianceDayResponse>>("Organization not found");
        if (await context.GetOrgRoleAsync(memberId, org.Id) == null)
            return ServiceResult.NotFound<List<ComplianceDayResponse>>("Member not found in this organization.");
        if (!await EffectiveRuleResolver.CanExportDataForMemberAsync(context, org.Id, callerUserId, memberId))
            return ServiceResult.Forbidden<List<ComplianceDayResponse>>("You do not have permission to see this member's working times.");

        var today = OrgClock.For(org).Today();
        var first = from ?? new DateOnly(today.Year, today.Month, 1);
        var last = to ?? first.AddMonths(1).AddDays(-1);
        if (EntryTimes.ValidateRange(first, last) is { } rangeError)
            return ServiceResult.BadRequest<List<ComplianceDayResponse>>(rangeError);

        var days = await WorkingTimeAct.DaysAsync(context, memberId, org.Id, first, last);
        return ServiceResult.Ok(days.Where(d => d.DailyMax || d.RestPeriod).ToList());
    }

    private async Task<TimeEntryResponse> RespondAsync(TimeEntry entry, Organization org)
    {
        entry.Organization = org;
        return await WorkingTimeAct.WithWarningsAsync(context, entry, TimeTrackingService.MapToResponse(entry));
    }

    private Task<TimeEntry?> FindAsync(Organization org, int memberId, int entryId) =>
        context.TimeEntries.FirstOrDefaultAsync(e => e.Id == entryId && e.UserId == memberId && e.OrganizationId == org.Id);

    /// <summary>Org Admin/Owner for any member; Group Admin (time-edit approval) for plain members of their group, not themselves.</summary>
    private async Task<(Organization? Org, (ServiceErrorType Type, string Message)? Error)> AuthorizeAsync(string slug, int callerUserId, int memberId)
    {
        var org = await context.FindOrgBySlugAsync(slug);
        if (org == null) return (null, (ServiceErrorType.NotFound, "Organization not found"));

        var callerRole = await context.GetOrgRoleAsync(callerUserId, org.Id);
        var memberRole = await context.GetOrgRoleAsync(memberId, org.Id);
        if (callerRole == null) return (org, (ServiceErrorType.Forbidden, "You are not a member of this organization."));
        if (memberRole == null) return (org, (ServiceErrorType.NotFound, "Member not found in this organization."));
        if (callerRole >= OrganizationRole.Admin) return (org, null);

        var isGroupAdmin = memberId != callerUserId && memberRole == OrganizationRole.Member
            && await context.GroupMembers.AsNoTracking().AnyAsync(gm =>
                gm.UserId == callerUserId && gm.Role == GroupRole.Admin
                && gm.Group.OrganizationId == org.Id && gm.Group.IsActive
                && gm.Group.GroupAdminCanApproveTimeEdits == true
                && gm.Group.Members.Any(m => m.UserId == memberId));
        return isGroupAdmin
            ? (org, null)
            : (org, (ServiceErrorType.Forbidden, "Only admins can change other members' time entries."));
    }
}
