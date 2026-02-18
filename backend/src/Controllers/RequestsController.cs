using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using TimeTracking.Api.Data;
using TimeTracking.Api.Models;
using TimeTracking.Api.Models.Dtos;

namespace TimeTracking.Api.Controllers;

[ApiController]
[Route("api/organizations")]
public class RequestsController : OrganizationBaseController
{
    public RequestsController(TimeTrackingDbContext context) : base(context) { }

    // ── Helper: map OrgRequest entity to OrgRequestResponse ──
    private static OrgRequestResponse MapToResponse(OrgRequest r, string orgName, string orgSlug, string? responderName = null)
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

    // ────────────────────────────────────────────────────
    //  POST  /api/organizations/{slug}/requests
    //  Any authenticated user: create a request (join, edit, overtime, etc.)
    // ────────────────────────────────────────────────────
    [HttpPost("{slug}/requests")]
    [Authorize]
    public async Task<ActionResult<OrgRequestResponse>> CreateRequest(
        string slug, [FromBody] CreateOrgRequestRequest request)
    {
        var userId = GetCurrentUserId();
        if (userId == null) return Unauthorized();

        var org = await GetOrgBySlug(slug);
        if (org == null) return NotFound(new { message = "Organization not found" });

        var user = await _context.Users.FindAsync(userId.Value);
        if (user == null) return Unauthorized();

        // Type-specific validation
        if (request.Type == RequestType.JoinOrganization)
        {
            // Check if already a member
            var isMember = await _context.UserOrganizations
                .AnyAsync(uo => uo.OrganizationId == org.Id && uo.UserId == userId.Value && uo.IsActive);
            if (isMember)
                return BadRequest(new { message = "You are already a member of this organization." });

            // Check JoinPolicy
            if (org.JoinPolicy == RuleMode.Disabled)
                return BadRequest(new { message = "This organization does not accept join requests. Ask an admin to add you." });

            if (org.JoinPolicy == RuleMode.Allowed)
            {
                // Auto-join: create accepted request and add member immediately
                var autoRequest = new OrgRequest
                {
                    UserId = userId.Value,
                    OrganizationId = org.Id,
                    Type = RequestType.JoinOrganization,
                    Message = request.Message,
                    Status = RequestStatus.Accepted,
                    RespondedAt = DateTime.UtcNow
                };
                _context.OrgRequests.Add(autoRequest);
                _context.UserOrganizations.Add(new UserOrganization
                {
                    UserId = userId.Value,
                    OrganizationId = org.Id,
                    Role = OrganizationRole.Member,
                    IsActive = true,
                    JoinedAt = DateTime.UtcNow
                });
                await _context.SaveChangesAsync();
                autoRequest.User = user;
                return CreatedAtAction(nameof(GetRequests), new { slug }, MapToResponse(autoRequest, org.Name, org.Slug));
            }
            // RequiresApproval: fall through to create pending request
        }
        else
        {
            // For non-join requests, user must be a member
            var isMember = await _context.UserOrganizations
                .AnyAsync(uo => uo.OrganizationId == org.Id && uo.UserId == userId.Value && uo.IsActive);
            if (!isMember)
                return BadRequest(new { message = "You must be a member of this organization." });

            // Check the rule mode: if Disabled, reject; if Allowed, they shouldn't need a request
            var ruleMode = request.Type switch
            {
                RequestType.EditPastEntry => org.EditPastEntriesMode,
                RequestType.EditPause => org.EditPauseMode,
                RequestType.SetInitialOvertime => org.InitialOvertimeMode,
                _ => RuleMode.Allowed
            };

            if (ruleMode == RuleMode.Disabled)
                return BadRequest(new { message = "This feature is disabled for this organization." });

            if (ruleMode == RuleMode.Allowed)
                return BadRequest(new { message = "You can perform this action directly without a request." });
        }

        // Check if already has a pending request of the same type
        var pendingExists = await _context.OrgRequests
            .AnyAsync(r => r.OrganizationId == org.Id
                        && r.UserId == userId.Value
                        && r.Type == request.Type
                        && r.Status == RequestStatus.Pending);
        if (pendingExists)
            return BadRequest(new { message = "You already have a pending request of this type for this organization." });

        var orgRequest = new OrgRequest
        {
            UserId = userId.Value,
            OrganizationId = org.Id,
            Type = request.Type,
            Message = request.Message,
            RelatedEntityId = request.RelatedEntityId,
            RequestData = request.RequestData,
            Status = RequestStatus.Pending
        };

        _context.OrgRequests.Add(orgRequest);
        await _context.SaveChangesAsync();

        // Reload with navigation properties for response mapping
        orgRequest.User = user;
        return CreatedAtAction(nameof(GetRequests), new { slug }, MapToResponse(orgRequest, org.Name, org.Slug));
    }

    // ────────────────────────────────────────────────────
    //  GET  /api/organizations/{slug}/requests
    //  Admin+: get requests for an org (optionally filter by type/status)
    // ────────────────────────────────────────────────────
    [HttpGet("{slug}/requests")]
    [Authorize]
    public async Task<ActionResult<IEnumerable<OrgRequestResponse>>> GetRequests(
        string slug, [FromQuery] RequestType? type, [FromQuery] RequestStatus? status)
    {
        var callerId = GetCurrentUserId();
        if (callerId == null) return Unauthorized();

        var org = await GetOrgBySlug(slug);
        if (org == null) return NotFound(new { message = "Organization not found" });

        var callerRole = await GetCallerRole(org.Id);
        if (callerRole == null || callerRole < OrganizationRole.Admin)
            return Forbid();

        var query = _context.OrgRequests
            .Include(r => r.User)
            .Include(r => r.RespondedByUser)
            .Where(r => r.OrganizationId == org.Id);

        if (type.HasValue)
            query = query.Where(r => r.Type == type.Value);
        if (status.HasValue)
            query = query.Where(r => r.Status == status.Value);

        var requests = await query
            .OrderByDescending(r => r.CreatedAt)
            .ToListAsync();

        return Ok(requests.Select(r => MapToResponse(r, org.Name, org.Slug)));
    }

