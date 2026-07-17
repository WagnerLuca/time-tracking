using Microsoft.EntityFrameworkCore;
using TimeTracking.Api.Data;
using TimeTracking.Api.Models;
using TimeTracking.Api.Models.Dtos;

namespace TimeTracking.Api.Services;

public class AbsenceDayService : IAbsenceDayService
{
    private readonly TimeTrackingDbContext _context;
    private readonly ILogger<AbsenceDayService> _logger;

    public AbsenceDayService(TimeTrackingDbContext context, ILogger<AbsenceDayService> logger)
    {
        _context = context;
        _logger = logger;
    }

    public async Task<ServiceResult<PaginatedResponse<AbsenceDayResponse>>> GetAbsencesAsync(
        string slug, int callerUserId, int? filterUserId, DateOnly? from, DateOnly? to, int limit, int offset)
    {
        var org = await GetOrgBySlugAsync(slug);
        if (org == null)
            return ServiceResult.NotFound<PaginatedResponse<AbsenceDayResponse>>("Organization not found");

        var callerRole = await GetRoleAsync(callerUserId, org.Id);
        if (callerRole == null)
            return ServiceResult.Forbidden<PaginatedResponse<AbsenceDayResponse>>();

        (limit, offset) = PaginationDefaults.Normalize(limit, offset);

        var query = _context.AbsenceDays
            .Include(a => a.User)
            .Where(a => a.OrganizationId == org.Id);

        // Admins always see all absences; visibility settings only restrict regular members
        bool isAdmin = callerRole >= OrganizationRole.Admin;

        if (isAdmin)
        {
            // Admin/Owner — full access, only filter by user if requested
            if (filterUserId.HasValue)
                query = query.Where(a => a.UserId == filterUserId.Value);
        }
        else if (filterUserId.HasValue && filterUserId.Value == callerUserId)
        {
            // Viewing own absences — always allowed
            query = query.Where(a => a.UserId == callerUserId);
        }
        else
        {
            // Non-admin viewing others or all — resolve group-aware visibility
            var groupAdminMemberIds = await GetGroupAdminAbsenceVisibleMemberIdsAsync(callerUserId, org.Id);
            bool isGroupAdmin = groupAdminMemberIds.Count > 0;

            // Get group member IDs for visibility (group members who share a group with the caller)
            var groupMemberIds = await GetGroupMemberVisibleUserIdsAsync(callerUserId, org.Id);

            // Effective visibility: org-level baseline, but GroupAdmins with AdminOnly also see their group members
            bool canSeeVacation = org.VacationVisibility >= VisibilityMode.AllMembers;
            bool canSeeSick = org.SickDayVisibility >= VisibilityMode.AllMembers;
            bool groupAdminCanSeeVacation = isGroupAdmin && org.VacationVisibility >= VisibilityMode.AdminOnly;
            bool groupAdminCanSeeSick = isGroupAdmin && org.SickDayVisibility >= VisibilityMode.AdminOnly;

            // Check group-level visibility overrides for regular members
            bool groupMemberCanSeeVacation = groupMemberIds.vacationVisible.Count > 0;
            bool groupMemberCanSeeSick = groupMemberIds.sickVisible.Count > 0;

            if (filterUserId.HasValue)
            {
                // Viewing a specific other member's absences
                bool isGroupAdminMember = groupAdminMemberIds.Contains(filterUserId.Value);
                bool isVacationVisible = canSeeVacation || (groupAdminCanSeeVacation && isGroupAdminMember) || groupMemberIds.vacationVisible.Contains(filterUserId.Value);
                bool isSickVisible = canSeeSick || (groupAdminCanSeeSick && isGroupAdminMember) || groupMemberIds.sickVisible.Contains(filterUserId.Value);

                query = query.Where(a => a.UserId == filterUserId.Value);
                if (!isVacationVisible && !isSickVisible)
                    query = query.Where(a => a.UserId == callerUserId); // fallback to own
                else if (!isVacationVisible)
                    query = query.Where(a => a.Type != AbsenceType.Vacation);
                else if (!isSickVisible)
                    query = query.Where(a => a.Type != AbsenceType.SickDay);
            }
            else
            {
                // No filter — list all visible absences
                if (!canSeeVacation && !canSeeSick && !groupAdminCanSeeVacation && !groupAdminCanSeeSick && !groupMemberCanSeeVacation && !groupMemberCanSeeSick)
                {
                    query = query.Where(a => a.UserId == callerUserId);
                }
                else
                {
                    query = query.Where(a =>
                        a.UserId == callerUserId
                        // All members can see these types (org-level AllMembers)
                        || (canSeeVacation && a.Type == AbsenceType.Vacation)
                        || (canSeeSick && a.Type == AbsenceType.SickDay)
                        || (canSeeVacation && canSeeSick && a.Type == AbsenceType.Other)
                        // Group admin can see their group members' absences (AdminOnly)
                        || (groupAdminCanSeeVacation && groupAdminMemberIds.Contains(a.UserId) && a.Type == AbsenceType.Vacation)
                        || (groupAdminCanSeeSick && groupAdminMemberIds.Contains(a.UserId) && a.Type == AbsenceType.SickDay)
                        // Group members can see fellow group members' absences (group-level AllMembers)
                        || (groupMemberCanSeeVacation && groupMemberIds.vacationVisible.Contains(a.UserId) && a.Type == AbsenceType.Vacation)
                        || (groupMemberCanSeeSick && groupMemberIds.sickVisible.Contains(a.UserId) && a.Type == AbsenceType.SickDay)
                    );
                }
            }
        }

        if (from.HasValue) query = query.Where(a => a.Date >= from.Value);
        if (to.HasValue)   query = query.Where(a => a.Date <= to.Value);

        var totalCount = await query.CountAsync();

        var absences = await query
            .OrderBy(a => a.Date)
            .Skip(offset)
            .Take(limit)
            .Select(a => new AbsenceDayResponse
            {
                Id = a.Id,
                UserId = a.UserId,
                OrganizationId = a.OrganizationId,
                Date = a.Date,
                Type = a.Type.ToString(),
                IsHalfDay = a.IsHalfDay,
                Note = a.Note,
                UserFirstName = a.User.FirstName,
                UserLastName = a.User.LastName,
                CustomTypeId = a.CustomTypeId,
                CustomTypeName = a.CustomType != null ? a.CustomType.Name : null,
                CustomTypeColor = a.CustomType != null ? a.CustomType.Color : null,
                CreditMode = a.CustomType != null ? a.CustomType.CreditMode.ToString() : null,
                CreatedAt = a.CreatedAt,
                CreatedByUserId = a.CreatedByUserId,
                CreatedByName = _context.Users
                    .Where(u => u.Id == a.CreatedByUserId)
                    .Select(u => u.FirstName + " " + u.LastName)
                    .FirstOrDefault(),
                CreatedViaRequestId = a.CreatedViaRequestId
            })
            .ToListAsync();

        return ServiceResult.Ok(new PaginatedResponse<AbsenceDayResponse>
        {
            Items = absences,
            TotalCount = totalCount,
            Limit = limit,
            Offset = offset
        });
    }

