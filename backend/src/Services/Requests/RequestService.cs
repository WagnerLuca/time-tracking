using System.Globalization;
using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using TimeTracking.Api.Data;
using TimeTracking.Api.Models;
using TimeTracking.Api.Models.Dtos;

namespace TimeTracking.Api.Services;

public class RequestService : IRequestService
{
    private readonly TimeTrackingDbContext _context;
    private readonly ILogger<RequestService> _logger;

    public RequestService(TimeTrackingDbContext context, ILogger<RequestService> logger)
    {
        _context = context;
        _logger = logger;
    }

    public async Task<ServiceResult<OrgRequestResponse>> CreateRequestAsync(
        string slug, int userId, CreateOrgRequestRequest request)
    {
        var org = await _context.FindOrgBySlugAsync(slug);
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
                await Memberships.ActivateAsync(_context, userId, org, OrganizationRole.Member);
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

            // Resolve the effective rule (org default + group overrides, most permissive wins)
            var ruleMode = await EffectiveRuleResolver.ResolveAsync(_context, org, userId, request.Type);

            if (ruleMode == RuleMode.Disabled)
                return ServiceResult.BadRequest<OrgRequestResponse>("This feature is disabled for this organization.");
            if (ruleMode == RuleMode.Allowed && request.Type != RequestType.RequestAbsence)
                return ServiceResult.BadRequest<OrgRequestResponse>("You can perform this action directly without a request.");

            // Absence requests from users who can themselves approve absences are auto-approved.
            // The request is still recorded for a complete audit trail.
            if (request.Type == RequestType.RequestAbsence
                && await EffectiveRuleResolver.CanApproveAbsencesAsync(_context, org.Id, userId))
            {
                var autoApproved = new OrgRequest
                {
                    UserId = userId,
                    OrganizationId = org.Id,
                    Type = RequestType.RequestAbsence,
                    Message = request.Message,
                    RequestData = request.RequestData,
                    Status = RequestStatus.Accepted,
                    RespondedAt = DateTime.UtcNow,
                    RespondedByUserId = userId
                };
                _context.OrgRequests.Add(autoApproved);
                await _context.SaveChangesAsync(); // assign Id for the audit link
                await ApplyAcceptanceSideEffectsAsync(autoApproved, org);
                await _context.SaveChangesAsync();
                _logger.LogInformation("Absence request {RequestId} auto-approved for approver {UserId} in org {OrgSlug}", autoApproved.Id, userId, slug);

                autoApproved.User = user;
                return ServiceResult.Ok(MapToResponse(autoApproved, org.Name, org.Slug,
                    $"{user.FirstName} {user.LastName}"));
            }
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
            Status = RequestStatus.Pending,
            GroupId = await ResolveRoutingGroupIdAsync(userId, org.Id, request.Type)
        };

        _context.OrgRequests.Add(orgRequest);
        await _context.SaveChangesAsync();
        _logger.LogInformation("Request {RequestId} of type {Type} created by user {UserId} for org {OrgSlug} (group {GroupId})", orgRequest.Id, orgRequest.Type, userId, slug, orgRequest.GroupId);

