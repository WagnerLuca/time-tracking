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
    private readonly IEmailSender _email;
    private readonly EmailSettings _emailSettings;
    private readonly ILogger<RequestService> _logger;
    private readonly RlsContext _rls;

    public RequestService(TimeTrackingDbContext context, IEmailSender email, EmailSettings emailSettings, ILogger<RequestService> logger, RlsContext rls)
    {
        _context = context;
        _rls = rls;
        _email = email;
        _emailSettings = emailSettings;
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
            var autoApprove = request.Type == RequestType.RequestAbsence
                && await EffectiveRuleResolver.CanApproveAbsencesAsync(_context, org.Id, userId);
            // A running timer is stopped, not edited (and the closed-month guard lets a stop through)
            if (request.Type == RequestType.EditPastEntry && request.RelatedEntityId is { } entryId
                && await _context.TimeEntries.AnyAsync(e => e.Id == entryId && e.IsRunning))
                return ServiceResult.BadRequest<OrgRequestResponse>("Cannot edit a running time entry.");
            await ClosedPeriods.EnsureRequestOpenAsync(_context, org, userId, new OrgRequest
            {
                OrganizationId = org.Id, UserId = userId, Type = request.Type,
                RelatedEntityId = request.RelatedEntityId, RequestData = request.RequestData
            }, autoApprove, _rls.ChangeReason);
            if (autoApprove)
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

        // Several open requests of one type are fine (two failure entries, two vacations), the same one twice is not
        var duplicate = await FindDuplicateAsync(org.Id, userId, request);
        if (duplicate != null)
            return ServiceResult.Conflict<OrgRequestResponse>(duplicate);

        var orgRequest = new OrgRequest
        {
            UserId = userId,
            OrganizationId = org.Id,
            Type = request.Type,
            Message = request.Message,
            RelatedEntityId = request.RelatedEntityId,
            RequestData = request.RequestData,
            Status = RequestStatus.Pending,
            GroupId = (await GroupTree.LoadAsync(_context, org.Id)).RoutingGroupFor(userId, request.Type)
        };

        _context.OrgRequests.Add(orgRequest);
        await _context.SaveChangesAsync();
        _logger.LogInformation("Request {RequestId} of type {Type} created by user {UserId} for org {OrgSlug} (group {GroupId})", orgRequest.Id, orgRequest.Type, userId, slug, orgRequest.GroupId);

        orgRequest.User = user;
        if (orgRequest.GroupId.HasValue)
            orgRequest.Group = await _context.Groups.AsNoTracking().FirstOrDefaultAsync(g => g.Id == orgRequest.GroupId.Value);
        await MailApproversAsync(orgRequest, org, user);
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
        var adminGroupIds = !isOrgAdmin ? await GetAdminGroupIdsAsync(callerUserId, org.Id) : new HashSet<int>();

        if (!isOrgAdmin && adminGroupIds.Count == 0)
            return ServiceResult.Forbidden<PaginatedResponse<OrgRequestResponse>>();

        (limit, offset) = PaginationDefaults.Normalize(limit, offset);

        var query = _context.OrgRequests
            .AsNoTracking()
            .Include(r => r.User)
            .Include(r => r.RespondedByUser)
            .Include(r => r.Group)
            .Where(r => r.OrganizationId == org.Id);

        // Group Admins can only see requests routed to their groups (and their sub-groups)
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
            Items = await WithClosedMonthsAsync(requests, requests.Select(r => MapToResponse(r, org.Name, org.Slug))),
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
                return ServiceResult.Forbidden<OrgRequestResponse>(code: "request.cannotRespond");

            // Still a member, still admin of the (active) group or one of its ancestors, and that
            // group's effective capabilities still grant this approval
            if (callerRole == null
                || !(await GroupTree.LoadAsync(_context, org.Id)).AdministersWith(callerUserId, orgRequest.GroupId.Value,
                       g => GroupTree.CanApprove(g, orgRequest.Type)))
                return ServiceResult.Forbidden<OrgRequestResponse>(code: "request.cannotRespond");
        }

        if (orgRequest.Status != RequestStatus.Pending)
            return ServiceResult.BadRequest<OrgRequestResponse>("This request has already been responded to.");

        orgRequest.Status = request.Accept ? RequestStatus.Accepted : RequestStatus.Declined;
        orgRequest.RespondedAt = DateTime.UtcNow;
        orgRequest.RespondedByUserId = callerUserId;
        orgRequest.ResponseReason = string.IsNullOrWhiteSpace(request.Reason) ? null : request.Reason.Trim();

        // Apply side effects on acceptance. They are only staged here and saved together with
        // the status below, so an approval applies completely or not at all.
        if (request.Accept)
        {
            _context.EntryChangeKindOverride = TimeEntryChangeKind.ApprovedRequest;
            var error = await ApplyAcceptanceSideEffectsAsync(orgRequest, org);
            if (error != null)
                return ServiceResult.BadRequest<OrgRequestResponse>(
                    $"The request cannot be applied: {error} Decline it and ask for a new one.");
        }

        // The in-app notification is saved together with the decision
        var (_, labelEn) = NotificationMails.Label(orgRequest.Type);
        _context.Notifications.Add(new Notification
        {
            UserId = orgRequest.UserId,
            OrganizationId = org.Id,
            Title = request.Accept ? "Request accepted" : "Request declined",
            Message = $"Your {labelEn} request in {org.Name} was {(request.Accept ? "accepted" : "declined")}."
                      + (orgRequest.ResponseReason != null ? $" Reason: {orgRequest.ResponseReason}" : ""),
            Type = request.Accept ? "RequestAccepted" : "RequestDeclined",
            RelatedEntityId = orgRequest.Id
        });

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

        await NotifyRequesterAsync(orgRequest, org);
        var responder = await _context.Users.FindAsync(callerUserId);
        return ServiceResult.Ok(MapToResponse(orgRequest, org.Name, org.Slug,
            responder != null ? $"{responder.FirstName} {responder.LastName}" : null));
    }

    /// <summary>
    /// The requester takes back an own request while it is still pending. Approvers see open
    /// requests only as pending rows (no stored notifications), so it simply drops out of their
    /// inbox; no mail. Marked as seen right away: there is no decision to tell the requester.
    /// </summary>
    public async Task<ServiceResult<OrgRequestResponse>> WithdrawRequestAsync(string slug, int userId, int requestId)
    {
        var org = await _context.FindOrgBySlugAsync(slug);
        if (org == null)
            return ServiceResult.NotFound<OrgRequestResponse>("Organization not found");

        // Someone else's request is "not found" rather than forbidden: no hint that it exists
        var orgRequest = await _context.OrgRequests
            .Include(r => r.User)
            .Include(r => r.Group)
            .FirstOrDefaultAsync(r => r.Id == requestId && r.OrganizationId == org.Id && r.UserId == userId);
        if (orgRequest == null)
            return ServiceResult.NotFound<OrgRequestResponse>("Request not found.");

        if (orgRequest.Status != RequestStatus.Pending)
            return ServiceResult.BadRequest<OrgRequestResponse>("Only pending requests can be withdrawn.");

        var now = DateTime.UtcNow;
        orgRequest.Status = RequestStatus.Withdrawn;
        orgRequest.RespondedAt = now;
        orgRequest.RespondedByUserId = userId;
        orgRequest.UserNotifiedAt = now;

        try
        {
            await _context.SaveChangesAsync();
        }
        catch (DbUpdateConcurrencyException)
        {
            // Status is a concurrency token: an approver answered (or a second click withdrew) it meanwhile
            return ServiceResult.Conflict<OrgRequestResponse>("Only pending requests can be withdrawn.");
        }

        _logger.LogInformation("Request {RequestId} withdrawn by user {UserId}", requestId, userId);
        return ServiceResult.Ok(MapToResponse(orgRequest, org.Name, org.Slug));
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

        // The responder's name only where the visibility matrix shows that person to the requester
        var visibilityByOrg = new Dictionary<int, MemberVisibility>();
        foreach (var org in requests.Select(r => r.Organization).DistinctBy(o => o.Id))
            visibilityByOrg[org.Id] = await MemberVisibility.ForCallerAsync(_context, org, userId);

        var items = requests.Select(r =>
        {
            r.User = user;
            if (r.RespondedByUserId is int by && by != userId && !visibilityByOrg[r.OrganizationId].Can(by, MemberInfo.Member))
                r.RespondedByUser = null;
            return MapToResponse(r, r.Organization.Name, r.Organization.Slug);
        }).ToList();

        return ServiceResult.Ok(new PaginatedResponse<OrgRequestResponse>
        {
            Items = await WithClosedMonthsAsync(requests, items),
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

        // Group-level admin: sees pending requests routed to their (active) groups and their
        // sub-groups, only while still a member of the organization and only for request types
        // the administered group may approve (effective capabilities, GroupTree)
        var groupAdminOrgIds = await _context.GroupMembers
            .Where(gm => gm.UserId == userId && gm.Role == GroupRole.Admin && gm.Group.IsActive
                      && _context.UserOrganizations.Any(uo => uo.UserId == userId && uo.OrganizationId == gm.Group.OrganizationId))
            .Select(gm => gm.Group.OrganizationId)
            .Distinct()
            .ToListAsync();
        var trees = new Dictionary<int, GroupTree>();
        foreach (var orgId in groupAdminOrgIds)
            trees[orgId] = await GroupTree.LoadAsync(_context, orgId);
        var groupAdminGroupIds = trees.Values
            .SelectMany(t => t.AdministeredBy(userId).SelectMany(t.Subtree))
            .ToList();

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
                     || (r.GroupId is { } groupId && trees.TryGetValue(r.OrganizationId, out var tree)
                         && tree.AdministersWith(userId, groupId, g => GroupTree.CanApprove(g, r.Type))))
            .ToList();

        return ServiceResult.Ok(new AdminNotificationResponse
        {
            PendingRequests = pendingRequests.Count,
            Requests = await WithClosedMonthsAsync(pendingRequests, pendingRequests.Select(r =>
                MapToResponse(r, r.Organization.Name, r.Organization.Slug)))
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
                    if (entry.IsRunning)
                        return "The time entry is still running.";

                    var data = JsonSerializer.Deserialize<EditEntryRequestData>(orgRequest.RequestData, RequestDataJson);
                    if (data != null)
                    {
                        var timesChanged = data.StartTime.HasValue || data.EndTime.HasValue;
                        if (data.StartTime.HasValue) entry.StartTime = data.StartTime.Value;
                        if (data.EndTime.HasValue)   { entry.EndTime = data.EndTime.Value; entry.IsFailure = false; }
                        if (data.Description != null) entry.Description = data.Description;
                        if (EntryTimes.Validate(entry.StartTime, entry.EndTime) is { } timesError)
                            return timesError;
                        if (timesChanged) await PauseRules.ApplyAsync(_context, entry);
                        entry.UpdatedAt = DateTime.UtcNow;
                    }
                }
                break;

            case RequestType.AddManualEntry:
                var manual = orgRequest.RequestData == null ? null
                    : JsonSerializer.Deserialize<ManualEntryRequestData>(orgRequest.RequestData, RequestDataJson);
                if (manual == null) return "Its data is invalid.";
                if (EntryTimes.Validate(manual.StartTime, manual.EndTime) is { } manualError) return manualError;
                if (manual.Description?.Length > 500 || manual.PauseDurationMinutes is < 0 or > 1440) return "Its data is invalid.";
                if (await EntryTimes.OverlapsAsync(_context, orgRequest.UserId, manual.StartTime, manual.EndTime))
                    return "It overlaps an existing time entry.";
                var manualEntry = new TimeEntry
                {
                    UserId = orgRequest.UserId,
                    OrganizationId = org.Id,
                    StartTime = manual.StartTime,
                    EndTime = manual.EndTime,
                    Description = manual.Description,
                    Source = TimeEntrySource.Manual,
                    CreatedAt = DateTime.UtcNow,
                    UpdatedAt = DateTime.UtcNow
                };
                if (manual.PauseDurationMinutes.HasValue)
                {
                    manualEntry.PauseDurationMinutes = manual.PauseDurationMinutes.Value;
                    manualEntry.PauseIsManual = true;
                }
                await PauseRules.ApplyAsync(_context, manualEntry);
                _context.TimeEntries.Add(manualEntry);
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
                        pauseEntry.PauseIsManual = true;
                        await PauseRules.ApplyAsync(_context, pauseEntry);
                        pauseEntry.UpdatedAt = DateTime.UtcNow;
                    }
                }
                break;

            case RequestType.WorkScheduleChange:
                if (orgRequest.RequestData != null)
                {
                    var scheduleData = JsonSerializer.Deserialize<WorkScheduleChangeRequestData>(orgRequest.RequestData, RequestDataJson);
                    if (scheduleData != null
                        && await ApplyWorkScheduleChangeAsync(orgRequest.UserId, org.Id, scheduleData) is { } scheduleError)
                        return scheduleError;
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

        // A range keeps only the member's working days (schedule, holidays); one picked day stays as asked
        var requestedDates = data.Dates.Distinct().ToList();
        if (requestedDates.Count > 1)
            requestedDates = await WorkingDays.FilterAsync(_context, orgRequest.UserId, org.Id, requestedDates);
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

    /// <summary>Stages the new period like a direct creation (same overlap rule, open predecessor auto-closed); returns why it cannot be applied.</summary>
    private async Task<string?> ApplyWorkScheduleChangeAsync(
        int userId, int orgId, WorkScheduleChangeRequestData data)
    {
        if (data.ValidTo is { } validTo && validTo < data.ValidFrom)
            return "Its data is invalid.";
        if (await WorkScheduleService.CheckOverlapAsync(_context, userId, orgId, data.ValidFrom, data.ValidTo) is { } overlap)
            return overlap;

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
            schedule.TargetSat = data.TargetSat ?? 0;
            schedule.TargetSun = data.TargetSun ?? 0;
        }

        await WorkScheduleService.CloseOpenScheduleAsync(_context, userId, orgId, data.ValidFrom);
        _context.WorkSchedules.Add(schedule);
        return null;
    }

    private async Task ApplyCsvImportAsync(int userId, Organization org, List<CsvImportEntry> entries)
    {
        var imported = 0;
        var clock = OrgClock.For(org);
        // Same row checks as a direct import; the request data was only stored, never validated
        foreach (var entry in entries.Take(EntryTimes.MaxImportRows))
        {
            if (TimeEntryExportService.ParseImportRow(clock, entry.Date, entry.StartTime, entry.EndTime, entry.PauseMinutes,
                    entry.Description, out var startUtc, out var endUtc) != null)
                continue;

            _context.TimeEntries.Add(new TimeEntry
            {
                UserId = userId,
                OrganizationId = org.Id,
                StartTime = startUtc,
                EndTime = endUtc,
                IsRunning = false,
                PauseDurationMinutes = entry.PauseMinutes,
                PauseIsManual = true,   // imported records keep their pause
                Description = entry.Description,
                Source = TimeEntrySource.Import,
                CreatedAt = DateTime.UtcNow,
                UpdatedAt = DateTime.UtcNow
            });
            imported++;
        }

        _logger.LogInformation("CSV import approved: {Count} entries created for user {UserId} in org {OrgId}",
            imported, userId, org.Id);
    }

    /// <summary>Marks the pending requests that would change a closed month (the hint in the inbox).</summary>
    private async Task<List<OrgRequestResponse>> WithClosedMonthsAsync(List<OrgRequest> requests, IEnumerable<OrgRequestResponse> mapped)
    {
        var closed = await ClosedPeriods.TouchedByRequestsAsync(_context, requests);
        return mapped.Select(m => closed.TryGetValue(m.Id, out var month) ? m with { ClosedMonth = month } : m).ToList();
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
            ResponseReason = r.ResponseReason,
            GroupId = r.GroupId,
            GroupName = r.Group?.Name
        };
    }

    /// <summary>
    /// Why this request would duplicate one that is still open, or null. Joining and the initial
    /// overtime are single values (one open request); a request about an entity (a time entry)
    /// conflicts with an open one about the same entity; absence requests with open ones sharing a
    /// date; anything else only with an identical open request.
    /// </summary>
    private async Task<string?> FindDuplicateAsync(int orgId, int userId, CreateOrgRequestRequest request)
    {
        var open = await _context.OrgRequests.AsNoTracking()
            .Where(r => r.OrganizationId == orgId && r.UserId == userId
                     && r.Type == request.Type && r.Status == RequestStatus.Pending)
            .ToListAsync();
        if (open.Count == 0) return null;

        const string same = "You already have a pending request for this.";
        if (request.Type is RequestType.JoinOrganization or RequestType.SetInitialOvertime)
            return "You already have a pending request of this type for this organization.";
        if (request.RelatedEntityId.HasValue)
            return open.Any(r => r.RelatedEntityId == request.RelatedEntityId) ? same : null;
        if (request.Type == RequestType.RequestAbsence)
        {
            var dates = AbsenceDates(request.RequestData);
            var overlap = open.SelectMany(r => AbsenceDates(r.RequestData)).Where(dates.Contains).Distinct().Order().ToList();
            return overlap.Count > 0
                ? $"You already have a pending absence request for {string.Join(", ", overlap.Select(d => d.ToString("yyyy-MM-dd")))}."
                : null;
        }
        return open.Any(r => r.RequestData == request.RequestData) ? same : null;
    }

    private static HashSet<DateOnly> AbsenceDates(string? requestData)
    {
        if (requestData == null) return [];
        try { return JsonSerializer.Deserialize<AbsenceRequestData>(requestData, RequestDataJson)?.Dates.ToHashSet() ?? []; }
        catch (JsonException) { return []; }
    }

    /// <summary>
    /// Mails everyone who may decide the new request, once each: org admins/owners and, for a
    /// group-routed one, the admins along the group's chain whose group approves the type.
    /// </summary>
    private async Task MailApproversAsync(OrgRequest orgRequest, Organization org, User requester)
    {
        var groupApprovers = orgRequest.GroupId is { } groupId
            ? (await GroupTree.LoadAsync(_context, org.Id)).ApproversOf(groupId, orgRequest.Type)
            : [];
        var approvers = await _context.UserOrganizations.AsNoTracking()
            .Where(uo => uo.OrganizationId == org.Id && uo.UserId != requester.Id && uo.User.IsActive
                      && (uo.Role >= OrganizationRole.Admin || groupApprovers.Contains(uo.UserId)))
            .Select(uo => uo.User.Email)
            .ToListAsync();

        var (de, en) = NotificationMails.Label(orgRequest.Type);
        var name = $"{requester.FirstName} {requester.LastName}";
        foreach (var to in approvers)
            await NotificationMails.TrySendAsync(_email, to,
                $"Neue Anfrage in {org.Name} / New request in {org.Name}",
                $"{name} hat eine Anfrage gestellt: {de}. / {name} submitted a request: {en}.\n\n" +
                $"{_emailSettings.FrontendBaseUrl}/organizations/{org.Slug}",
                _logger);
    }

    /// <summary>Mails the requester the decision (with the reason, if one was given).</summary>
    private async Task NotifyRequesterAsync(OrgRequest orgRequest, Organization org)
    {
        if (!orgRequest.User.IsActive) return;
        var accepted = orgRequest.Status == RequestStatus.Accepted;
        var (de, en) = NotificationMails.Label(orgRequest.Type);
        var reason = orgRequest.ResponseReason != null ? $"\n\nBegründung / Reason: {orgRequest.ResponseReason}" : "";
        await NotificationMails.TrySendAsync(_email, orgRequest.User.Email,
            accepted ? $"Anfrage angenommen / Request accepted ({org.Name})" : $"Anfrage abgelehnt / Request declined ({org.Name})",
            $"Deine Anfrage ({de}) in {org.Name} wurde {(accepted ? "angenommen" : "abgelehnt")}. / " +
            $"Your {en} request in {org.Name} was {(accepted ? "accepted" : "declined")}.{reason}\n\n{_emailSettings.FrontendBaseUrl}/",
            _logger);
    }

    /// <summary>
    /// The groups whose routed requests a Group Admin sees in this org: the groups they administer
    /// whose effective capabilities approve anything, with all their sub-groups.
    /// </summary>
    private async Task<HashSet<int>> GetAdminGroupIdsAsync(int userId, int orgId)
    {
        var tree = await GroupTree.LoadAsync(_context, orgId);
        return tree.AdministeredBy(userId)
            .Where(id => GroupTree.CanApproveAny(tree.Effective(id)))
            .SelectMany(tree.Subtree)
            .ToHashSet();
    }
}