    public async Task<ServiceResult<AbsenceDayResponse>> CreateAbsenceAsync(
        string slug, int userId, CreateAbsenceDayRequest request)
    {
        var org = await GetOrgBySlugAsync(slug);
        if (org == null)
            return ServiceResult.NotFound<AbsenceDayResponse>("Organization not found");

        var callerRole = await GetRoleAsync(userId, org.Id);
        if (callerRole == null)
            return ServiceResult.Forbidden<AbsenceDayResponse>();

        // Enforce the effective self-service rule. Org admins and users who can
        // approve absences (group admins with the capability) bypass this check.
        if (callerRole < OrganizationRole.Admin
            && !await EffectiveRuleResolver.CanApproveAbsencesAsync(_context, org.Id, userId))
        {
            var mode = await EffectiveRuleResolver.ResolveAsync(_context, org, userId, RequestType.RequestAbsence);
            if (mode == RuleMode.Disabled)
                return ServiceResult.Forbidden<AbsenceDayResponse>("Adding absences is disabled in this organization. Ask an admin.");
            if (mode == RuleMode.RequiresApproval)
                return ServiceResult.Forbidden<AbsenceDayResponse>("Absences require approval in this organization. Please submit an absence request.");
        }

        var customTypeResult = await ResolveCustomTypeAsync(org.Id, request.CustomTypeId);
        if (!customTypeResult.ok)
            return ServiceResult.BadRequest<AbsenceDayResponse>("Unknown or inactive custom absence type.");

        var exists = await _context.AbsenceDays
            .AnyAsync(a => a.UserId == userId && a.OrganizationId == org.Id && a.Date == request.Date);
        if (exists)
            return ServiceResult.Conflict<AbsenceDayResponse>("An absence already exists on this date.");

        var absence = new AbsenceDay
        {
            UserId = userId,
            OrganizationId = org.Id,
            Date = request.Date,
            Type = customTypeResult.type != null ? AbsenceType.Other : request.Type,
            CustomTypeId = customTypeResult.type?.Id,
            IsHalfDay = request.IsHalfDay,
            Note = request.Note,
            CreatedByUserId = userId
        };

        _context.AbsenceDays.Add(absence);
        await _context.SaveChangesAsync();
        _logger.LogInformation("Absence {AbsenceId} created for user {UserId} in org {OrgSlug}", absence.Id, userId, slug);

        var user = await _context.Users.FindAsync(userId);
        return ServiceResult.Ok(MapToResponse(absence, user, customTypeResult.type));
    }

