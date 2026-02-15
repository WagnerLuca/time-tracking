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
public class OrganizationsController : ControllerBase
{
    private readonly TimeTrackingDbContext _context;

    public OrganizationsController(TimeTrackingDbContext context)
    {
        _context = context;
    }

    // ── Helper: get current user ID from JWT claims ──
    private int? GetCurrentUserId()
    {
        var claim = User.FindFirst(ClaimTypes.NameIdentifier);
        return claim != null && int.TryParse(claim.Value, out var id) ? id : null;
    }

    // ── Helper: get caller's role inside an organization ──
    private async Task<OrganizationRole?> GetCallerRole(int organizationId)
    {
        var userId = GetCurrentUserId();
        if (userId == null) return null;

        var membership = await _context.UserOrganizations
            .FirstOrDefaultAsync(uo => uo.OrganizationId == organizationId
                                    && uo.UserId == userId.Value
                                    && uo.IsActive);
        return membership?.Role;
    }

    // ────────────────────────────────────────────────────
    //  GET  /api/organizations
    // ────────────────────────────────────────────────────
    /// <summary>
    /// Get all organizations
    /// </summary>
    [HttpGet]
    public async Task<ActionResult<IEnumerable<OrganizationResponse>>> GetOrganizations()
    {
        var organizations = await _context.Organizations
            .Where(o => o.IsActive)
            .Select(o => new OrganizationResponse
            {
                Id = o.Id,
                Name = o.Name,
                Description = o.Description,
                Slug = o.Slug,
                Website = o.Website,
                LogoUrl = o.LogoUrl,
                CreatedAt = o.CreatedAt,
                MemberCount = o.UserOrganizations.Count(uo => uo.IsActive)
            })
            .ToListAsync();

        return Ok(organizations);
    }

    // ────────────────────────────────────────────────────
    //  GET  /api/organizations/{id}
    // ────────────────────────────────────────────────────
    /// <summary>
    /// Get organization by ID with members
    /// </summary>
    [HttpGet("{id}")]
    public async Task<ActionResult<OrganizationDetailResponse>> GetOrganization(int id)
    {
        var organization = await _context.Organizations
            .Where(o => o.Id == id && o.IsActive)
            .Select(o => new OrganizationDetailResponse
            {
                Id = o.Id,
                Name = o.Name,
                Description = o.Description,
                Slug = o.Slug,
                Website = o.Website,
                LogoUrl = o.LogoUrl,
                AutoPauseEnabled = o.AutoPauseEnabled,
                AllowEditPastEntries = o.AllowEditPastEntries,
                CreatedAt = o.CreatedAt,
                Members = o.UserOrganizations
                    .Where(uo => uo.IsActive)
                    .Select(uo => new OrganizationMemberResponse
                    {
                        Id = uo.User.Id,
                        Email = uo.User.Email,
                        FirstName = uo.User.FirstName,
                        LastName = uo.User.LastName,
                        ProfileImageUrl = uo.User.ProfileImageUrl,
                        Role = uo.Role.ToString(),
                        JoinedAt = uo.JoinedAt
                    })
                    .ToList(),
                PauseRules = o.PauseRules
                    .OrderBy(pr => pr.MinHours)
                    .Select(pr => new PauseRuleResponse
                    {
                        Id = pr.Id,
                        OrganizationId = pr.OrganizationId,
                        MinHours = pr.MinHours,
                        PauseMinutes = pr.PauseMinutes
                    })
                    .ToList()
            })
            .FirstOrDefaultAsync();

        if (organization == null)
            return NotFound(new { message = "Organization not found" });

        return Ok(organization);
    }

    // ────────────────────────────────────────────────────
    //  GET  /api/organizations/user/{userId}
    // ────────────────────────────────────────────────────
    /// <summary>
    /// Get user's organizations
    /// </summary>
    [HttpGet("user/{userId}")]
    public async Task<ActionResult<IEnumerable<UserOrganizationResponse>>> GetUserOrganizations(int userId)
    {
        var userExists = await _context.Users.AnyAsync(u => u.Id == userId && u.IsActive);
        if (!userExists)
            return NotFound(new { message = "User not found" });

        var organizations = await _context.UserOrganizations
            .Where(uo => uo.UserId == userId && uo.IsActive)
            .Select(uo => new UserOrganizationResponse
            {
                OrganizationId = uo.Organization.Id,
                Name = uo.Organization.Name,
                Description = uo.Organization.Description,
                Slug = uo.Organization.Slug,
                Role = uo.Role.ToString(),
                JoinedAt = uo.JoinedAt,
                MemberCount = uo.Organization.UserOrganizations.Count(x => x.IsActive)
            })
            .ToListAsync();

        return Ok(organizations);
    }