        orgRequest.User = user;
        if (orgRequest.GroupId.HasValue)
            orgRequest.Group = await _context.Groups.AsNoTracking().FirstOrDefaultAsync(g => g.Id == orgRequest.GroupId.Value);
        return ServiceResult.Ok(MapToResponse(orgRequest, org.Name, org.Slug));
    }

    public async Task<ServiceResult<PaginatedResponse<OrgRequestResponse>>> GetRequestsAsync(
        string slug, int callerUserId, RequestType? type, RequestStatus? status, int limit, int offset)
    {
        var org = await _context.FindOrgBySlugAsync(slug);
        if (org == null)
            return ServiceResult.NotFound<PaginatedResponse<OrgRequestResponse>>("Organization not found");

        var callerRole = await _context.GetOrgRoleAsync(callerUserId, org.Id);
        var isOrgAdmin = callerRole != null && callerRole >= OrganizationRole.Admin;
        var adminGroupIds = !isOrgAdmin ? await GetAdminGroupIdsAsync(callerUserId, org.Id) : new List<int>();

        if (!isOrgAdmin && adminGroupIds.Count == 0)
            return ServiceResult.Forbidden<PaginatedResponse<OrgRequestResponse>>();

        (limit, offset) = PaginationDefaults.Normalize(limit, offset);

        var query = _context.OrgRequests
            .AsNoTracking()
            .Include(r => r.User)
            .Include(r => r.RespondedByUser)
            .Include(r => r.Group)
            .Where(r => r.OrganizationId == org.Id);

        // Group Admins can only see requests routed to their groups
        if (!isOrgAdmin)
            query = query.Where(r => r.GroupId.HasValue && adminGroupIds.Contains(r.GroupId.Value));

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
        var org = await _context.FindOrgBySlugAsync(slug);
        if (org == null)
            return ServiceResult.NotFound<OrgRequestResponse>("Organization not found");

        var orgRequest = await _context.OrgRequests
            .Include(r => r.User)
            .Include(r => r.Group)
            .FirstOrDefaultAsync(r => r.Id == requestId && r.OrganizationId == org.Id);

        if (orgRequest == null)
            return ServiceResult.NotFound<OrgRequestResponse>("Request not found.");

        // Authorization: Org Admin can always respond; Group Admin can respond to group-routed requests
        var callerRole = await _context.GetOrgRoleAsync(callerUserId, org.Id);
        var isOrgAdmin = callerRole != null && callerRole >= OrganizationRole.Admin;

        if (!isOrgAdmin)
        {
            if (!orgRequest.GroupId.HasValue)
                return ServiceResult.Forbidden<OrgRequestResponse>();

            // Still a member, still admin of the (active) group, and the group still grants this approval
            var group = callerRole == null ? null : await _context.GroupMembers
                .Where(gm => gm.GroupId == orgRequest.GroupId.Value
                          && gm.UserId == callerUserId
                          && gm.Role == GroupRole.Admin
                          && gm.Group.IsActive)
                .Select(gm => gm.Group)
                .FirstOrDefaultAsync();
            if (group == null || !GroupCanApprove(group, orgRequest.Type))
                return ServiceResult.Forbidden<OrgRequestResponse>();
        }

        if (orgRequest.Status != RequestStatus.Pending)
            return ServiceResult.BadRequest<OrgRequestResponse>("This request has already been responded to.");

        orgRequest.Status = request.Accept ? RequestStatus.Accepted : RequestStatus.Declined;
        orgRequest.RespondedAt = DateTime.UtcNow;
        orgRequest.RespondedByUserId = callerUserId;

        // Apply side effects on acceptance. They are only staged here and saved together with
        // the status below, so an approval applies completely or not at all.
        if (request.Accept)
        {
            var error = await ApplyAcceptanceSideEffectsAsync(orgRequest, org);
            if (error != null)
                return ServiceResult.BadRequest<OrgRequestResponse>(
                    $"The request cannot be applied: {error} Decline it and ask for a new one.");
        }

        try
        {
            await _context.SaveChangesAsync();
        }
        catch (DbUpdateConcurrencyException)
        {
            // Status is a concurrency token: another admin answered this request at the same time
            return ServiceResult.Conflict<OrgRequestResponse>("This request has already been responded to.");
        }

        _logger.LogInformation("Request {RequestId} {Action} by user {UserId}", requestId, request.Accept ? "accepted" : "declined", callerUserId);

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
            .Include(r => r.Group)
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
        // Org-level admin: sees all pending requests for their orgs
        var adminOrgIds = await _context.UserOrganizations
            .Where(uo => uo.UserId == userId && uo.Role >= OrganizationRole.Admin)
            .Select(uo => uo.OrganizationId)
            .ToListAsync();

        // Group-level admin: sees pending requests routed to their (active) groups, only while
        // still a member of the organization and only for request types the group may approve
        var adminGroups = await _context.GroupMembers
            .Where(gm => gm.UserId == userId && gm.Role == GroupRole.Admin && gm.Group.IsActive
                      && _context.UserOrganizations.Any(uo => uo.UserId == userId && uo.OrganizationId == gm.Group.OrganizationId))
            .Select(gm => gm.Group)
            .ToListAsync();
        var groupAdminGroupIds = adminGroups.Select(g => g.Id).ToList();

        // Organizations that require 2FA show nothing to callers without it
        var hasTwoFactor = await _context.Users.Where(u => u.Id == userId).Select(u => u.TwoFactorEnabled).FirstOrDefaultAsync();
        var blockedOrgIds = hasTwoFactor ? new List<int>() : await _context.Organizations
            .Where(o => o.Require2fa).Select(o => o.Id).ToListAsync();

        if (adminOrgIds.Count == 0 && groupAdminGroupIds.Count == 0)
            return ServiceResult.Ok(new AdminNotificationResponse
            {
                PendingRequests = 0,
                Requests = new List<OrgRequestResponse>()
            });

        var pendingRequests = await _context.OrgRequests
            .AsNoTracking()
            .Include(r => r.User)
            .Include(r => r.Organization)
            .Include(r => r.Group)
            .Where(r => r.Status == RequestStatus.Pending
                     && !blockedOrgIds.Contains(r.OrganizationId)
                     && (adminOrgIds.Contains(r.OrganizationId)
                         || (r.GroupId.HasValue && groupAdminGroupIds.Contains(r.GroupId.Value))))
            .OrderByDescending(r => r.CreatedAt)
            .ToListAsync();
        pendingRequests = pendingRequests
            .Where(r => adminOrgIds.Contains(r.OrganizationId)
                     || adminGroups.Any(g => g.Id == r.GroupId && GroupCanApprove(g, r.Type)))
            .ToList();

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
            .Include(r => r.Group)
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

    public async Task<ServiceResult<MarkedSeenResponse>> MarkNotificationsSeenAsync(int userId, List<int>? requestIds)
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
        return ServiceResult.Ok(new MarkedSeenResponse { Marked = requests.Count });
    }

    // ────────────────────────────────────────────────────
    //  Private helpers
    // ────────────────────────────────────────────────────

    private static readonly JsonSerializerOptions RequestDataJson = new() { PropertyNameCaseInsensitive = true };

    /// <summary>Stages the effects of an accepted request (no save). Returns an error message when they cannot be applied.</summary>
    private async Task<string?> ApplyAcceptanceSideEffectsAsync(OrgRequest orgRequest, Organization org)
    {
        try
        {
            return await ApplyAcceptanceSideEffectsCoreAsync(orgRequest, org);
        }
        catch (JsonException)
        {
            return "Its data is invalid.";
        }
    }

    private async Task<string?> ApplyAcceptanceSideEffectsCoreAsync(OrgRequest orgRequest, Organization org)
    {
        switch (orgRequest.Type)
        {
            case RequestType.JoinOrganization:
                await Memberships.ActivateAsync(_context, orgRequest.UserId, org, OrganizationRole.Member);
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
                    if (entry == null)
                        return "The time entry no longer exists.";

                    var data = JsonSerializer.Deserialize<EditEntryRequestData>(orgRequest.RequestData, RequestDataJson);
                    if (data != null)
                    {
                        var timesChanged = data.StartTime.HasValue || data.EndTime.HasValue;
                        if (data.StartTime.HasValue) entry.StartTime = data.StartTime.Value;
                        if (data.EndTime.HasValue)   { entry.EndTime = data.EndTime.Value; entry.IsFailure = false; }
                        if (data.Description != null) entry.Description = data.Description;
                        if (entry.EndTime.HasValue && entry.EndTime.Value <= entry.StartTime)
                            return "The end time would be before the start time.";
                        if (timesChanged) await PauseRules.ApplyAsync(_context, entry);
                        entry.UpdatedAt = DateTime.UtcNow;
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
                    var scheduleData = JsonSerializer.Deserialize<WorkScheduleChangeRequestData>(orgRequest.RequestData, RequestDataJson);
                    if (scheduleData != null)
                        await ApplyWorkScheduleChangeAsync(orgRequest.UserId, org.Id, scheduleData);
                }
                break;

            case RequestType.CsvImport:
                if (orgRequest.RequestData != null)
                {
                    var importData = JsonSerializer.Deserialize<CsvImportRequestData>(orgRequest.RequestData, RequestDataJson);
                    if (importData?.Entries != null)
                        await ApplyCsvImportAsync(orgRequest.UserId, org, importData.Entries);
                }
                break;

            case RequestType.RequestAbsence:
                if (orgRequest.RequestData != null)
                {
                    var absenceData = JsonSerializer.Deserialize<AbsenceRequestData>(orgRequest.RequestData, RequestDataJson);
                    if (absenceData != null)
                        await ApplyAbsenceRequestAsync(orgRequest, org, absenceData);
                }
                break;
        }

        return null;
    }

    /// <summary>
    /// Creates the requested absence days on approval. Dates that already have an
    /// absence are skipped. Approver and request are recorded for the audit trail.
    /// </summary>
    private async Task ApplyAbsenceRequestAsync(OrgRequest orgRequest, Organization org, AbsenceRequestData data)
    {
        if (data.Dates.Count == 0) return;

        // Validate optional custom absence type belongs to this org
        int? customTypeId = null;
        var type = data.Type;
        if (data.CustomTypeId.HasValue)
        {
            var custom = await _context.AbsenceTypeDefinitions
                .AsNoTracking()
                .FirstOrDefaultAsync(t => t.Id == data.CustomTypeId.Value
                                       && t.OrganizationId == org.Id && t.IsActive);
            if (custom == null) return; // unknown type — do not create anything
            customTypeId = custom.Id;
            type = AbsenceType.Other;
        }

        var requestedDates = data.Dates.Distinct().ToList();
        var existingDates = (await _context.AbsenceDays
            .Where(a => a.OrganizationId == org.Id
                     && a.UserId == orgRequest.UserId
                     && requestedDates.Contains(a.Date))
            .Select(a => a.Date)
            .ToListAsync()).ToHashSet();

        foreach (var date in requestedDates)
        {
            if (existingDates.Contains(date)) continue;
            _context.AbsenceDays.Add(new AbsenceDay
            {
                UserId = orgRequest.UserId,
                OrganizationId = org.Id,
                Date = date,
                Type = type,
                CustomTypeId = customTypeId,
                IsHalfDay = data.IsHalfDay,
                Note = data.Note,
                CreatedByUserId = orgRequest.RespondedByUserId,
                CreatedViaRequestId = orgRequest.Id
            });
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

    private async Task ApplyCsvImportAsync(int userId, Organization org, List<CsvImportEntry> entries)
    {
        var imported = 0;
        foreach (var entry in entries)
        {
            if (string.IsNullOrEmpty(entry.Date) || string.IsNullOrEmpty(entry.StartTime)
                || string.IsNullOrEmpty(entry.EndTime))
                continue;

            if (!DateTime.TryParse($"{entry.Date} {entry.StartTime}", CultureInfo.InvariantCulture,
                    DateTimeStyles.None, out var startTime))
                continue;
            if (!DateTime.TryParse($"{entry.Date} {entry.EndTime}", CultureInfo.InvariantCulture,
                    DateTimeStyles.None, out var endTime))
                continue;
            if (endTime <= startTime)
                continue;

            _context.TimeEntries.Add(new TimeEntry
            {
                UserId = userId,
                OrganizationId = org.Id,
                // CSV times are the organization's local wall-clock times
                StartTime = OrgClock.For(org).ToUtc(startTime),
                EndTime = OrgClock.For(org).ToUtc(endTime),
                IsRunning = false,
                PauseDurationMinutes = Math.Max(0, entry.PauseMinutes),
                Description = entry.Description,
                CreatedAt = DateTime.UtcNow,
                UpdatedAt = DateTime.UtcNow
            });
            imported++;
        }

        _logger.LogInformation("CSV import approved: {Count} entries created for user {UserId} in org {OrgId}",
            imported, userId, org.Id);
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
                ?? (r.RespondedByUser != null ? $"{r.RespondedByUser.FirstName} {r.RespondedByUser.LastName}" : null),
            GroupId = r.GroupId,
            GroupName = r.Group?.Name
        };
    }

    /// <summary>
    /// Determines which group (if any) a request should be routed to for approval.
    /// Returns the first group where the user is a member AND a Group Admin has the
    /// relevant approval capability enabled.
    /// </summary>
    private async Task<int?> ResolveRoutingGroupIdAsync(int userId, int orgId, RequestType requestType)
    {
        // Join requests are always handled at the org level
        if (requestType == RequestType.JoinOrganization)
            return null;

        // Find all groups the user belongs to in this org that have a Group Admin
        // with the relevant approval capability
        var userGroups = await _context.GroupMembers
            .Include(gm => gm.Group)
            .Where(gm => gm.UserId == userId
                      && gm.Group.OrganizationId == orgId
                      && gm.Group.IsActive)
            .Select(gm => gm.Group)
            .ToListAsync();

        foreach (var group in userGroups)
        {
            if (GroupCanApprove(group, requestType))
            {
                // Verify the group actually has at least one Group Admin
                var hasGroupAdmin = await _context.GroupMembers
                    .AnyAsync(gm => gm.GroupId == group.Id && gm.Role == GroupRole.Admin);
                if (hasGroupAdmin)
                    return group.Id;
            }
        }

        return null;
    }

    /// <summary>Whether a group's admins may approve this kind of request.</summary>
    private static bool GroupCanApprove(Group group, RequestType type) => type switch
    {
        RequestType.EditPastEntry or RequestType.EditPause or RequestType.CsvImport or RequestType.SetInitialOvertime
            => group.GroupAdminCanApproveTimeEdits == true,
        RequestType.WorkScheduleChange => group.GroupAdminCanApproveScheduleChanges == true,
        RequestType.RequestAbsence => group.GroupAdminCanApproveAbsences == true,
        _ => false
    };

    /// <summary>
    /// Returns the IDs of groups where the user is a Group Admin within the given org,
    /// filtered to only groups that have at least one approval capability enabled.
    /// </summary>
    private async Task<List<int>> GetAdminGroupIdsAsync(int userId, int orgId)
    {
        return await _context.GroupMembers
            .Include(gm => gm.Group)
            .Where(gm => gm.UserId == userId
                      && gm.Role == GroupRole.Admin
                      && gm.Group.OrganizationId == orgId
                      && gm.Group.IsActive
                      && (gm.Group.GroupAdminCanApproveTimeEdits == true
                       || gm.Group.GroupAdminCanApproveScheduleChanges == true
                       || gm.Group.GroupAdminCanApproveAbsences == true))
            .Select(gm => gm.GroupId)
            .ToListAsync();
    }
}