    public async Task<ServiceResult<AbsenceDayResponse>> AdminCreateAbsenceAsync(
        string slug, int callerUserId, AdminCreateAbsenceDayRequest request)
    {
        var org = await GetOrgBySlugAsync(slug);
        if (org == null)
            return ServiceResult.NotFound<AbsenceDayResponse>("Organization not found");

        var callerRole = await GetRoleAsync(callerUserId, org.Id);
        if (callerRole == null || callerRole < OrganizationRole.Admin)
            return ServiceResult.Forbidden<AbsenceDayResponse>();

        var isMember = await _context.UserOrganizations
            .AnyAsync(uo => uo.OrganizationId == org.Id && uo.UserId == request.UserId);
        if (!isMember)
            return ServiceResult.NotFound<AbsenceDayResponse>("User is not a member of this organization.");

        var customTypeResult = await ResolveCustomTypeAsync(org.Id, request.CustomTypeId);
        if (!customTypeResult.ok)
            return ServiceResult.BadRequest<AbsenceDayResponse>("Unknown or inactive custom absence type.");

        var exists = await _context.AbsenceDays
            .AnyAsync(a => a.UserId == request.UserId && a.OrganizationId == org.Id && a.Date == request.Date);
        if (exists)
            return ServiceResult.Conflict<AbsenceDayResponse>("An absence already exists on this date for this user.");

        var absence = new AbsenceDay
        {
            UserId = request.UserId,
            OrganizationId = org.Id,
            Date = request.Date,
            Type = customTypeResult.type != null ? AbsenceType.Other : request.Type,
            CustomTypeId = customTypeResult.type?.Id,
            IsHalfDay = request.IsHalfDay,
            Note = request.Note,
            CreatedByUserId = callerUserId
        };

        _context.AbsenceDays.Add(absence);
        await _context.SaveChangesAsync();
        _logger.LogInformation("Admin absence {AbsenceId} created for user {TargetUserId} in org {OrgSlug} by user {CallerId}", absence.Id, request.UserId, slug, callerUserId);

        var user = await _context.Users.FindAsync(request.UserId);
        return ServiceResult.Ok(MapToResponse(absence, user, customTypeResult.type));
    }

    public async Task<ServiceResult> DeleteAbsenceAsync(string slug, int callerUserId, int absenceId)
    {
        var org = await GetOrgBySlugAsync(slug);
        if (org == null)
            return ServiceResult.NotFound("Organization not found");

        var callerRole = await GetRoleAsync(callerUserId, org.Id);
        if (callerRole == null)
            return ServiceResult.Forbidden();

        var absence = await _context.AbsenceDays
            .FirstOrDefaultAsync(a => a.Id == absenceId && a.OrganizationId == org.Id);
        if (absence == null)
            return ServiceResult.NotFound("Absence not found");

        // Members can only delete their own
        if (callerRole < OrganizationRole.Admin && absence.UserId != callerUserId)
            return ServiceResult.Forbidden();

        _context.AbsenceDays.Remove(absence);
        await _context.SaveChangesAsync();
        _logger.LogInformation("Absence {AbsenceId} deleted from org {OrgSlug} by user {UserId}", absenceId, slug, callerUserId);
        return ServiceResult.Ok();
    }

    // ────────────────────────────────────────────────────
    //  Custom absence type definitions
    // ────────────────────────────────────────────────────

    public async Task<ServiceResult<List<AbsenceTypeDefinitionResponse>>> GetAbsenceTypesAsync(string slug, int callerUserId)
    {
        var org = await GetOrgBySlugAsync(slug);
        if (org == null)
            return ServiceResult.NotFound<List<AbsenceTypeDefinitionResponse>>("Organization not found");

        var callerRole = await GetRoleAsync(callerUserId, org.Id);
        if (callerRole == null)
            return ServiceResult.Forbidden<List<AbsenceTypeDefinitionResponse>>();

        var types = await _context.AbsenceTypeDefinitions
            .AsNoTracking()
            .Where(t => t.OrganizationId == org.Id && t.IsActive)
            .OrderBy(t => t.Name)
            .Select(t => MapTypeToResponse(t))
            .ToListAsync();

        return ServiceResult.Ok(types);
    }

