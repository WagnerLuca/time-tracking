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
        var org = await _context.FindOrgBySlugAsync(slug);
        if (org == null)
            return ServiceResult.NotFound<PaginatedResponse<AbsenceDayResponse>>("Organization not found");

        var callerRole = await _context.GetOrgRoleAsync(callerUserId, org.Id);
        if (callerRole == null)
            return ServiceResult.Forbidden<PaginatedResponse<AbsenceDayResponse>>();

        (limit, offset) = PaginationDefaults.Normalize(limit, offset);

        // Admins see every absence (also of former members); everyone else themselves, and the
        // others per kind of absence as the member visibility matrix (and group admin rights) allow
        var visibility = await MemberVisibility.ForCallerAsync(_context, org, callerUserId);
        var callerIsAdmin = visibility.CallerIsOrgAdmin;
        var noteIds = visibility.Ids(MemberInfo.AbsenceNotes).ToList();
        var query = _context.AbsenceDays.Where(a => a.OrganizationId == org.Id);
        if (!visibility.CallerIsOrgAdmin)
        {
            if (filterUserId.HasValue && !visibility.Can(filterUserId.Value, MemberInfo.Member))
                return ServiceResult.NotFound<PaginatedResponse<AbsenceDayResponse>>("Member not found in this organization.");
            var vacationIds = visibility.Ids(MemberInfo.Vacation).ToList();
            var sickIds = visibility.Ids(MemberInfo.SickDays).ToList();
            var otherIds = visibility.Ids(MemberInfo.OtherAbsences).ToList();
            query = query.Where(a => (a.Type == AbsenceType.Vacation && vacationIds.Contains(a.UserId))
                                  || (a.Type == AbsenceType.SickDay && sickIds.Contains(a.UserId))
                                  || (a.Type == AbsenceType.Other && otherIds.Contains(a.UserId)));
        }
        if (filterUserId.HasValue)
            query = query.Where(a => a.UserId == filterUserId.Value);

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
                Note = callerIsAdmin || noteIds.Contains(a.UserId) ? a.Note : null,
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

        // Who entered an absence names a person too
        absences = absences
            .Select(a => !callerIsAdmin && a.CreatedByUserId is int by && !visibility.Can(by, MemberInfo.Member) ? a with { CreatedByName = null } : a)
            .ToList();

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
        => Single(await CreateOwnAsync(slug, userId, [request.Date], request.Type, request.CustomTypeId, request.IsHalfDay, request.Note));

    public async Task<ServiceResult<List<AbsenceDayResponse>>> CreateAbsenceRangeAsync(
        string slug, int userId, CreateAbsenceRangeRequest request)
    {
        var dates = await WorkingDaysBetweenAsync(slug, userId, request.From, request.To);
        if (dates == null)
            return ServiceResult.BadRequest<List<AbsenceDayResponse>>("Choose a range of at most one year with at least one working day.");
        return await CreateOwnAsync(slug, userId, dates, request.Type, request.CustomTypeId, request.IsHalfDay, request.Note);
    }

    public async Task<ServiceResult<AbsenceDayResponse>> AdminCreateAbsenceAsync(
        string slug, int callerUserId, AdminCreateAbsenceDayRequest request)
        => Single(await CreateForMemberAsync(slug, callerUserId, request.UserId, [request.Date], request.Type, request.CustomTypeId, request.IsHalfDay, request.Note));

    public async Task<ServiceResult<List<AbsenceDayResponse>>> AdminCreateAbsenceRangeAsync(
        string slug, int callerUserId, CreateAbsenceRangeRequest request)
    {
        if (request.UserId is not int targetUserId)
            return ServiceResult.BadRequest<List<AbsenceDayResponse>>("UserId is required.");
        var dates = await WorkingDaysBetweenAsync(slug, targetUserId, request.From, request.To);
        if (dates == null)
            return ServiceResult.BadRequest<List<AbsenceDayResponse>>("Choose a range of at most one year with at least one working day.");
        return await CreateForMemberAsync(slug, callerUserId, targetUserId, dates, request.Type, request.CustomTypeId, request.IsHalfDay, request.Note);
    }

    /// <summary>Self-service: the member adds their own absences, subject to the effective self-service rule.</summary>
    private async Task<ServiceResult<List<AbsenceDayResponse>>> CreateOwnAsync(
        string slug, int userId, IReadOnlyList<DateOnly> dates, AbsenceType type, int? customTypeId, bool isHalfDay, string? note)
    {
        var org = await _context.FindOrgBySlugAsync(slug);
        if (org == null)
            return ServiceResult.NotFound<List<AbsenceDayResponse>>("Organization not found");

        var callerRole = await _context.GetOrgRoleAsync(userId, org.Id);
        if (callerRole == null)
            return ServiceResult.Forbidden<List<AbsenceDayResponse>>();

        // Enforce the effective self-service rule. Org admins and users who can
        // approve absences (group admins with the capability) bypass this check.
        if (callerRole < OrganizationRole.Admin
            && !await EffectiveRuleResolver.CanApproveAbsencesAsync(_context, org.Id, userId))
        {
            var mode = await EffectiveRuleResolver.ResolveAsync(_context, org, userId, RequestType.RequestAbsence);
            if (mode == RuleMode.Disabled)
                return ServiceResult.Forbidden<List<AbsenceDayResponse>>("Adding absences is disabled in this organization. Ask an admin.");
            if (mode == RuleMode.RequiresApproval)
                return ServiceResult.Forbidden<List<AbsenceDayResponse>>("Absences require approval in this organization. Please submit an absence request.");
        }

        return await AddAbsencesAsync(org, userId, userId, dates, type, customTypeId, isHalfDay, note);
    }

    /// <summary>Admins add absences for any member.</summary>
    private async Task<ServiceResult<List<AbsenceDayResponse>>> CreateForMemberAsync(
        string slug, int callerUserId, int targetUserId, IReadOnlyList<DateOnly> dates, AbsenceType type, int? customTypeId, bool isHalfDay, string? note)
    {
        var org = await _context.FindOrgBySlugAsync(slug);
        if (org == null)
            return ServiceResult.NotFound<List<AbsenceDayResponse>>("Organization not found");

        var callerRole = await _context.GetOrgRoleAsync(callerUserId, org.Id);
        if (callerRole == null || callerRole < OrganizationRole.Admin)
            return ServiceResult.Forbidden<List<AbsenceDayResponse>>();

        var isMember = await _context.UserOrganizations
            .AnyAsync(uo => uo.OrganizationId == org.Id && uo.UserId == targetUserId);
        if (!isMember)
            return ServiceResult.NotFound<List<AbsenceDayResponse>>("User is not a member of this organization.");

        return await AddAbsencesAsync(org, callerUserId, targetUserId, dates, type, customTypeId, isHalfDay, note);
    }

    /// <summary>Validates all days first, then saves them together: either every day is created or none.</summary>
    private async Task<ServiceResult<List<AbsenceDayResponse>>> AddAbsencesAsync(
        Organization org, int callerUserId, int targetUserId, IReadOnlyList<DateOnly> dates,
        AbsenceType type, int? customTypeId, bool isHalfDay, string? note)
    {
        var customTypeResult = await ResolveCustomTypeAsync(org.Id, customTypeId);
        if (!customTypeResult.ok)
            return ServiceResult.BadRequest<List<AbsenceDayResponse>>("Unknown or inactive custom absence type.");

        var taken = await _context.AbsenceDays
            .Where(a => a.UserId == targetUserId && a.OrganizationId == org.Id && dates.Contains(a.Date))
            .Select(a => a.Date)
            .OrderBy(d => d)
            .ToListAsync();
        if (taken.Count > 0)
            return ServiceResult.Conflict<List<AbsenceDayResponse>>(
                dates.Count == 1
                    ? "An absence already exists on this date."
                    : $"Absences already exist on {string.Join(", ", taken.Select(d => d.ToString("yyyy-MM-dd")))}.");

        var absences = dates.Select(date => new AbsenceDay
        {
            UserId = targetUserId,
            OrganizationId = org.Id,
            Date = date,
            Type = customTypeResult.type != null ? AbsenceType.Other : type,
            CustomTypeId = customTypeResult.type?.Id,
            IsHalfDay = isHalfDay,
            Note = note,
            CreatedByUserId = callerUserId
        }).ToList();

        _context.AbsenceDays.AddRange(absences);
        await _context.SaveChangesAsync();
        _logger.LogInformation("{Count} absence day(s) created for user {TargetUserId} in org {OrgSlug} by user {CallerId}",
            absences.Count, targetUserId, org.Slug, callerUserId);

        var user = await _context.Users.FindAsync(targetUserId);
        return ServiceResult.Ok(absences.Select(a => MapToResponse(a, user, customTypeResult.type)).ToList());
    }

    /// <summary>
    /// The member's working days between from and to (inclusive), holidays skipped (see
    /// <see cref="WorkingDays"/>); null for an empty, reversed or &gt; 1 year range.
    /// </summary>
    private async Task<List<DateOnly>?> WorkingDaysBetweenAsync(string slug, int userId, DateOnly from, DateOnly to)
    {
        if (to < from || to.DayNumber - from.DayNumber > 366) return null;
        var org = await _context.FindOrgBySlugAsync(slug);
        var all = Enumerable.Range(0, to.DayNumber - from.DayNumber + 1).Select(from.AddDays).ToList();
        var days = await WorkingDays.FilterAsync(_context, userId, org?.Id, all);
        return days.Count == 0 ? null : days;
    }

    private static ServiceResult<AbsenceDayResponse> Single(ServiceResult<List<AbsenceDayResponse>> result) =>
        result.IsSuccess
            ? ServiceResult.Ok(result.Data![0])
            : ServiceResult.Fail<AbsenceDayResponse>(result.ErrorType!.Value, result.ErrorMessage!);

    public Task<ServiceResult> DeleteAbsenceAsync(string slug, int callerUserId, int absenceId) =>
        DeleteAbsencesAsync(slug, callerUserId, [absenceId]);

    /// <summary>
    /// Deletes several absence days (a span) in one save, all or nothing: one closed-month reason
    /// prompt for the whole span, and a refused day leaves the others in place.
    /// </summary>
    public async Task<ServiceResult> DeleteAbsencesAsync(string slug, int callerUserId, IReadOnlyCollection<int> absenceIds)
    {
        var org = await _context.FindOrgBySlugAsync(slug);
        if (org == null)
            return ServiceResult.NotFound("Organization not found");

        var callerRole = await _context.GetOrgRoleAsync(callerUserId, org.Id);
        if (callerRole == null)
            return ServiceResult.Forbidden();

        var ids = absenceIds.Distinct().ToList();
        var absences = await _context.AbsenceDays
            .Where(a => ids.Contains(a.Id) && a.OrganizationId == org.Id)
            .ToListAsync();
        if (absences.Count != ids.Count)
            return ServiceResult.NotFound("Absence not found");

        // Members can only delete their own, self-entered absences: one an admin entered or that
        // came from an approved request is part of the approval trail
        if (callerRole < OrganizationRole.Admin)
        {
            if (absences.Any(a => a.UserId != callerUserId))
                return ServiceResult.Forbidden();
            if (absences.Any(a => a.CreatedViaRequestId != null || (a.CreatedByUserId != null && a.CreatedByUserId != callerUserId)))
                return ServiceResult.Forbidden("This absence was approved or entered by an admin. Ask an admin to remove it.");
        }

        _context.AbsenceDays.RemoveRange(absences);
        await _context.SaveChangesAsync();
        _logger.LogInformation("Absences {AbsenceIds} deleted from org {OrgSlug} by user {UserId}", ids, slug, callerUserId);
        return ServiceResult.Ok();
    }

    // ────────────────────────────────────────────────────
    //  Custom absence type definitions
    // ────────────────────────────────────────────────────

    public async Task<ServiceResult<List<AbsenceTypeDefinitionResponse>>> GetAbsenceTypesAsync(string slug, int callerUserId)
    {
        var org = await _context.FindOrgBySlugAsync(slug);
        if (org == null)
            return ServiceResult.NotFound<List<AbsenceTypeDefinitionResponse>>("Organization not found");

        var callerRole = await _context.GetOrgRoleAsync(callerUserId, org.Id);
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
        var org = await _context.FindOrgBySlugAsync(slug);
        if (org == null)
            return ServiceResult.NotFound<AbsenceTypeDefinitionResponse>("Organization not found");

        var callerRole = await _context.GetOrgRoleAsync(callerUserId, org.Id);
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
        var org = await _context.FindOrgBySlugAsync(slug);
        if (org == null)
            return ServiceResult.NotFound<AbsenceTypeDefinitionResponse>("Organization not found");

        var callerRole = await _context.GetOrgRoleAsync(callerUserId, org.Id);
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
        var org = await _context.FindOrgBySlugAsync(slug);
        if (org == null)
            return ServiceResult.NotFound("Organization not found");

        var callerRole = await _context.GetOrgRoleAsync(callerUserId, org.Id);
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

}
