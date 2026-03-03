using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using TimeTracking.Api.Data;
using TimeTracking.Api.Models;
using TimeTracking.Api.Models.Dtos;

namespace TimeTracking.Api.Services;

public class RequestService : IRequestService
{
    private readonly TimeTrackingDbContext _context;

    public RequestService(TimeTrackingDbContext context)
    {
        _context = context;
    }

    public async Task<ServiceResult<OrgRequestResponse>> CreateRequestAsync(
        string slug, int userId, CreateOrgRequestRequest request)
    {
        var org = await GetOrgBySlugAsync(slug);
        if (org == null)
            return ServiceResult.NotFound<OrgRequestResponse>("Organization not found");

        var user = await _context.Users.FindAsync(userId);
        if (user == null)
            return ServiceResult.Fail<OrgRequestResponse>(ServiceErrorType.Unauthorized, "Unauthorized");

        // ── Join request logic ──
        if (request.Type == RequestType.JoinOrganization)
        {
            var isMember = await _context.UserOrganizations
                .AnyAsync(uo => uo.OrganizationId == org.Id && uo.UserId == userId);
            if (isMember)
                return ServiceResult.BadRequest<OrgRequestResponse>("You are already a member of this organization.");

            if (org.JoinPolicy == RuleMode.Disabled)
                return ServiceResult.BadRequest<OrgRequestResponse>("This organization does not accept join requests. Ask an admin to add you.");

            if (org.JoinPolicy == RuleMode.Allowed)
            {
                // Auto-join
                var autoRequest = new OrgRequest
                {
                    UserId = userId,
                    OrganizationId = org.Id,
                    Type = RequestType.JoinOrganization,
                    Message = request.Message,
                    Status = RequestStatus.Accepted,
                    RespondedAt = DateTime.UtcNow
                };
                _context.OrgRequests.Add(autoRequest);
                _context.UserOrganizations.Add(new UserOrganization
                {
                    UserId = userId,
                    OrganizationId = org.Id,
                    Role = OrganizationRole.Member,
                    IsActive = true,
                    JoinedAt = DateTime.UtcNow
                });
                await _context.SaveChangesAsync();
                autoRequest.User = user;
                return ServiceResult.Ok(MapToResponse(autoRequest, org.Name, org.Slug));
            }
            // RequiresApproval → fall through to pending
        }
        else
        {
            // Non-join requests require membership
            var isMember = await _context.UserOrganizations
                .AnyAsync(uo => uo.OrganizationId == org.Id && uo.UserId == userId);
            if (!isMember)
                return ServiceResult.BadRequest<OrgRequestResponse>("You must be a member of this organization.");

            var ruleMode = request.Type switch
            {
                RequestType.EditPastEntry => org.EditPastEntriesMode,
                RequestType.EditPause => org.EditPauseMode,
                RequestType.SetInitialOvertime => org.InitialOvertimeMode,
                RequestType.WorkScheduleChange => org.WorkScheduleChangeMode,
                _ => RuleMode.Allowed
            };

            if (ruleMode == RuleMode.Disabled)
                return ServiceResult.BadRequest<OrgRequestResponse>("This feature is disabled for this organization.");
            if (ruleMode == RuleMode.Allowed)
                return ServiceResult.BadRequest<OrgRequestResponse>("You can perform this action directly without a request.");
        }

        // Check for existing pending request of same type
        var pendingExists = await _context.OrgRequests
            .AnyAsync(r => r.OrganizationId == org.Id
                        && r.UserId == userId
                        && r.Type == request.Type
                        && r.Status == RequestStatus.Pending);
        if (pendingExists)
            return ServiceResult.Conflict<OrgRequestResponse>("You already have a pending request of this type for this organization.");

        var orgRequest = new OrgRequest
        {
            UserId = userId,
            OrganizationId = org.Id,
            Type = request.Type,
            Message = request.Message,
            RelatedEntityId = request.RelatedEntityId,
            RequestData = request.RequestData,
            Status = RequestStatus.Pending
        };

        _context.OrgRequests.Add(orgRequest);
        await _context.SaveChangesAsync();

        orgRequest.User = user;
        return ServiceResult.Ok(MapToResponse(orgRequest, org.Name, org.Slug));
    }