    public async Task<ServiceResult<AbsenceTypeDefinitionResponse>> CreateAbsenceTypeAsync(
        string slug, int callerUserId, UpsertAbsenceTypeDefinitionRequest request)
    {
        var org = await GetOrgBySlugAsync(slug);
        if (org == null)
            return ServiceResult.NotFound<AbsenceTypeDefinitionResponse>("Organization not found");

        var callerRole = await GetRoleAsync(callerUserId, org.Id);
        if (callerRole == null || callerRole < OrganizationRole.Admin)
            return ServiceResult.Forbidden<AbsenceTypeDefinitionResponse>();

        var nameTaken = await _context.AbsenceTypeDefinitions
            .AnyAsync(t => t.OrganizationId == org.Id && t.IsActive && t.Name == request.Name);
        if (nameTaken)
            return ServiceResult.Conflict<AbsenceTypeDefinitionResponse>("An absence type with this name already exists.");

        var type = new AbsenceTypeDefinition
        {
            OrganizationId = org.Id,
            Name = request.Name,
            Color = request.Color,
            CreditMode = request.CreditMode,
            CountsAsVacation = request.CountsAsVacation
        };

        _context.AbsenceTypeDefinitions.Add(type);
        await _context.SaveChangesAsync();
        _logger.LogInformation("Absence type {TypeId} ({Name}) created in org {OrgSlug} by user {UserId}", type.Id, type.Name, slug, callerUserId);

        return ServiceResult.Ok(MapTypeToResponse(type));
    }

    public async Task<ServiceResult<AbsenceTypeDefinitionResponse>> UpdateAbsenceTypeAsync(
        string slug, int callerUserId, int typeId, UpsertAbsenceTypeDefinitionRequest request)
    {
        var org = await GetOrgBySlugAsync(slug);
        if (org == null)
            return ServiceResult.NotFound<AbsenceTypeDefinitionResponse>("Organization not found");

        var callerRole = await GetRoleAsync(callerUserId, org.Id);
        if (callerRole == null || callerRole < OrganizationRole.Admin)
            return ServiceResult.Forbidden<AbsenceTypeDefinitionResponse>();

        var type = await _context.AbsenceTypeDefinitions
            .FirstOrDefaultAsync(t => t.Id == typeId && t.OrganizationId == org.Id && t.IsActive);
        if (type == null)
            return ServiceResult.NotFound<AbsenceTypeDefinitionResponse>("Absence type not found");

        var nameTaken = await _context.AbsenceTypeDefinitions
            .AnyAsync(t => t.OrganizationId == org.Id && t.IsActive && t.Name == request.Name && t.Id != typeId);
        if (nameTaken)
            return ServiceResult.Conflict<AbsenceTypeDefinitionResponse>("An absence type with this name already exists.");

        type.Name = request.Name;
        type.Color = request.Color;
        type.CreditMode = request.CreditMode;
        type.CountsAsVacation = request.CountsAsVacation;
        await _context.SaveChangesAsync();
        _logger.LogInformation("Absence type {TypeId} updated in org {OrgSlug} by user {UserId}", typeId, slug, callerUserId);

        return ServiceResult.Ok(MapTypeToResponse(type));
    }

    public async Task<ServiceResult> DeleteAbsenceTypeAsync(string slug, int callerUserId, int typeId)
    {
        var org = await GetOrgBySlugAsync(slug);
        if (org == null)
            return ServiceResult.NotFound("Organization not found");

        var callerRole = await GetRoleAsync(callerUserId, org.Id);
        if (callerRole == null || callerRole < OrganizationRole.Admin)
            return ServiceResult.Forbidden();

        var type = await _context.AbsenceTypeDefinitions
            .FirstOrDefaultAsync(t => t.Id == typeId && t.OrganizationId == org.Id && t.IsActive);
        if (type == null)
            return ServiceResult.NotFound("Absence type not found");

        // Soft-delete so existing absence days keep their type reference
        type.IsActive = false;
        await _context.SaveChangesAsync();
        _logger.LogInformation("Absence type {TypeId} deactivated in org {OrgSlug} by user {UserId}", typeId, slug, callerUserId);
        return ServiceResult.Ok();
    }

    private static AbsenceTypeDefinitionResponse MapTypeToResponse(AbsenceTypeDefinition t) => new()
    {
        Id = t.Id,
        OrganizationId = t.OrganizationId,
        Name = t.Name,
        Color = t.Color,
        CreditMode = t.CreditMode.ToString(),
        CountsAsVacation = t.CountsAsVacation,
        IsActive = t.IsActive,
        CreatedAt = t.CreatedAt
    };

