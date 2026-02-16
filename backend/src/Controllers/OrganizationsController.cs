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

    // ── Helper: resolve organization by slug ──
    private async Task<Organization?> GetOrgBySlug(string slug)
    {
        return await _context.Organizations
            .FirstOrDefaultAsync(o => o.Slug == slug && o.IsActive);
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
    //  GET  /api/organizations/{slug}
    // ────────────────────────────────────────────────────
    /// <summary>
    /// Get organization by slug with members
    /// </summary>
    [HttpGet("{slug}")]
    public async Task<ActionResult<OrganizationDetailResponse>> GetOrganization(string slug)
    {
        var organization = await _context.Organizations
            .Where(o => o.Slug == slug && o.IsActive)
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
                AllowEditPause = o.AllowEditPause,
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

        return CreatedAtAction(nameof(GetOrganization), new { slug = organization.Slug }, response);
    }

    // ────────────────────────────────────────────────────
    //  PUT  /api/organizations/{slug}
    //  Only Owner or Admin can update.
    // ────────────────────────────────────────────────────
    /// <summary>
    /// Update an organization (Owner or Admin only)
    /// </summary>
    [HttpPut("{slug}")]
    [Authorize]
    [ProducesResponseType(typeof(OrganizationResponse), StatusCodes.Status200OK)]
    public async Task<ActionResult<OrganizationResponse>> UpdateOrganization(string slug, [FromBody] UpdateOrganizationRequest request)
    {
        var organization = await _context.Organizations
            .Include(o => o.UserOrganizations)
            .FirstOrDefaultAsync(o => o.Slug == slug && o.IsActive);

        if (organization == null)
            return NotFound(new { message = "Organization not found" });

        var callerRole = await GetCallerRole(organization.Id);
        if (callerRole == null)
            return Forbid();
        if (callerRole < OrganizationRole.Admin)
            return Forbid();

        // Check slug uniqueness if changing
        if (request.Slug != null && request.Slug != organization.Slug)
        {
            if (await _context.Organizations.AnyAsync(o => o.Slug == request.Slug && o.IsActive && o.Id != organization.Id))
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
    //  DELETE  /api/organizations/{slug}
    //  Only Owner can delete (soft-delete).
    // ────────────────────────────────────────────────────
    /// <summary>
    /// Delete an organization (Owner only, soft-delete)
    /// </summary>
    [HttpDelete("{slug}")]
    [Authorize]
    [ProducesResponseType(StatusCodes.Status204NoContent)]
    public async Task<IActionResult> DeleteOrganization(string slug)
    {
        var organization = await _context.Organizations
            .Include(o => o.UserOrganizations)
            .FirstOrDefaultAsync(o => o.Slug == slug && o.IsActive);

        if (organization == null)
            return NotFound(new { message = "Organization not found" });

        var callerRole = await GetCallerRole(organization.Id);
        if (callerRole == null || callerRole != OrganizationRole.Owner)
            return Forbid();

        // Soft-delete organization and all memberships
        organization.IsActive = false;
        organization.UpdatedAt = DateTime.UtcNow;

        foreach (var membership in organization.UserOrganizations)
            membership.IsActive = false;

        await _context.SaveChangesAsync();

        return NoContent();
    }

    // ────────────────────────────────────────────────────
    //  POST  /api/organizations/{slug}/members
    //  Owner or Admin can add members.
    // ────────────────────────────────────────────────────
    /// <summary>
    /// Add a member to the organization (Owner or Admin only)
    /// </summary>
    [HttpPost("{slug}/members")]
    [Authorize]
    [ProducesResponseType(typeof(OrganizationMemberResponse), StatusCodes.Status201Created)]
    public async Task<ActionResult<OrganizationMemberResponse>> AddMember(string slug, [FromBody] AddMemberRequest request)
    {
        var org = await GetOrgBySlug(slug);
        if (org == null)
            return NotFound(new { message = "Organization not found" });

        var callerRole = await GetCallerRole(org.Id);
        if (callerRole == null || callerRole < OrganizationRole.Admin)
            return Forbid();

        // Admin cannot add someone as Owner
        if (callerRole == OrganizationRole.Admin && request.Role == OrganizationRole.Owner)
            return Forbid();

        var user = await _context.Users.FirstOrDefaultAsync(u => u.Id == request.UserId && u.IsActive);
        if (user == null)
            return NotFound(new { message = "User not found" });

        // Check if already a member
        var existing = await _context.UserOrganizations
            .FirstOrDefaultAsync(uo => uo.OrganizationId == org.Id && uo.UserId == request.UserId);

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
                OrganizationId = org.Id,
                Role = request.Role,
                JoinedAt = DateTime.UtcNow,
                IsActive = true
            };
            _context.UserOrganizations.Add(existing);
        }

        await _context.SaveChangesAsync();

        return CreatedAtAction(nameof(GetOrganization), new { slug }, new OrganizationMemberResponse
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
    //  PUT  /api/organizations/{slug}/members/{userId}
    //  Owner can change any role.
    //  Admin can change Members only (not other Admins/Owners).
    // ────────────────────────────────────────────────────
    /// <summary>
    /// Update a member's role (Owner or Admin)
    /// </summary>
    [HttpPut("{slug}/members/{userId}")]
    [Authorize]
    [ProducesResponseType(typeof(OrganizationMemberResponse), StatusCodes.Status200OK)]
    public async Task<ActionResult<OrganizationMemberResponse>> UpdateMemberRole(string slug, int userId, [FromBody] UpdateMemberRoleRequest request)
    {
        var org = await GetOrgBySlug(slug);
        if (org == null)
            return NotFound(new { message = "Organization not found" });

        var callerRole = await GetCallerRole(org.Id);
        if (callerRole == null || callerRole < OrganizationRole.Admin)
            return Forbid();

        var membership = await _context.UserOrganizations
            .Include(uo => uo.User)
            .FirstOrDefaultAsync(uo => uo.OrganizationId == org.Id && uo.UserId == userId && uo.IsActive);

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
    //  DELETE  /api/organizations/{slug}/members/{userId}
    //  Owner can remove anyone (except themselves — must delete org instead).
    //  Admin can remove Members only.
    //  Any member can remove themselves (leave).
    // ────────────────────────────────────────────────────
    /// <summary>
    /// Remove a member from the organization
    /// </summary>
    [HttpDelete("{slug}/members/{userId}")]
    [Authorize]
    [ProducesResponseType(StatusCodes.Status204NoContent)]
    public async Task<IActionResult> RemoveMember(string slug, int userId)
    {
        var callerId = GetCurrentUserId();
        if (callerId == null)
            return Unauthorized();

        var org = await GetOrgBySlug(slug);
        if (org == null)
            return NotFound(new { message = "Organization not found" });

        var callerRole = await GetCallerRole(org.Id);
        if (callerRole == null)
            return Forbid();

        var membership = await _context.UserOrganizations
            .FirstOrDefaultAsync(uo => uo.OrganizationId == org.Id && uo.UserId == userId && uo.IsActive);

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
    //  PUT  /api/organizations/{slug}/settings
    // ────────────────────────────────────────────────────
    /// <summary>
    /// Update organization settings (Admin+ only)
    /// </summary>
    [HttpPut("{slug}/settings")]
    public async Task<IActionResult> UpdateSettings(string slug, [FromBody] UpdateOrganizationSettingsRequest request)
    {
        var callerId = GetCurrentUserId();
        if (callerId == null) return Unauthorized();

        var org = await GetOrgBySlug(slug);
        if (org == null) return NotFound(new { message = "Organization not found" });

        var callerRole = await GetCallerRole(org.Id);
        if (callerRole == null || callerRole < OrganizationRole.Admin)
            return Forbid();

        if (request.AutoPauseEnabled.HasValue)
            org.AutoPauseEnabled = request.AutoPauseEnabled.Value;
        if (request.AllowEditPastEntries.HasValue)
            org.AllowEditPastEntries = request.AllowEditPastEntries.Value;
        if (request.AllowEditPause.HasValue)
            org.AllowEditPause = request.AllowEditPause.Value;

        org.UpdatedAt = DateTime.UtcNow;
        await _context.SaveChangesAsync();

        return Ok(new { org.AutoPauseEnabled, org.AllowEditPastEntries, org.AllowEditPause });
    }

    // ────────────────────────────────────────────────────
    //  GET  /api/organizations/{slug}/pause-rules
    // ────────────────────────────────────────────────────
    [HttpGet("{slug}/pause-rules")]
    public async Task<ActionResult<IEnumerable<PauseRuleResponse>>> GetPauseRules(string slug)
    {
        var org = await GetOrgBySlug(slug);
        if (org == null) return NotFound(new { message = "Organization not found" });

        var rules = await _context.PauseRules
            .Where(pr => pr.OrganizationId == org.Id)
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
    //  POST  /api/organizations/{slug}/pause-rules
    // ────────────────────────────────────────────────────
    /// <summary>
    /// Create a pause rule (Admin+ only)
    /// </summary>
    [HttpPost("{slug}/pause-rules")]
    public async Task<ActionResult<PauseRuleResponse>> CreatePauseRule(string slug, [FromBody] CreatePauseRuleRequest request)
    {
        var callerId = GetCurrentUserId();
        if (callerId == null) return Unauthorized();

        var org = await GetOrgBySlug(slug);
        if (org == null) return NotFound(new { message = "Organization not found" });

        var callerRole = await GetCallerRole(org.Id);
        if (callerRole == null || callerRole < OrganizationRole.Admin)
            return Forbid();

        if (request.MinHours <= 0)
            return BadRequest(new { message = "MinHours must be greater than 0." });
        if (request.PauseMinutes <= 0)
            return BadRequest(new { message = "PauseMinutes must be greater than 0." });

        // Check for duplicate min hours
        var exists = await _context.PauseRules
            .AnyAsync(pr => pr.OrganizationId == org.Id && Math.Abs(pr.MinHours - request.MinHours) < 0.01);
        if (exists)
            return BadRequest(new { message = "A pause rule with this threshold already exists." });

        var rule = new PauseRule
        {
            OrganizationId = org.Id,
            MinHours = request.MinHours,
            PauseMinutes = request.PauseMinutes,
            CreatedAt = DateTime.UtcNow
        };

        _context.PauseRules.Add(rule);
        await _context.SaveChangesAsync();

        return CreatedAtAction(nameof(GetPauseRules), new { slug }, new PauseRuleResponse
        {
            Id = rule.Id,
            OrganizationId = rule.OrganizationId,
            MinHours = rule.MinHours,
            PauseMinutes = rule.PauseMinutes
        });
    }

    // ────────────────────────────────────────────────────
    //  PUT  /api/organizations/{slug}/pause-rules/{ruleId}
    // ────────────────────────────────────────────────────
    [HttpPut("{slug}/pause-rules/{ruleId}")]
    public async Task<ActionResult<PauseRuleResponse>> UpdatePauseRule(string slug, int ruleId, [FromBody] UpdatePauseRuleRequest request)
    {
        var callerId = GetCurrentUserId();
        if (callerId == null) return Unauthorized();

        var org = await GetOrgBySlug(slug);
        if (org == null) return NotFound(new { message = "Organization not found" });

        var callerRole = await GetCallerRole(org.Id);
        if (callerRole == null || callerRole < OrganizationRole.Admin)
            return Forbid();

        var rule = await _context.PauseRules
            .FirstOrDefaultAsync(pr => pr.Id == ruleId && pr.OrganizationId == org.Id);

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
    //  DELETE  /api/organizations/{slug}/pause-rules/{ruleId}
    // ────────────────────────────────────────────────────
    [HttpDelete("{slug}/pause-rules/{ruleId}")]
    public async Task<IActionResult> DeletePauseRule(string slug, int ruleId)
    {
        var callerId = GetCurrentUserId();
        if (callerId == null) return Unauthorized();

        var org = await GetOrgBySlug(slug);
        if (org == null) return NotFound(new { message = "Organization not found" });

        var callerRole = await GetCallerRole(org.Id);
        if (callerRole == null || callerRole < OrganizationRole.Admin)
            return Forbid();

        var rule = await _context.PauseRules
            .FirstOrDefaultAsync(pr => pr.Id == ruleId && pr.OrganizationId == org.Id);

        if (rule == null)
            return NotFound(new { message = "Pause rule not found." });

        _context.PauseRules.Remove(rule);
        await _context.SaveChangesAsync();

        return NoContent();
    }

    // ────────────────────────────────────────────────────
    //  GET  /api/organizations/{slug}/work-schedule
    //  Get the current user's work schedule for this org
    // ────────────────────────────────────────────────────
    [HttpGet("{slug}/work-schedule")]
    [Authorize]
    public async Task<ActionResult<WorkScheduleResponse>> GetMyWorkSchedule(string slug)
    {
        var userId = GetCurrentUserId();
        if (userId == null) return Unauthorized();

        var org = await GetOrgBySlug(slug);
        if (org == null) return NotFound(new { message = "Organization not found" });

        var membership = await _context.UserOrganizations
            .FirstOrDefaultAsync(uo => uo.OrganizationId == org.Id && uo.UserId == userId.Value && uo.IsActive);

        if (membership == null)
            return NotFound(new { message = "You are not a member of this organization." });

        return Ok(new WorkScheduleResponse
        {
            UserId = userId.Value,
            OrganizationId = org.Id,
            WeeklyWorkHours = membership.WeeklyWorkHours,
            TargetMon = membership.TargetMon,
            TargetTue = membership.TargetTue,
            TargetWed = membership.TargetWed,
            TargetThu = membership.TargetThu,
            TargetFri = membership.TargetFri
        });
    }

    // ────────────────────────────────────────────────────
    //  PUT  /api/organizations/{slug}/work-schedule
    //  Update the current user's work schedule
    // ────────────────────────────────────────────────────
    [HttpPut("{slug}/work-schedule")]
    [Authorize]
    public async Task<ActionResult<WorkScheduleResponse>> UpdateMyWorkSchedule(string slug, [FromBody] UpdateWorkScheduleRequest request)
    {
        var userId = GetCurrentUserId();
        if (userId == null) return Unauthorized();

        var org = await GetOrgBySlug(slug);
        if (org == null) return NotFound(new { message = "Organization not found" });

        var membership = await _context.UserOrganizations
            .FirstOrDefaultAsync(uo => uo.OrganizationId == org.Id && uo.UserId == userId.Value && uo.IsActive);

        if (membership == null)
            return NotFound(new { message = "You are not a member of this organization." });

        if (request.WeeklyWorkHours.HasValue)
        {
            membership.WeeklyWorkHours = request.WeeklyWorkHours.Value;

            if (request.DistributeEvenly)
            {
                // Distribute equally across Mon-Fri
                var daily = Math.Round(request.WeeklyWorkHours.Value / 5.0, 2);
                membership.TargetMon = daily;
                membership.TargetTue = daily;
                membership.TargetWed = daily;
                membership.TargetThu = daily;
                membership.TargetFri = daily;
            }
            else
            {
                // Use custom per-day targets
                membership.TargetMon = request.TargetMon ?? membership.TargetMon;
                membership.TargetTue = request.TargetTue ?? membership.TargetTue;
                membership.TargetWed = request.TargetWed ?? membership.TargetWed;
                membership.TargetThu = request.TargetThu ?? membership.TargetThu;
                membership.TargetFri = request.TargetFri ?? membership.TargetFri;
            }
        }
        else if (!request.DistributeEvenly)
        {
            // Just updating individual day targets without changing weekly total
            if (request.TargetMon.HasValue) membership.TargetMon = request.TargetMon.Value;
            if (request.TargetTue.HasValue) membership.TargetTue = request.TargetTue.Value;
            if (request.TargetWed.HasValue) membership.TargetWed = request.TargetWed.Value;
            if (request.TargetThu.HasValue) membership.TargetThu = request.TargetThu.Value;
            if (request.TargetFri.HasValue) membership.TargetFri = request.TargetFri.Value;
        }

        await _context.SaveChangesAsync();

        return Ok(new WorkScheduleResponse
        {
            UserId = userId.Value,
            OrganizationId = org.Id,
            WeeklyWorkHours = membership.WeeklyWorkHours,
            TargetMon = membership.TargetMon,
            TargetTue = membership.TargetTue,
            TargetWed = membership.TargetWed,
            TargetThu = membership.TargetThu,
            TargetFri = membership.TargetFri
        });
    }

    // ────────────────────────────────────────────────────
    //  GET  /api/organizations/{slug}/time-overview
    //  Admin+: Get time tracked by all members in a date range
    // ────────────────────────────────────────────────────
    [HttpGet("{slug}/time-overview")]
    [Authorize]
    public async Task<ActionResult<IEnumerable<MemberTimeOverviewResponse>>> GetTimeOverview(
        string slug, [FromQuery] DateTime? from, [FromQuery] DateTime? to)
    {
        var callerId = GetCurrentUserId();
        if (callerId == null) return Unauthorized();

        var org = await GetOrgBySlug(slug);
        if (org == null) return NotFound(new { message = "Organization not found" });

        var callerRole = await GetCallerRole(org.Id);
        if (callerRole == null || callerRole < OrganizationRole.Admin)
            return Forbid();

        // Default to current week (Mon-Sun)
        var now = DateTime.UtcNow;
        var dayOfWeek = (int)now.DayOfWeek;
        if (dayOfWeek == 0) dayOfWeek = 7;
        var weekStart = from ?? now.Date.AddDays(-(dayOfWeek - 1));
        var weekEnd = to ?? weekStart.AddDays(7);

        var members = await _context.UserOrganizations
            .Where(uo => uo.OrganizationId == org.Id && uo.IsActive)
            .Include(uo => uo.User)
            .ToListAsync();

        var memberIds = members.Select(m => m.UserId).ToList();

        var timeEntries = await _context.TimeEntries
            .Where(te => memberIds.Contains(te.UserId)
                      && te.OrganizationId == org.Id
                      && !te.IsRunning
                      && te.StartTime >= weekStart
                      && te.StartTime < weekEnd)
            .ToListAsync();

        var result = members.Select(m =>
        {
            var entries = timeEntries.Where(te => te.UserId == m.UserId).ToList();
            var totalMinutes = entries.Sum(e =>
                e.EndTime.HasValue ? (e.EndTime.Value - e.StartTime).TotalMinutes : 0);
            var totalPause = entries.Sum(e => e.PauseDurationMinutes);

            return new MemberTimeOverviewResponse
            {
                UserId = m.UserId,
                FirstName = m.User.FirstName,
                LastName = m.User.LastName,
                Email = m.User.Email,
                Role = m.Role.ToString(),
                WeeklyWorkHours = m.WeeklyWorkHours,
                TotalTrackedMinutes = Math.Round(totalMinutes, 1),
                NetTrackedMinutes = Math.Round(totalMinutes - totalPause, 1),
                EntryCount = entries.Count
            };
        }).ToList();

        return Ok(result);
    }

    // ────────────────────────────────────────────────────
    //  GET  /api/organizations/{slug}/member-entries/{userId}
    //  Admin+: Get detailed time entries for a specific member
    // ────────────────────────────────────────────────────
    [HttpGet("{slug}/member-entries/{userId}")]
    [Authorize]
    public async Task<ActionResult<IEnumerable<object>>> GetMemberEntries(
        string slug, int userId, [FromQuery] DateTime? from, [FromQuery] DateTime? to)
    {
        var callerId = GetCurrentUserId();
        if (callerId == null) return Unauthorized();

        var org = await GetOrgBySlug(slug);
        if (org == null) return NotFound(new { message = "Organization not found" });

        var callerRole = await GetCallerRole(org.Id);
        if (callerRole == null || callerRole < OrganizationRole.Admin)
            return Forbid();

        var now = DateTime.UtcNow;
        var dayOfWeek = (int)now.DayOfWeek;
        if (dayOfWeek == 0) dayOfWeek = 7;
        var weekStart = from ?? now.Date.AddDays(-(dayOfWeek - 1));
        var weekEnd = to ?? weekStart.AddDays(7);

        var entries = await _context.TimeEntries
            .Where(te => te.UserId == userId
                      && te.OrganizationId == org.Id
                      && te.StartTime >= weekStart
                      && te.StartTime < weekEnd)
            .OrderByDescending(te => te.StartTime)
            .Select(te => new
            {
                te.Id,
                te.UserId,
                te.OrganizationId,
                te.Description,
                te.StartTime,
                te.EndTime,
                te.IsRunning,
                DurationMinutes = te.EndTime.HasValue
                    ? Math.Round((te.EndTime.Value - te.StartTime).TotalMinutes, 1)
                    : (double?)null,
                te.PauseDurationMinutes,
                NetDurationMinutes = te.EndTime.HasValue
                    ? Math.Round((te.EndTime.Value - te.StartTime).TotalMinutes - te.PauseDurationMinutes, 1)
                    : (double?)null,
                te.CreatedAt
            })
            .ToListAsync();

        return Ok(entries);
    }
}