    public async Task<ServiceResult<PaginatedResponse<OrgRequestResponse>>> GetRequestsAsync(
        string slug, int callerUserId, RequestType? type, RequestStatus? status, int limit, int offset)
    {
        var org = await GetOrgBySlugAsync(slug);
        if (org == null)
            return ServiceResult.NotFound<PaginatedResponse<OrgRequestResponse>>("Organization not found");

        var callerRole = await GetRoleAsync(callerUserId, org.Id);
        if (callerRole == null || callerRole < OrganizationRole.Admin)
            return ServiceResult.Forbidden<PaginatedResponse<OrgRequestResponse>>();

        (limit, offset) = PaginationDefaults.Normalize(limit, offset);

        var query = _context.OrgRequests
            .AsNoTracking()
            .Include(r => r.User)
            .Include(r => r.RespondedByUser)
            .Where(r => r.OrganizationId == org.Id);

        if (type.HasValue)   query = query.Where(r => r.Type == type.Value);
        if (status.HasValue) query = query.Where(r => r.Status == status.Value);

        var totalCount = await query.CountAsync();

        var requests = await query
            .OrderByDescending(r => r.CreatedAt)
            .Skip(offset)
            .Take(limit)
            .ToListAsync();

        return ServiceResult.Ok(new PaginatedResponse<OrgRequestResponse>
        {
            Items = requests.Select(r => MapToResponse(r, org.Name, org.Slug)).ToList(),
            TotalCount = totalCount,
            Limit = limit,
            Offset = offset
        });
    }

    public async Task<ServiceResult<OrgRequestResponse>> RespondToRequestAsync(
        string slug, int callerUserId, int requestId, RespondToOrgRequestRequest request)
    {
        var org = await GetOrgBySlugAsync(slug);
        if (org == null)
            return ServiceResult.NotFound<OrgRequestResponse>("Organization not found");

        var callerRole = await GetRoleAsync(callerUserId, org.Id);
        if (callerRole == null || callerRole < OrganizationRole.Admin)
            return ServiceResult.Forbidden<OrgRequestResponse>();

        var orgRequest = await _context.OrgRequests
            .Include(r => r.User)
            .FirstOrDefaultAsync(r => r.Id == requestId && r.OrganizationId == org.Id);

        if (orgRequest == null)
            return ServiceResult.NotFound<OrgRequestResponse>("Request not found.");

        if (orgRequest.Status != RequestStatus.Pending)
            return ServiceResult.BadRequest<OrgRequestResponse>("This request has already been responded to.");

        orgRequest.Status = request.Accept ? RequestStatus.Accepted : RequestStatus.Declined;
        orgRequest.RespondedAt = DateTime.UtcNow;
        orgRequest.RespondedByUserId = callerUserId;

        // Apply side effects on acceptance
        if (request.Accept)
        {
            await ApplyAcceptanceSideEffectsAsync(orgRequest, org);
        }

        await _context.SaveChangesAsync();

        var responder = await _context.Users.FindAsync(callerUserId);
        return ServiceResult.Ok(MapToResponse(orgRequest, org.Name, org.Slug,
            responder != null ? $"{responder.FirstName} {responder.LastName}" : null));
    }

    public async Task<ServiceResult<PaginatedResponse<OrgRequestResponse>>> GetMyRequestsAsync(int userId, RequestType? type, int limit, int offset)
    {
        (limit, offset) = PaginationDefaults.Normalize(limit, offset);

        var query = _context.OrgRequests
            .AsNoTracking()
            .Include(r => r.Organization)
            .Include(r => r.RespondedByUser)
            .Where(r => r.UserId == userId);

        if (type.HasValue)
            query = query.Where(r => r.Type == type.Value);

        var totalCount = await query.CountAsync();

        var requests = await query
            .OrderByDescending(r => r.CreatedAt)
            .Skip(offset)
            .Take(limit)
            .ToListAsync();

        var user = await _context.Users.AsNoTracking().FirstOrDefaultAsync(u => u.Id == userId);
        if (user == null)
            return ServiceResult.Fail<PaginatedResponse<OrgRequestResponse>>(ServiceErrorType.Unauthorized, "Unauthorized");

        var items = requests.Select(r =>
        {
            r.User = user;
            return MapToResponse(r, r.Organization.Name, r.Organization.Slug);
        }).ToList();

        return ServiceResult.Ok(new PaginatedResponse<OrgRequestResponse>
        {
            Items = items,
            TotalCount = totalCount,
            Limit = limit,
            Offset = offset
        });
    }