    // ────────────────────────────────────────────────────
    //  POST  /api/organizations
    //  Any authenticated user can create an organization.
    //  The creator becomes the Owner.
    // ────────────────────────────────────────────────────
    /// <summary>
    /// Create a new organization (authenticated user becomes Owner)
    /// </summary>
    [HttpPost]
    [Authorize]
    [ProducesResponseType(typeof(OrganizationResponse), StatusCodes.Status201Created)]
    public async Task<ActionResult<OrganizationResponse>> CreateOrganization([FromBody] CreateOrganizationRequest request)
    {
        var userId = GetCurrentUserId();
        if (userId == null)
            return Unauthorized(new { message = "Invalid user" });

        // Check slug uniqueness
        if (await _context.Organizations.AnyAsync(o => o.Slug == request.Slug && o.IsActive))
            return Conflict(new { message = "An organization with this slug already exists" });

        var organization = new Organization
        {
            Name = request.Name,
            Description = request.Description,
            Slug = request.Slug,
            Website = request.Website,
            LogoUrl = request.LogoUrl,
            CreatedAt = DateTime.UtcNow,
            UpdatedAt = DateTime.UtcNow,
            IsActive = true
        };

        _context.Organizations.Add(organization);
        await _context.SaveChangesAsync();

        // Add creator as Owner
        _context.UserOrganizations.Add(new UserOrganization
        {
            UserId = userId.Value,
            OrganizationId = organization.Id,
            Role = OrganizationRole.Owner,
            JoinedAt = DateTime.UtcNow,
            IsActive = true
        });
        await _context.SaveChangesAsync();

        var response = new OrganizationResponse
        {
            Id = organization.Id,
            Name = organization.Name,
            Description = organization.Description,
            Slug = organization.Slug,
            Website = organization.Website,
            LogoUrl = organization.LogoUrl,
            CreatedAt = organization.CreatedAt,
            MemberCount = 1
        };

        return CreatedAtAction(nameof(GetOrganization), new { id = organization.Id }, response);
    }

    // ────────────────────────────────────────────────────
    //  PUT  /api/organizations/{id}
    //  Only Owner or Admin can update.
    // ────────────────────────────────────────────────────
    /// <summary>
    /// Update an organization (Owner or Admin only)
    /// </summary>
    [HttpPut("{id}")]
    [Authorize]
    [ProducesResponseType(typeof(OrganizationResponse), StatusCodes.Status200OK)]
    public async Task<ActionResult<OrganizationResponse>> UpdateOrganization(int id, [FromBody] UpdateOrganizationRequest request)
    {
        var callerRole = await GetCallerRole(id);
        if (callerRole == null)
            return Forbid();
        if (callerRole < OrganizationRole.Admin)
            return Forbid();

        var organization = await _context.Organizations
            .Include(o => o.UserOrganizations)
            .FirstOrDefaultAsync(o => o.Id == id && o.IsActive);

        if (organization == null)
            return NotFound(new { message = "Organization not found" });

        // Check slug uniqueness if changing
        if (request.Slug != null && request.Slug != organization.Slug)
        {
            if (await _context.Organizations.AnyAsync(o => o.Slug == request.Slug && o.IsActive && o.Id != id))
                return Conflict(new { message = "An organization with this slug already exists" });
        }

        if (request.Name != null) organization.Name = request.Name;
        if (request.Description != null) organization.Description = request.Description;
        if (request.Slug != null) organization.Slug = request.Slug;
        if (request.Website != null) organization.Website = request.Website;
        if (request.LogoUrl != null) organization.LogoUrl = request.LogoUrl;
        organization.UpdatedAt = DateTime.UtcNow;

        await _context.SaveChangesAsync();

        return Ok(new OrganizationResponse
        {
            Id = organization.Id,
            Name = organization.Name,
            Description = organization.Description,
            Slug = organization.Slug,
            Website = organization.Website,
            LogoUrl = organization.LogoUrl,
            CreatedAt = organization.CreatedAt,
            MemberCount = organization.UserOrganizations.Count(uo => uo.IsActive)
        });
    }

