using Microsoft.EntityFrameworkCore;
using TimeTracking.Api.Data;
using TimeTracking.Api.Models;
using TimeTracking.Api.Models.Dtos;

namespace TimeTracking.Api.Services;

public class OrganizationService : IOrganizationService
{
    private readonly TimeTrackingDbContext _context;

    public OrganizationService(TimeTrackingDbContext context)
    {
        _context = context;
    }

    // ────────────────────────────────────────────────────
    //  Organization CRUD
    // ────────────────────────────────────────────────────

    public async Task<ServiceResult<List<OrganizationResponse>>> GetOrganizationsAsync()
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
                MemberCount = o.UserOrganizations.Count(uo => uo.IsActive),
                JoinPolicy = o.JoinPolicy.ToString()
            })
            .ToListAsync();

        return ServiceResult.Ok(organizations);
    }

    public async Task<ServiceResult<OrganizationDetailResponse>> GetOrganizationAsync(string slug)
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
                EditPastEntriesMode = o.EditPastEntriesMode.ToString(),
                EditPauseMode = o.EditPauseMode.ToString(),
                InitialOvertimeMode = o.InitialOvertimeMode.ToString(),
                JoinPolicy = o.JoinPolicy.ToString(),
                WorkScheduleChangeMode = o.WorkScheduleChangeMode.ToString(),
                MemberTimeEntryVisibility = o.MemberTimeEntryVisibility,
                SettingsUpdatedAt = o.SettingsUpdatedAt,
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
                        JoinedAt = uo.JoinedAt,
                        InitialOvertimeHours = uo.InitialOvertimeHours
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
            return ServiceResult.NotFound<OrganizationDetailResponse>("Organization not found");

        return ServiceResult.Ok(organization);
    }

    public async Task<ServiceResult<List<UserOrganizationResponse>>> GetUserOrganizationsAsync(int userId)
    {
        var userExists = await _context.Users.AnyAsync(u => u.Id == userId && u.IsActive);
        if (!userExists)
            return ServiceResult.NotFound<List<UserOrganizationResponse>>("User not found");

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

        return ServiceResult.Ok(organizations);
    }

    public async Task<ServiceResult<OrganizationResponse>> CreateOrganizationAsync(
        int callerUserId, CreateOrganizationRequest request)
    {
        if (await _context.Organizations.AnyAsync(o => o.Slug == request.Slug && o.IsActive))
            return ServiceResult.Conflict<OrganizationResponse>("An organization with this slug already exists");

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

        _context.UserOrganizations.Add(new UserOrganization
        {
            UserId = callerUserId,
            OrganizationId = organization.Id,
            Role = OrganizationRole.Owner,
            JoinedAt = DateTime.UtcNow,
            IsActive = true
        });
        await _context.SaveChangesAsync();

        return ServiceResult.Ok(new OrganizationResponse
        {
            Id = organization.Id,
            Name = organization.Name,
            Description = organization.Description,
            Slug = organization.Slug,
            Website = organization.Website,
            LogoUrl = organization.LogoUrl,
            CreatedAt = organization.CreatedAt,
            MemberCount = 1,
            JoinPolicy = organization.JoinPolicy.ToString()
        });
    }

    public async Task<ServiceResult<OrganizationResponse>> UpdateOrganizationAsync(
        string slug, int callerUserId, UpdateOrganizationRequest request)
    {
        var organization = await _context.Organizations
            .Include(o => o.UserOrganizations)
            .FirstOrDefaultAsync(o => o.Slug == slug && o.IsActive);

        if (organization == null)
            return ServiceResult.NotFound<OrganizationResponse>("Organization not found");

        var callerRole = await GetRoleAsync(callerUserId, organization.Id);
        if (callerRole == null || callerRole < OrganizationRole.Admin)
            return ServiceResult.Forbidden<OrganizationResponse>();

        if (request.Slug != null && request.Slug != organization.Slug)
        {
            if (await _context.Organizations.AnyAsync(o => o.Slug == request.Slug && o.IsActive && o.Id != organization.Id))
                return ServiceResult.Conflict<OrganizationResponse>("An organization with this slug already exists");
        }

        if (request.Name != null) organization.Name = request.Name;
        if (request.Description != null) organization.Description = request.Description;
        if (request.Slug != null) organization.Slug = request.Slug;
        if (request.Website != null) organization.Website = request.Website;
        if (request.LogoUrl != null) organization.LogoUrl = request.LogoUrl;
        organization.UpdatedAt = DateTime.UtcNow;

        await _context.SaveChangesAsync();

        return ServiceResult.Ok(new OrganizationResponse
        {
            Id = organization.Id,
            Name = organization.Name,
            Description = organization.Description,
            Slug = organization.Slug,
            Website = organization.Website,
            LogoUrl = organization.LogoUrl,
            CreatedAt = organization.CreatedAt,
            MemberCount = organization.UserOrganizations.Count(uo => uo.IsActive),
            JoinPolicy = organization.JoinPolicy.ToString()
        });
    }

    public async Task<ServiceResult> DeleteOrganizationAsync(string slug, int callerUserId)
    {
        var organization = await _context.Organizations
            .Include(o => o.UserOrganizations)
            .FirstOrDefaultAsync(o => o.Slug == slug && o.IsActive);

        if (organization == null)
            return ServiceResult.NotFound("Organization not found");

        var callerRole = await GetRoleAsync(callerUserId, organization.Id);
        if (callerRole == null || callerRole != OrganizationRole.Owner)
            return ServiceResult.Forbidden();

        organization.IsActive = false;
        organization.UpdatedAt = DateTime.UtcNow;

        foreach (var membership in organization.UserOrganizations)
            membership.IsActive = false;

        await _context.SaveChangesAsync();
        return ServiceResult.Ok();
    }

    // ────────────────────────────────────────────────────
    //  Member management
    // ────────────────────────────────────────────────────

    public async Task<ServiceResult<OrganizationMemberResponse>> AddMemberAsync(
        string slug, int callerUserId, AddMemberRequest request)
    {
        var org = await GetOrgBySlugAsync(slug);
        if (org == null)
            return ServiceResult.NotFound<OrganizationMemberResponse>("Organization not found");

        var callerRole = await GetRoleAsync(callerUserId, org.Id);
        if (callerRole == null || callerRole < OrganizationRole.Admin)
            return ServiceResult.Forbidden<OrganizationMemberResponse>();

        if (callerRole == OrganizationRole.Admin && request.Role == OrganizationRole.Owner)
            return ServiceResult.Forbidden<OrganizationMemberResponse>();

        var user = await _context.Users.FirstOrDefaultAsync(u => u.Id == request.UserId && u.IsActive);
        if (user == null)
            return ServiceResult.NotFound<OrganizationMemberResponse>("User not found");

        var existing = await _context.UserOrganizations
            .FirstOrDefaultAsync(uo => uo.OrganizationId == org.Id && uo.UserId == request.UserId);

        if (existing != null)
        {
            if (existing.IsActive)
                return ServiceResult.Conflict<OrganizationMemberResponse>("User is already a member of this organization");

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

        return ServiceResult.Ok(new OrganizationMemberResponse
        {
            Id = user.Id,
            Email = user.Email,
            FirstName = user.FirstName,
            LastName = user.LastName,
            ProfileImageUrl = user.ProfileImageUrl,
            Role = existing.Role.ToString(),
            JoinedAt = existing.JoinedAt,
            InitialOvertimeHours = existing.InitialOvertimeHours
        });
    }

    public async Task<ServiceResult<OrganizationMemberResponse>> UpdateMemberRoleAsync(
        string slug, int callerUserId, int userId, UpdateMemberRoleRequest request)
    {
        var org = await GetOrgBySlugAsync(slug);
        if (org == null)
            return ServiceResult.NotFound<OrganizationMemberResponse>("Organization not found");

        var callerRole = await GetRoleAsync(callerUserId, org.Id);
        if (callerRole == null || callerRole < OrganizationRole.Admin)
            return ServiceResult.Forbidden<OrganizationMemberResponse>();

        var membership = await _context.UserOrganizations
            .Include(uo => uo.User)
            .FirstOrDefaultAsync(uo => uo.OrganizationId == org.Id && uo.UserId == userId && uo.IsActive);

        if (membership == null)
            return ServiceResult.NotFound<OrganizationMemberResponse>("Member not found in this organization");

        if (membership.Role == OrganizationRole.Owner)
            return ServiceResult.BadRequest<OrganizationMemberResponse>("Cannot change the Owner's role. Transfer ownership first.");

        if (callerRole == OrganizationRole.Admin)
        {
            if (request.Role == OrganizationRole.Owner)
                return ServiceResult.Forbidden<OrganizationMemberResponse>();
            if (membership.Role == OrganizationRole.Admin)
                return ServiceResult.Forbidden<OrganizationMemberResponse>();
        }

        membership.Role = request.Role;
        await _context.SaveChangesAsync();

        return ServiceResult.Ok(new OrganizationMemberResponse
        {
            Id = membership.User.Id,
            Email = membership.User.Email,
            FirstName = membership.User.FirstName,
            LastName = membership.User.LastName,
            ProfileImageUrl = membership.User.ProfileImageUrl,
            Role = membership.Role.ToString(),
            JoinedAt = membership.JoinedAt,
            InitialOvertimeHours = membership.InitialOvertimeHours
        });
    }

    public async Task<ServiceResult> RemoveMemberAsync(string slug, int callerUserId, int userId)
    {
        var org = await GetOrgBySlugAsync(slug);
        if (org == null)
            return ServiceResult.NotFound("Organization not found");

        var callerRole = await GetRoleAsync(callerUserId, org.Id);
        if (callerRole == null)
            return ServiceResult.Forbidden();

        var membership = await _context.UserOrganizations
            .FirstOrDefaultAsync(uo => uo.OrganizationId == org.Id && uo.UserId == userId && uo.IsActive);

        if (membership == null)
            return ServiceResult.NotFound("Member not found in this organization");

        bool isSelf = callerUserId == userId;

        if (isSelf && membership.Role == OrganizationRole.Owner)
            return ServiceResult.BadRequest("Owner cannot leave. Delete the organization or transfer ownership first.");

        if (!isSelf)
        {
            if (callerRole < OrganizationRole.Admin)
                return ServiceResult.Forbidden();
            if (callerRole == OrganizationRole.Admin && membership.Role >= OrganizationRole.Admin)
                return ServiceResult.Forbidden();
        }

        membership.IsActive = false;
        await _context.SaveChangesAsync();

        return ServiceResult.Ok();
    }

    // ────────────────────────────────────────────────────
    //  Settings
    // ────────────────────────────────────────────────────

    public async Task<ServiceResult<object>> UpdateSettingsAsync(
        string slug, int callerUserId, UpdateOrganizationSettingsRequest request)
    {
        var org = await GetOrgBySlugAsync(slug);
        if (org == null)
            return ServiceResult.NotFound<object>("Organization not found");

        var callerRole = await GetRoleAsync(callerUserId, org.Id);
        if (callerRole == null || callerRole < OrganizationRole.Admin)
            return ServiceResult.Forbidden<object>();

        if (request.AutoPauseEnabled.HasValue)   org.AutoPauseEnabled = request.AutoPauseEnabled.Value;
        if (request.EditPastEntriesMode.HasValue) org.EditPastEntriesMode = request.EditPastEntriesMode.Value;
        if (request.EditPauseMode.HasValue)       org.EditPauseMode = request.EditPauseMode.Value;
        if (request.InitialOvertimeMode.HasValue) org.InitialOvertimeMode = request.InitialOvertimeMode.Value;
        if (request.JoinPolicy.HasValue)          org.JoinPolicy = request.JoinPolicy.Value;
        if (request.WorkScheduleChangeMode.HasValue) org.WorkScheduleChangeMode = request.WorkScheduleChangeMode.Value;
        if (request.MemberTimeEntryVisibility.HasValue) org.MemberTimeEntryVisibility = request.MemberTimeEntryVisibility.Value;

        org.UpdatedAt = DateTime.UtcNow;
        org.SettingsUpdatedAt = DateTime.UtcNow;
        await _context.SaveChangesAsync();

        // Notify all org members except the caller
        var memberUserIds = await _context.UserOrganizations
            .Where(uo => uo.OrganizationId == org.Id && uo.IsActive && uo.UserId != callerUserId)
            .Select(uo => uo.UserId)
            .ToListAsync();

        if (memberUserIds.Count > 0)
        {
            var notifications = memberUserIds.Select(uid => new Notification
            {
                UserId = uid,
                OrganizationId = org.Id,
                Title = "Organization rules updated",
                Message = $"The rules for \"{org.Name}\" have been updated by an administrator.",
                Type = "SettingsChanged"
            }).ToList();

            _context.Notifications.AddRange(notifications);
            await _context.SaveChangesAsync();
        }

        return ServiceResult.Ok<object>(new
        {
            org.AutoPauseEnabled,
            EditPastEntriesMode = org.EditPastEntriesMode.ToString(),
            EditPauseMode = org.EditPauseMode.ToString(),
            InitialOvertimeMode = org.InitialOvertimeMode.ToString(),
            JoinPolicy = org.JoinPolicy.ToString(),
            WorkScheduleChangeMode = org.WorkScheduleChangeMode.ToString(),
            org.MemberTimeEntryVisibility
        });
    }

    // ────────────────────────────────────────────────────
    //  Time overview (admin)
    // ────────────────────────────────────────────────────

    public async Task<ServiceResult<List<MemberTimeOverviewResponse>>> GetTimeOverviewAsync(
        string slug, int callerUserId, DateTime? from, DateTime? to)
    {
        var org = await GetOrgBySlugAsync(slug);
        if (org == null)
            return ServiceResult.NotFound<List<MemberTimeOverviewResponse>>("Organization not found");

        var callerRole = await GetRoleAsync(callerUserId, org.Id);
        if (callerRole == null || callerRole < OrganizationRole.Admin || !org.MemberTimeEntryVisibility)
            return ServiceResult.Forbidden<List<MemberTimeOverviewResponse>>();

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

        var today = DateOnly.FromDateTime(DateTime.UtcNow);
        var schedules = await _context.WorkSchedules
            .Where(s => memberIds.Contains(s.UserId)
                     && s.OrganizationId == org.Id
                     && s.ValidFrom <= today
                     && (s.ValidTo == null || s.ValidTo >= today))
            .ToListAsync();

        var result = members.Select(m =>
        {
            var entries = timeEntries.Where(te => te.UserId == m.UserId).ToList();
            var totalMinutes = entries.Sum(e =>
                e.EndTime.HasValue ? (e.EndTime.Value - e.StartTime).TotalMinutes : 0);
            var totalPause = entries.Sum(e => e.PauseDurationMinutes);

            var schedule = schedules
                .Where(s => s.UserId == m.UserId)
                .OrderByDescending(s => s.ValidFrom)
                .FirstOrDefault();

            return new MemberTimeOverviewResponse
            {
                UserId = m.UserId,
                FirstName = m.User.FirstName,
                LastName = m.User.LastName,
                Email = m.User.Email,
                Role = m.Role.ToString(),
                WeeklyWorkHours = schedule?.WeeklyWorkHours,
                TotalTrackedMinutes = Math.Round(totalMinutes, 1),
                NetTrackedMinutes = Math.Round(totalMinutes - totalPause, 1),
                EntryCount = entries.Count
            };
        }).ToList();

        return ServiceResult.Ok(result);
    }

    public async Task<ServiceResult<List<object>>> GetMemberEntriesAsync(
        string slug, int callerUserId, int userId, DateTime? from, DateTime? to)
    {
        var org = await GetOrgBySlugAsync(slug);
        if (org == null)
            return ServiceResult.NotFound<List<object>>("Organization not found");

        var callerRole = await GetRoleAsync(callerUserId, org.Id);
        if (callerRole == null || callerRole < OrganizationRole.Admin || !org.MemberTimeEntryVisibility)
            return ServiceResult.Forbidden<List<object>>();

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
            .Select(te => (object)new
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

        return ServiceResult.Ok(entries);
    }

    // ────────────────────────────────────────────────────
    //  Initial overtime (admin)
    // ────────────────────────────────────────────────────

    public async Task<ServiceResult<object>> SetMemberInitialOvertimeAsync(
        string slug, int callerUserId, int userId, SetInitialOvertimeRequest request)
    {
        var org = await GetOrgBySlugAsync(slug);
        if (org == null)
            return ServiceResult.NotFound<object>("Organization not found");

        var callerRole = await GetRoleAsync(callerUserId, org.Id);
        if (callerRole == null || callerRole < OrganizationRole.Admin)
            return ServiceResult.Forbidden<object>();

        var membership = await _context.UserOrganizations
            .FirstOrDefaultAsync(uo => uo.OrganizationId == org.Id && uo.UserId == userId && uo.IsActive);

        if (membership == null)
            return ServiceResult.NotFound<object>("Member not found in this organization.");

        membership.InitialOvertimeHours = request.InitialOvertimeHours;
        await _context.SaveChangesAsync();

        return ServiceResult.Ok<object>(new { userId, InitialOvertimeHours = membership.InitialOvertimeHours });
    }

    // ────────────────────────────────────────────────────
    //  Private helpers
    // ────────────────────────────────────────────────────

    private async Task<Organization?> GetOrgBySlugAsync(string slug)
    {
        return await _context.Organizations.FirstOrDefaultAsync(o => o.Slug == slug && o.IsActive);
    }

    private async Task<OrganizationRole?> GetRoleAsync(int userId, int organizationId)
    {
        var membership = await _context.UserOrganizations
            .FirstOrDefaultAsync(uo => uo.OrganizationId == organizationId
                                    && uo.UserId == userId
                                    && uo.IsActive);
        return membership?.Role;
    }
}