    public async Task<ServiceResult<AdminNotificationResponse>> GetAdminNotificationsAsync(int userId)
    {
        var adminOrgIds = await _context.UserOrganizations
            .Where(uo => uo.UserId == userId && uo.Role >= OrganizationRole.Admin)
            .Select(uo => uo.OrganizationId)
            .ToListAsync();

        if (adminOrgIds.Count == 0)
            return ServiceResult.Ok(new AdminNotificationResponse
            {
                PendingRequests = 0,
                Requests = new List<OrgRequestResponse>()
            });

        var pendingRequests = await _context.OrgRequests
            .AsNoTracking()
            .Include(r => r.User)
            .Include(r => r.Organization)
            .Where(r => adminOrgIds.Contains(r.OrganizationId) && r.Status == RequestStatus.Pending)
            .OrderByDescending(r => r.CreatedAt)
            .ToListAsync();

        return ServiceResult.Ok(new AdminNotificationResponse
        {
            PendingRequests = pendingRequests.Count,
            Requests = pendingRequests.Select(r =>
                MapToResponse(r, r.Organization.Name, r.Organization.Slug)).ToList()
        });
    }

    public async Task<ServiceResult<UserNotificationResponse>> GetUserNotificationsAsync(int userId)
    {
        var unseenResponses = await _context.OrgRequests
            .AsNoTracking()
            .Include(r => r.User)
            .Include(r => r.Organization)
            .Include(r => r.RespondedByUser)
            .Where(r => r.UserId == userId
                     && r.Status != RequestStatus.Pending
                     && r.UserNotifiedAt == null)
            .OrderByDescending(r => r.RespondedAt)
            .ToListAsync();

        return ServiceResult.Ok(new UserNotificationResponse
        {
            Count = unseenResponses.Count,
            Requests = unseenResponses.Select(r =>
                MapToResponse(r, r.Organization.Name, r.Organization.Slug)).ToList()
        });
    }

    public async Task<ServiceResult<object>> MarkNotificationsSeenAsync(int userId, List<int>? requestIds)
    {
        var query = _context.OrgRequests
            .Where(r => r.UserId == userId
                     && r.Status != RequestStatus.Pending
                     && r.UserNotifiedAt == null);

        if (requestIds != null && requestIds.Count > 0)
            query = query.Where(r => requestIds.Contains(r.Id));

        var requests = await query.ToListAsync();
        foreach (var req in requests)
            req.UserNotifiedAt = DateTime.UtcNow;

        await _context.SaveChangesAsync();
        return ServiceResult.Ok<object>(new { marked = requests.Count });
    }

    // ────────────────────────────────────────────────────
    //  Private helpers
    // ────────────────────────────────────────────────────

    private async Task ApplyAcceptanceSideEffectsAsync(OrgRequest orgRequest, Organization org)
    {
        switch (orgRequest.Type)
        {
            case RequestType.JoinOrganization:
                _context.UserOrganizations.Add(new UserOrganization
                {
                    UserId = orgRequest.UserId,
                    OrganizationId = org.Id,
                    Role = OrganizationRole.Member,
                    IsActive = true,
                    JoinedAt = DateTime.UtcNow
                });
                break;

            case RequestType.SetInitialOvertime:
                if (orgRequest.RequestData != null
                    && double.TryParse(orgRequest.RequestData,
                        System.Globalization.NumberStyles.Any,
                        System.Globalization.CultureInfo.InvariantCulture, out var hours))
                {
                    var membership = await _context.UserOrganizations
                        .FirstOrDefaultAsync(uo => uo.OrganizationId == org.Id
                                                && uo.UserId == orgRequest.UserId);
                    if (membership != null)
                        membership.InitialOvertimeHours = hours;
                }
                break;

            case RequestType.EditPastEntry:
                if (orgRequest.RelatedEntityId.HasValue && orgRequest.RequestData != null)
                {
                    var entry = await _context.TimeEntries
                        .FirstOrDefaultAsync(e => e.Id == orgRequest.RelatedEntityId.Value
                                               && e.OrganizationId == org.Id
                                               && e.UserId == orgRequest.UserId);
                    if (entry != null)
                    {
                        try
                        {
                            var data = JsonSerializer.Deserialize<EditEntryRequestData>(
                                orgRequest.RequestData,
                                new JsonSerializerOptions { PropertyNameCaseInsensitive = true });
                            if (data != null)
                            {
                                if (data.StartTime.HasValue) entry.StartTime = data.StartTime.Value;
                                if (data.EndTime.HasValue)   entry.EndTime = data.EndTime.Value;
                                if (data.Description != null) entry.Description = data.Description;
                                entry.UpdatedAt = DateTime.UtcNow;
                            }
                        }
                        catch { /* invalid JSON, skip */ }
                    }
                }
                break;

            case RequestType.EditPause:
                if (orgRequest.RelatedEntityId.HasValue && orgRequest.RequestData != null)
                {
                    var pauseEntry = await _context.TimeEntries
                        .FirstOrDefaultAsync(e => e.Id == orgRequest.RelatedEntityId.Value
                                               && e.OrganizationId == org.Id
                                               && e.UserId == orgRequest.UserId);
                    if (pauseEntry != null && int.TryParse(orgRequest.RequestData, out var pauseMinutes))
                    {
                        pauseEntry.PauseDurationMinutes = Math.Max(0, pauseMinutes);
                        pauseEntry.UpdatedAt = DateTime.UtcNow;
                    }
                }
                break;

            case RequestType.WorkScheduleChange:
                if (orgRequest.RequestData != null)
                {
                    try
                    {
                        var scheduleData = JsonSerializer.Deserialize<WorkScheduleChangeRequestData>(
                            orgRequest.RequestData,
                            new JsonSerializerOptions { PropertyNameCaseInsensitive = true });
                        if (scheduleData != null)
                        {
                            await ApplyWorkScheduleChangeAsync(orgRequest.UserId, org.Id, scheduleData);
                        }
                    }
                    catch { /* invalid JSON — request is accepted but schedule unchanged */ }
                }
                break;
        }
    }