    // ────────────────────────────────────────────────────
    //  DELETE  /api/organizations/{id}
    //  Only Owner can delete (soft-delete).
    // ────────────────────────────────────────────────────
    /// <summary>
    /// Delete an organization (Owner only, soft-delete)
    /// </summary>
    [HttpDelete("{id}")]
    [Authorize]
    [ProducesResponseType(StatusCodes.Status204NoContent)]
    public async Task<IActionResult> DeleteOrganization(int id)
    {
        var callerRole = await GetCallerRole(id);
        if (callerRole == null || callerRole != OrganizationRole.Owner)
            return Forbid();

        var organization = await _context.Organizations
            .Include(o => o.UserOrganizations)
            .FirstOrDefaultAsync(o => o.Id == id && o.IsActive);

        if (organization == null)
            return NotFound(new { message = "Organization not found" });

        // Soft-delete organization and all memberships
        organization.IsActive = false;
        organization.UpdatedAt = DateTime.UtcNow;

        foreach (var membership in organization.UserOrganizations)
            membership.IsActive = false;

        await _context.SaveChangesAsync();

        return NoContent();
    }

    // ────────────────────────────────────────────────────
    //  POST  /api/organizations/{id}/members
    //  Owner or Admin can add members.
    // ────────────────────────────────────────────────────
    /// <summary>
    /// Add a member to the organization (Owner or Admin only)
    /// </summary>
    [HttpPost("{id}/members")]
    [Authorize]
    [ProducesResponseType(typeof(OrganizationMemberResponse), StatusCodes.Status201Created)]
    public async Task<ActionResult<OrganizationMemberResponse>> AddMember(int id, [FromBody] AddMemberRequest request)
    {
        var callerRole = await GetCallerRole(id);
        if (callerRole == null || callerRole < OrganizationRole.Admin)
            return Forbid();

        // Admin cannot add someone as Owner
        if (callerRole == OrganizationRole.Admin && request.Role == OrganizationRole.Owner)
            return Forbid();

        var orgExists = await _context.Organizations.AnyAsync(o => o.Id == id && o.IsActive);
        if (!orgExists)
            return NotFound(new { message = "Organization not found" });

        var user = await _context.Users.FirstOrDefaultAsync(u => u.Id == request.UserId && u.IsActive);
        if (user == null)
            return NotFound(new { message = "User not found" });

        // Check if already a member
        var existing = await _context.UserOrganizations
            .FirstOrDefaultAsync(uo => uo.OrganizationId == id && uo.UserId == request.UserId);

        if (existing != null)
        {
            if (existing.IsActive)
                return Conflict(new { message = "User is already a member of this organization" });

            // Re-activate previously removed member
            existing.IsActive = true;
            existing.Role = request.Role;
            existing.JoinedAt = DateTime.UtcNow;
        }
        else
        {
            existing = new UserOrganization
            {
                UserId = request.UserId,
                OrganizationId = id,
                Role = request.Role,
                JoinedAt = DateTime.UtcNow,
                IsActive = true
            };
            _context.UserOrganizations.Add(existing);
        }

        await _context.SaveChangesAsync();

        return CreatedAtAction(nameof(GetOrganization), new { id }, new OrganizationMemberResponse
        {
            Id = user.Id,
            Email = user.Email,
            FirstName = user.FirstName,
            LastName = user.LastName,
            ProfileImageUrl = user.ProfileImageUrl,
            Role = existing.Role.ToString(),
            JoinedAt = existing.JoinedAt
        });
    }