    // ────────────────────────────────────────────────────
    //  Private helpers
    // ────────────────────────────────────────────────────

    private static AbsenceDayResponse MapToResponse(AbsenceDay absence, User? user, AbsenceTypeDefinition? customType = null) => new()
    {
        Id = absence.Id,
        UserId = absence.UserId,
        OrganizationId = absence.OrganizationId,
        Date = absence.Date,
        Type = absence.Type.ToString(),
        IsHalfDay = absence.IsHalfDay,
        Note = absence.Note,
        UserFirstName = user?.FirstName,
        UserLastName = user?.LastName,
        CustomTypeId = absence.CustomTypeId,
        CustomTypeName = customType?.Name,
        CustomTypeColor = customType?.Color,
        CreditMode = customType?.CreditMode.ToString(),
        CreatedAt = absence.CreatedAt,
        CreatedByUserId = absence.CreatedByUserId,
        CreatedViaRequestId = absence.CreatedViaRequestId
    };

    /// <summary>
    /// Validates an optional custom absence type id against the org.
    /// Returns (ok: false) when the id is set but unknown/inactive.
    /// </summary>
    private async Task<(bool ok, AbsenceTypeDefinition? type)> ResolveCustomTypeAsync(int orgId, int? customTypeId)
    {
        if (!customTypeId.HasValue) return (true, null);
        var type = await _context.AbsenceTypeDefinitions
            .AsNoTracking()
            .FirstOrDefaultAsync(t => t.Id == customTypeId.Value && t.OrganizationId == orgId && t.IsActive);
        return (type != null, type);
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

    /// <summary>
    /// Returns the set of user IDs whose absences a Group Admin can view,
    /// based on groups where they are Admin. (Used for AdminOnly visibility.)
    /// </summary>
    private async Task<List<int>> GetGroupAdminAbsenceVisibleMemberIdsAsync(int callerUserId, int orgId)
    {
        var adminGroupIds = await _context.GroupMembers
            .Include(gm => gm.Group)
            .Where(gm => gm.UserId == callerUserId
                      && gm.Role == GroupRole.Admin
                      && gm.Group.OrganizationId == orgId
                      && gm.Group.IsActive)
            .Select(gm => gm.GroupId)
            .ToListAsync();

        if (adminGroupIds.Count == 0)
            return new List<int>();

        return await _context.GroupMembers
            .Where(gm => adminGroupIds.Contains(gm.GroupId))
            .Select(gm => gm.UserId)
            .Distinct()
            .ToListAsync();
    }

    /// <summary>
    /// Returns the sets of user IDs whose absences are visible to a regular group member,
    /// based on group-level VacationVisibility/SickDayVisibility overrides.
    /// Only includes users from groups where visibility is set to AllMembers.
    /// </summary>
    private async Task<(List<int> vacationVisible, List<int> sickVisible)> GetGroupMemberVisibleUserIdsAsync(int callerUserId, int orgId)
    {
        // Get all groups the caller belongs to in this org
        var userGroups = await _context.GroupMembers
            .Include(gm => gm.Group)
            .Where(gm => gm.UserId == callerUserId
                      && gm.Group.OrganizationId == orgId
                      && gm.Group.IsActive)
            .Select(gm => gm.Group)
            .ToListAsync();

        if (userGroups.Count == 0)
            return (new List<int>(), new List<int>());

        // Groups where vacation is visible to all members
        var vacationGroupIds = userGroups
            .Where(g => g.VacationVisibility == VisibilityMode.AllMembers)
            .Select(g => g.Id).ToList();

        // Groups where sick days are visible to all members
        var sickGroupIds = userGroups
            .Where(g => g.SickDayVisibility == VisibilityMode.AllMembers)
            .Select(g => g.Id).ToList();

        var allGroupIds = vacationGroupIds.Union(sickGroupIds).Distinct().ToList();
        if (allGroupIds.Count == 0)
            return (new List<int>(), new List<int>());

        // Get member IDs per group
        var groupMembers = await _context.GroupMembers
            .Where(gm => allGroupIds.Contains(gm.GroupId))
            .Select(gm => new { gm.GroupId, gm.UserId })
            .ToListAsync();

        var vacationVisible = groupMembers
            .Where(gm => vacationGroupIds.Contains(gm.GroupId))
            .Select(gm => gm.UserId).Distinct().ToList();

        var sickVisible = groupMembers
            .Where(gm => sickGroupIds.Contains(gm.GroupId))
            .Select(gm => gm.UserId).Distinct().ToList();

        return (vacationVisible, sickVisible);
    }
}