    private async Task ApplyWorkScheduleChangeAsync(
        int userId, int orgId, WorkScheduleChangeRequestData data)
    {
        var schedule = new WorkSchedule
        {
            UserId = userId,
            OrganizationId = orgId,
            ValidFrom = data.ValidFrom,
            ValidTo = data.ValidTo,
            WeeklyWorkHours = data.WeeklyWorkHours
        };

        if (data.WeeklyWorkHours.HasValue && data.DistributeEvenly)
        {
            var daily = Math.Round(data.WeeklyWorkHours.Value / 5.0, 2);
            schedule.TargetMon = daily;
            schedule.TargetTue = daily;
            schedule.TargetWed = daily;
            schedule.TargetThu = daily;
            schedule.TargetFri = daily;
        }
        else
        {
            schedule.TargetMon = data.TargetMon ?? 0;
            schedule.TargetTue = data.TargetTue ?? 0;
            schedule.TargetWed = data.TargetWed ?? 0;
            schedule.TargetThu = data.TargetThu ?? 0;
            schedule.TargetFri = data.TargetFri ?? 0;
        }

        // Close any open-ended predecessor schedule
        var openSchedule = await _context.WorkSchedules
            .Where(s => s.UserId == userId && s.OrganizationId == orgId
                     && s.ValidTo == null && s.ValidFrom < data.ValidFrom)
            .OrderByDescending(s => s.ValidFrom)
            .FirstOrDefaultAsync();

        if (openSchedule != null)
            openSchedule.ValidTo = data.ValidFrom.AddDays(-1);

        _context.WorkSchedules.Add(schedule);
    }

    private static OrgRequestResponse MapToResponse(
        OrgRequest r, string orgName, string orgSlug, string? responderName = null)
    {
        return new OrgRequestResponse
        {
            Id = r.Id,
            UserId = r.UserId,
            UserFirstName = r.User.FirstName,
            UserLastName = r.User.LastName,
            UserEmail = r.User.Email,
            OrganizationId = r.OrganizationId,
            OrganizationName = orgName,
            OrganizationSlug = orgSlug,
            Type = r.Type.ToString(),
            Status = r.Status.ToString(),
            Message = r.Message,
            RelatedEntityId = r.RelatedEntityId,
            RequestData = r.RequestData,
            CreatedAt = r.CreatedAt,
            RespondedAt = r.RespondedAt,
            RespondedByName = responderName
                ?? (r.RespondedByUser != null ? $"{r.RespondedByUser.FirstName} {r.RespondedByUser.LastName}" : null)
        };
    }

    private async Task<Organization?> GetOrgBySlugAsync(string slug)
    {
        return await _context.Organizations.AsNoTracking().FirstOrDefaultAsync(o => o.Slug == slug);
    }

    private async Task<OrganizationRole?> GetRoleAsync(int userId, int organizationId)
    {
        var membership = await _context.UserOrganizations
            .AsNoTracking()
            .FirstOrDefaultAsync(uo => uo.OrganizationId == organizationId
                                    && uo.UserId == userId);
        return membership?.Role;
    }
}