    // ────────────────────────────────────────────────────
    //  PUT  /api/organizations/{id}/members/{userId}
    //  Owner can change any role.
    //  Admin can change Members only (not other Admins/Owners).
    // ────────────────────────────────────────────────────
    /// <summary>
    /// Update a member's role (Owner or Admin)
    /// </summary>
    [HttpPut("{id}/members/{userId}")]
    [Authorize]
    [ProducesResponseType(typeof(OrganizationMemberResponse), StatusCodes.Status200OK)]
    public async Task<ActionResult<OrganizationMemberResponse>> UpdateMemberRole(int id, int userId, [FromBody] UpdateMemberRoleRequest request)
    {
        var callerRole = await GetCallerRole(id);
        if (callerRole == null || callerRole < OrganizationRole.Admin)
            return Forbid();

        var membership = await _context.UserOrganizations
            .Include(uo => uo.User)
            .FirstOrDefaultAsync(uo => uo.OrganizationId == id && uo.UserId == userId && uo.IsActive);

        if (membership == null)
            return NotFound(new { message = "Member not found in this organization" });

        // Cannot change Owner's role (only another Owner could, but there should be exactly one)
        if (membership.Role == OrganizationRole.Owner)
            return BadRequest(new { message = "Cannot change the Owner's role. Transfer ownership first." });

        // Admin cannot promote to Owner or change another Admin
        if (callerRole == OrganizationRole.Admin)
        {
            if (request.Role == OrganizationRole.Owner)
                return Forbid();
            if (membership.Role == OrganizationRole.Admin)
                return Forbid();
        }

        membership.Role = request.Role;
        await _context.SaveChangesAsync();

        return Ok(new OrganizationMemberResponse
        {
            Id = membership.User.Id,
            Email = membership.User.Email,
            FirstName = membership.User.FirstName,
            LastName = membership.User.LastName,
            ProfileImageUrl = membership.User.ProfileImageUrl,
            Role = membership.Role.ToString(),
            JoinedAt = membership.JoinedAt
        });
    }

    // ────────────────────────────────────────────────────
    //  DELETE  /api/organizations/{id}/members/{userId}
    //  Owner can remove anyone (except themselves — must delete org instead).
    //  Admin can remove Members only.
    //  Any member can remove themselves (leave).
    // ────────────────────────────────────────────────────
    /// <summary>
    /// Remove a member from the organization
    /// </summary>
    [HttpDelete("{id}/members/{userId}")]
    [Authorize]
    [ProducesResponseType(StatusCodes.Status204NoContent)]
    public async Task<IActionResult> RemoveMember(int id, int userId)
    {
        var callerId = GetCurrentUserId();
        if (callerId == null)
            return Unauthorized();

        var callerRole = await GetCallerRole(id);
        if (callerRole == null)
            return Forbid();

        var membership = await _context.UserOrganizations
            .FirstOrDefaultAsync(uo => uo.OrganizationId == id && uo.UserId == userId && uo.IsActive);

        if (membership == null)
            return NotFound(new { message = "Member not found in this organization" });

        bool isSelf = callerId.Value == userId;

        // Owner cannot remove themselves (delete the org instead)
        if (isSelf && membership.Role == OrganizationRole.Owner)
            return BadRequest(new { message = "Owner cannot leave. Delete the organization or transfer ownership first." });

        // Non-self removal requires Admin+ privileges
        if (!isSelf)
        {
            if (callerRole < OrganizationRole.Admin)
                return Forbid();

            // Admin cannot remove another Admin or Owner
            if (callerRole == OrganizationRole.Admin && membership.Role >= OrganizationRole.Admin)
                return Forbid();
        }

        membership.IsActive = false;
        await _context.SaveChangesAsync();

        return NoContent();
    }

    // ────────────────────────────────────────────────────
    //  PUT  /api/organizations/{id}/settings
    // ────────────────────────────────────────────────────
    /// <summary>
    /// Update organization settings (Admin+ only)
    /// </summary>
    [HttpPut("{id}/settings")]
    public async Task<IActionResult> UpdateSettings(int id, [FromBody] UpdateOrganizationSettingsRequest request)
    {
        var callerId = GetCurrentUserId();
        if (callerId == null) return Unauthorized();

        var callerRole = await GetCallerRole(id);
        if (callerRole == null || callerRole < OrganizationRole.Admin)
            return Forbid();

        var org = await _context.Organizations.FirstOrDefaultAsync(o => o.Id == id && o.IsActive);
        if (org == null) return NotFound(new { message = "Organization not found" });

        if (request.AutoPauseEnabled.HasValue)
            org.AutoPauseEnabled = request.AutoPauseEnabled.Value;
        if (request.AllowEditPastEntries.HasValue)
            org.AllowEditPastEntries = request.AllowEditPastEntries.Value;

        org.UpdatedAt = DateTime.UtcNow;
        await _context.SaveChangesAsync();

        return Ok(new { org.AutoPauseEnabled, org.AllowEditPastEntries });
    }