    // ────────────────────────────────────────────────────
    //  PUT  /api/organizations/{slug}/requests/{id}
    //  Admin+: accept or decline a request
    // ────────────────────────────────────────────────────
    [HttpPut("{slug}/requests/{id}")]
    [Authorize]
    public async Task<ActionResult<OrgRequestResponse>> RespondToRequest(
        string slug, int id, [FromBody] RespondToOrgRequestRequest request)
    {
        var callerId = GetCurrentUserId();
        if (callerId == null) return Unauthorized();

        var org = await GetOrgBySlug(slug);
        if (org == null) return NotFound(new { message = "Organization not found" });

        var callerRole = await GetCallerRole(org.Id);
        if (callerRole == null || callerRole < OrganizationRole.Admin)
            return Forbid();

        var orgRequest = await _context.OrgRequests
            .Include(r => r.User)
            .FirstOrDefaultAsync(r => r.Id == id && r.OrganizationId == org.Id);

        if (orgRequest == null)
            return NotFound(new { message = "Request not found." });

        if (orgRequest.Status != RequestStatus.Pending)
            return BadRequest(new { message = "This request has already been responded to." });

        orgRequest.Status = request.Accept ? RequestStatus.Accepted : RequestStatus.Declined;
        orgRequest.RespondedAt = DateTime.UtcNow;
        orgRequest.RespondedByUserId = callerId.Value;

        // Apply side effects on acceptance
        if (request.Accept)
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
                        && double.TryParse(orgRequest.RequestData, System.Globalization.NumberStyles.Any,
                            System.Globalization.CultureInfo.InvariantCulture, out var hours))
                    {
                        var membership = await _context.UserOrganizations
                            .FirstOrDefaultAsync(uo => uo.OrganizationId == org.Id
                                                    && uo.UserId == orgRequest.UserId && uo.IsActive);
                        if (membership != null)
                            membership.InitialOvertimeHours = hours;
                    }
                    break;

                // EditPastEntry and EditPause side effects would be handled
                // by the frontend/specific edit endpoints when the request is approved.
                // The approval simply grants permission.
            }
        }

        await _context.SaveChangesAsync();

        var responder = await _context.Users.FindAsync(callerId.Value);
        return Ok(MapToResponse(orgRequest, org.Name, org.Slug,
            responder != null ? $"{responder.FirstName} {responder.LastName}" : null));
    }

    // ────────────────────────────────────────────────────
    //  GET  /api/organizations/my-requests
    //  Get the current user's requests across all orgs
    // ────────────────────────────────────────────────────
    [HttpGet("my-requests")]
    [Authorize]
    public async Task<ActionResult<IEnumerable<OrgRequestResponse>>> GetMyRequests(
        [FromQuery] RequestType? type)
    {
        var userId = GetCurrentUserId();
        if (userId == null) return Unauthorized();

        var query = _context.OrgRequests
            .Include(r => r.Organization)
            .Include(r => r.RespondedByUser)
            .Where(r => r.UserId == userId.Value);

        if (type.HasValue)
            query = query.Where(r => r.Type == type.Value);

        var requests = await query
            .OrderByDescending(r => r.CreatedAt)
            .ToListAsync();

        var user = await _context.Users.FindAsync(userId.Value);
        if (user == null) return Unauthorized();

        return Ok(requests.Select(r =>
        {
            r.User = user; // attach for mapping
            return MapToResponse(r, r.Organization.Name, r.Organization.Slug);
        }));
    }

    // ────────────────────────────────────────────────────
    //  GET  /api/organizations/notifications
    //  Get pending request notifications for admin
    // ────────────────────────────────────────────────────
    [HttpGet("notifications")]
    [Authorize]
    public async Task<ActionResult<AdminNotificationResponse>> GetAdminNotifications()
    {
        var userId = GetCurrentUserId();
        if (userId == null) return Unauthorized();

        var adminOrgIds = await _context.UserOrganizations
            .Where(uo => uo.UserId == userId.Value && uo.IsActive && uo.Role >= OrganizationRole.Admin)
            .Select(uo => uo.OrganizationId)
            .ToListAsync();

        if (adminOrgIds.Count == 0)
            return Ok(new AdminNotificationResponse { PendingRequests = 0, Requests = new List<OrgRequestResponse>() });

        var pendingRequests = await _context.OrgRequests
            .Include(r => r.User)
            .Include(r => r.Organization)
            .Where(r => adminOrgIds.Contains(r.OrganizationId) && r.Status == RequestStatus.Pending)
            .OrderByDescending(r => r.CreatedAt)
            .ToListAsync();

        return Ok(new AdminNotificationResponse
        {
            PendingRequests = pendingRequests.Count,
            Requests = pendingRequests.Select(r =>
                MapToResponse(r, r.Organization.Name, r.Organization.Slug)).ToList()
        });
    }
}