    // ────────────────────────────────────────────────────
    //  GET  /api/organizations/{id}/pause-rules
    // ────────────────────────────────────────────────────
    [HttpGet("{id}/pause-rules")]
    public async Task<ActionResult<IEnumerable<PauseRuleResponse>>> GetPauseRules(int id)
    {
        var rules = await _context.PauseRules
            .Where(pr => pr.OrganizationId == id)
            .OrderBy(pr => pr.MinHours)
            .Select(pr => new PauseRuleResponse
            {
                Id = pr.Id,
                OrganizationId = pr.OrganizationId,
                MinHours = pr.MinHours,
                PauseMinutes = pr.PauseMinutes
            })
            .ToListAsync();

        return Ok(rules);
    }

    // ────────────────────────────────────────────────────
    //  POST  /api/organizations/{id}/pause-rules
    // ────────────────────────────────────────────────────
    /// <summary>
    /// Create a pause rule (Admin+ only)
    /// </summary>
    [HttpPost("{id}/pause-rules")]
    public async Task<ActionResult<PauseRuleResponse>> CreatePauseRule(int id, [FromBody] CreatePauseRuleRequest request)
    {
        var callerId = GetCurrentUserId();
        if (callerId == null) return Unauthorized();

        var callerRole = await GetCallerRole(id);
        if (callerRole == null || callerRole < OrganizationRole.Admin)
            return Forbid();

        if (request.MinHours <= 0)
            return BadRequest(new { message = "MinHours must be greater than 0." });
        if (request.PauseMinutes <= 0)
            return BadRequest(new { message = "PauseMinutes must be greater than 0." });

        // Check for duplicate min hours
        var exists = await _context.PauseRules
            .AnyAsync(pr => pr.OrganizationId == id && Math.Abs(pr.MinHours - request.MinHours) < 0.01);
        if (exists)
            return BadRequest(new { message = "A pause rule with this threshold already exists." });

        var rule = new PauseRule
        {
            OrganizationId = id,
            MinHours = request.MinHours,
            PauseMinutes = request.PauseMinutes,
            CreatedAt = DateTime.UtcNow
        };

        _context.PauseRules.Add(rule);
        await _context.SaveChangesAsync();

        return CreatedAtAction(nameof(GetPauseRules), new { id }, new PauseRuleResponse
        {
            Id = rule.Id,
            OrganizationId = rule.OrganizationId,
            MinHours = rule.MinHours,
            PauseMinutes = rule.PauseMinutes
        });
    }

    // ────────────────────────────────────────────────────
    //  PUT  /api/organizations/{id}/pause-rules/{ruleId}
    // ────────────────────────────────────────────────────
    [HttpPut("{id}/pause-rules/{ruleId}")]
    public async Task<ActionResult<PauseRuleResponse>> UpdatePauseRule(int id, int ruleId, [FromBody] UpdatePauseRuleRequest request)
    {
        var callerId = GetCurrentUserId();
        if (callerId == null) return Unauthorized();

        var callerRole = await GetCallerRole(id);
        if (callerRole == null || callerRole < OrganizationRole.Admin)
            return Forbid();

        var rule = await _context.PauseRules
            .FirstOrDefaultAsync(pr => pr.Id == ruleId && pr.OrganizationId == id);

        if (rule == null)
            return NotFound(new { message = "Pause rule not found." });

        rule.MinHours = request.MinHours;
        rule.PauseMinutes = request.PauseMinutes;
        await _context.SaveChangesAsync();

        return Ok(new PauseRuleResponse
        {
            Id = rule.Id,
            OrganizationId = rule.OrganizationId,
            MinHours = rule.MinHours,
            PauseMinutes = rule.PauseMinutes
        });
    }

    // ────────────────────────────────────────────────────
    //  DELETE  /api/organizations/{id}/pause-rules/{ruleId}
    // ────────────────────────────────────────────────────
    [HttpDelete("{id}/pause-rules/{ruleId}")]
    public async Task<IActionResult> DeletePauseRule(int id, int ruleId)
    {
        var callerId = GetCurrentUserId();
        if (callerId == null) return Unauthorized();

        var callerRole = await GetCallerRole(id);
        if (callerRole == null || callerRole < OrganizationRole.Admin)
            return Forbid();

        var rule = await _context.PauseRules
            .FirstOrDefaultAsync(pr => pr.Id == ruleId && pr.OrganizationId == id);

        if (rule == null)
            return NotFound(new { message = "Pause rule not found." });

        _context.PauseRules.Remove(rule);
        await _context.SaveChangesAsync();

        return NoContent();
    }
}
