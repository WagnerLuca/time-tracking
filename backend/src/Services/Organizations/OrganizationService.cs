using Microsoft.EntityFrameworkCore;
using TimeTracking.Api.Data;
using TimeTracking.Api.Models;
using TimeTracking.Api.Models.Dtos;

namespace TimeTracking.Api.Services;

public class OrganizationService : IOrganizationService
{
    private readonly TimeTrackingDbContext _context;
    private readonly ILogger<OrganizationService> _logger;

    public OrganizationService(TimeTrackingDbContext context, ILogger<OrganizationService> logger)
    {
        _context = context;
        _logger = logger;
    }

    // ────────────────────────────────────────────────────
    //  Organization CRUD
    // ────────────────────────────────────────────────────

    public async Task<ServiceResult<PaginatedResponse<OrganizationResponse>>> GetOrganizationsAsync(int limit, int offset)
    {
        (limit, offset) = PaginationDefaults.Normalize(limit, offset);

        var query = _context.Organizations
            .AsQueryable();

        var totalCount = await query.CountAsync();

        var organizations = await query
            .OrderBy(o => o.Name)
            .Skip(offset)
            .Take(limit)
            .Select(o => new OrganizationResponse
            {
                Id = o.Id,
                Name = o.Name,
                Description = o.Description,
                Slug = o.Slug,
                Website = o.Website,
                LogoUrl = o.LogoUrl,
                CreatedAt = o.CreatedAt,
                MemberCount = o.UserOrganizations.Count(),
                JoinPolicy = o.JoinPolicy.ToString()
            })
            .ToListAsync();

        return ServiceResult.Ok(new PaginatedResponse<OrganizationResponse>
        {
            Items = organizations,
            TotalCount = totalCount,
            Limit = limit,
            Offset = offset
        });
    }

    public async Task<ServiceResult<OrganizationDetailResponse>> GetOrganizationAsync(string slug, int? callerUserId)
    {
        var organization = await _context.Organizations
            .Where(o => o.Slug == slug)
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
                Require2fa = o.Require2fa,
                CsvImportMode = o.CsvImportMode.ToString(),
                AbsenceSelfServiceMode = o.AbsenceSelfServiceMode.ToString(),
                VacationVisibility = o.VacationVisibility.ToString(),
                SickDayVisibility = o.SickDayVisibility.ToString(),
                DefaultVacationDays = o.DefaultVacationDays,
                BusinessDayEnd = o.BusinessDayEnd.ToString("HH:mm"),
                TimeZone = o.TimeZone,
                SettingsUpdatedAt = o.SettingsUpdatedAt,
                CreatedAt = o.CreatedAt,
                Members = o.UserOrganizations
                    .Select(uo => new OrganizationMemberResponse
                    {
                        Id = uo.User.Id,
                        Email = uo.User.Email,
                        FirstName = uo.User.FirstName,
                        LastName = uo.User.LastName,
                        ProfileImageUrl = uo.User.ProfileImageUrl,
                        Role = uo.Role.ToString(),
                        JoinedAt = uo.JoinedAt,
                        InitialOvertimeHours = uo.InitialOvertimeHours,
                        VacationDaysPerYear = uo.VacationDaysPerYear,
                        CardCount = _context.BadgeCards.Count(c => c.OrganizationId == uo.OrganizationId && c.UserId == uo.UserId)
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

        // Strip member PII for non-members: only members can see the full member list.
        // An org that requires 2FA shows it only to members who have 2FA, like every other org endpoint.
        var isMember = callerUserId.HasValue && organization.Members.Any(m => m.Id == callerUserId.Value);
        if (isMember && organization.Require2fa)
            isMember = await _context.Users.AnyAsync(u => u.Id == callerUserId!.Value && u.TwoFactorEnabled);
        if (!isMember)
        {
            return ServiceResult.Ok(organization with { Members = new List<OrganizationMemberResponse>() });
        }

        // Compute vacation days used for each member (current year, in the org's time zone)
        var today = OrgClock.For(organization.TimeZone).Today();
        var currentYear = today.Year;
        var yearStart = new DateOnly(currentYear, 1, 1);
        var yearEnd = new DateOnly(currentYear, 12, 31);
        var memberIds = organization.Members.Select(m => m.Id).ToList();

        // Load holiday dates for this org (to exclude from vacation count)
        var orgHolidays = await _context.Holidays.AsNoTracking()
            .Where(h => h.OrganizationId == organization.Id)
            .ToListAsync();
        var holidayDates = orgHolidays
            .Select(h => h.DateIn(currentYear))
            .OfType<DateOnly>()
            .ToHashSet();

        var vacationUsage = await _context.AbsenceDays.AsNoTracking()
            .Where(a => a.OrganizationId == organization.Id
                     && memberIds.Contains(a.UserId)
                     && (a.Type == AbsenceType.Vacation || (a.CustomType != null && a.CustomType.CountsAsVacation))
                     && a.Date >= yearStart && a.Date <= yearEnd)
            .ToListAsync();

        // Only the member's working days count (a vacation day on a day off costs nothing)
        var schedulesByUser = (await _context.WorkSchedules.AsNoTracking()
                .Where(ws => ws.OrganizationId == organization.Id && memberIds.Contains(ws.UserId))
                .ToListAsync())
            .ToLookup(ws => ws.UserId);

        // Split usage into taken (up to today) and planned (after today, rest of year)
        var usageByUser = vacationUsage
            .Where(a => !holidayDates.Contains(a.Date) && WorkingDays.IsWorkingDay(schedulesByUser[a.UserId], a.Date))
            .GroupBy(a => a.UserId)
            .ToDictionary(
                g => g.Key,
                g => new
                {
                    Taken = g.Where(a => a.Date <= today).Sum(a => a.IsHalfDay ? 0.5 : 1.0),
                    Planned = g.Where(a => a.Date > today).Sum(a => a.IsHalfDay ? 0.5 : 1.0)
                });

        // Group memberships for tag display (e.g. "Frontend Admin")
        var memberGroups = await _context.GroupMembers.AsNoTracking()
            .Where(gm => gm.Group.OrganizationId == organization.Id && gm.Group.IsActive)
            .Select(gm => new
            {
                gm.UserId,
                Group = new MemberGroupResponse
                {
                    GroupId = gm.GroupId,
                    Name = gm.Group.Name,
                    Color = gm.Group.Color,
                    IsGroupAdmin = gm.Role == GroupRole.Admin
                }
            })
            .ToListAsync();
        var groupsByUser = memberGroups
            .GroupBy(g => g.UserId)
            .ToDictionary(g => g.Key, g => g.Select(x => x.Group).OrderBy(x => x.Name).ToList());

        // Determine whose vacation balance the caller may see (self, admins, effective VacationVisibility)
        var vacationVisibleIds = await GetVacationVisibleUserIdsAsync(callerUserId!.Value, organization.Id);
        var callerIsAdmin = organization.Members.First(m => m.Id == callerUserId.Value).Role is nameof(OrganizationRole.Owner) or nameof(OrganizationRole.Admin);

        var membersWithVacation = organization.Members.Select(m =>
        {
            var visible = vacationVisibleIds.Contains(m.Id);
            var usage = usageByUser.GetValueOrDefault(m.Id);
            var taken = usage?.Taken ?? 0;
            var planned = usage?.Planned ?? 0;
            var used = taken + planned;
            return m with
            {
                VacationVisible = visible,
                VacationDaysPerYear = visible ? m.VacationDaysPerYear : 0,
                VacationDaysUsed = visible ? used : 0,
                VacationDaysTaken = visible ? taken : 0,
                VacationDaysPlanned = visible ? planned : 0,
                VacationDaysRemaining = visible ? Math.Max(0, m.VacationDaysPerYear - used) : 0,
                CardCount = callerIsAdmin ? m.CardCount : 0,
                // Starting overtime balance is payroll data: the person and admins only
                InitialOvertimeHours = callerIsAdmin || m.Id == callerUserId.Value ? m.InitialOvertimeHours : 0,
                Groups = groupsByUser.GetValueOrDefault(m.Id) ?? new List<MemberGroupResponse>()
            };
        }).ToList();

        return ServiceResult.Ok(organization with { Members = membersWithVacation });
    }

    /// <summary>User IDs whose vacation balance the caller may see (same rule as vacation days, see AbsenceVisibility).</summary>
    private async Task<HashSet<int>> GetVacationVisibleUserIdsAsync(int callerUserId, int orgId)
    {
        var org = await _context.Organizations.AsNoTracking().FirstAsync(o => o.Id == orgId);
        return (await AbsenceVisibility.ForCallerAsync(_context, org, callerUserId)).Vacation;
    }

    public async Task<ServiceResult<List<UserOrganizationResponse>>> GetUserOrganizationsAsync(int callerUserId)
    {
        var organizations = await _context.UserOrganizations
            .Where(uo => uo.UserId == callerUserId)
            .Select(uo => new UserOrganizationResponse
            {
                OrganizationId = uo.Organization.Id,
                Name = uo.Organization.Name,
                Description = uo.Organization.Description,
                Slug = uo.Organization.Slug,
                Role = uo.Role.ToString(),
                JoinedAt = uo.JoinedAt,
                MemberCount = uo.Organization.UserOrganizations.Count(),
                Require2fa = uo.Organization.Require2fa
            })
            .ToListAsync();

        return ServiceResult.Ok(organizations);
    }

    public async Task<ServiceResult<OrganizationResponse>> CreateOrganizationAsync(
        int callerUserId, CreateOrganizationRequest request)
    {
        if (await _context.Organizations.AnyAsync(o => o.Slug == request.Slug))
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
            IsActive = true,
            // Statutory minimum breaks (§ 4 ArbZG) as a sensible default; admins can change them
            PauseRules =
            {
                new PauseRule { MinHours = 6, PauseMinutes = 30, CreatedAt = DateTime.UtcNow },
                new PauseRule { MinHours = 9, PauseMinutes = 45, CreatedAt = DateTime.UtcNow }
            }
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

        _logger.LogInformation("Organization {Slug} created by user {UserId}", organization.Slug, callerUserId);

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
            .FirstOrDefaultAsync(o => o.Slug == slug);

        if (organization == null)
            return ServiceResult.NotFound<OrganizationResponse>("Organization not found");

        var callerRole = await _context.GetOrgRoleAsync(callerUserId, organization.Id);
        if (callerRole == null || callerRole < OrganizationRole.Admin)
            return ServiceResult.Forbidden<OrganizationResponse>();

        if (request.Slug != null && request.Slug != organization.Slug)
        {
            if (await _context.Organizations.AnyAsync(o => o.Slug == request.Slug && o.Id != organization.Id))
                return ServiceResult.Conflict<OrganizationResponse>("An organization with this slug already exists");
        }

        if (request.Name != null) organization.Name = request.Name;
        if (request.Description != null) organization.Description = request.Description;
        if (request.Slug != null) organization.Slug = request.Slug;
        if (request.Website != null) organization.Website = request.Website;
        if (request.LogoUrl != null) organization.LogoUrl = request.LogoUrl;
        organization.UpdatedAt = DateTime.UtcNow;

        await _context.SaveChangesAsync();

        _logger.LogInformation("Organization {Slug} updated by user {UserId}", organization.Slug, callerUserId);

        return ServiceResult.Ok(new OrganizationResponse
        {
            Id = organization.Id,
            Name = organization.Name,
            Description = organization.Description,
            Slug = organization.Slug,
            Website = organization.Website,
            LogoUrl = organization.LogoUrl,
            CreatedAt = organization.CreatedAt,
            MemberCount = organization.UserOrganizations.Count(),
            JoinPolicy = organization.JoinPolicy.ToString()
        });
    }

    public async Task<ServiceResult> DeleteOrganizationAsync(string slug, int callerUserId)
    {
        var organization = await _context.Organizations
            .Include(o => o.UserOrganizations)
            .FirstOrDefaultAsync(o => o.Slug == slug);

        if (organization == null)
            return ServiceResult.NotFound("Organization not found");

        var callerRole = await _context.GetOrgRoleAsync(callerUserId, organization.Id);
        if (callerRole == null || callerRole != OrganizationRole.Owner)
            return ServiceResult.Forbidden();

        organization.IsActive = false;
        organization.UpdatedAt = DateTime.UtcNow;

        foreach (var membership in organization.UserOrganizations)
            membership.IsActive = false;

        await _context.SaveChangesAsync();

        _logger.LogInformation("Organization {Slug} soft-deleted by user {UserId}", slug, callerUserId);

        return ServiceResult.Ok();
    }

    // ────────────────────────────────────────────────────
    //  Member management
    // ────────────────────────────────────────────────────

    public async Task<ServiceResult<OrganizationMemberResponse>> UpdateMemberRoleAsync(
        string slug, int callerUserId, int userId, UpdateMemberRoleRequest request)
    {
        var org = await _context.FindOrgBySlugAsync(slug, tracked: true);
        if (org == null)
            return ServiceResult.NotFound<OrganizationMemberResponse>("Organization not found");

        var callerRole = await _context.GetOrgRoleAsync(callerUserId, org.Id);
        if (callerRole == null || callerRole < OrganizationRole.Admin)
            return ServiceResult.Forbidden<OrganizationMemberResponse>();

        var membership = await _context.UserOrganizations
            .Include(uo => uo.User)
            .FirstOrDefaultAsync(uo => uo.OrganizationId == org.Id && uo.UserId == userId);

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

        _logger.LogInformation("Member {UserId} role updated to {Role} in org {OrgId} by user {CallerId}", userId, membership.Role, org.Id, callerUserId);

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
        var org = await _context.FindOrgBySlugAsync(slug, tracked: true);
        if (org == null)
            return ServiceResult.NotFound("Organization not found");

        var callerRole = await _context.GetOrgRoleAsync(callerUserId, org.Id);
        if (callerRole == null)
            return ServiceResult.Forbidden();

        var membership = await _context.UserOrganizations
            .FirstOrDefaultAsync(uo => uo.OrganizationId == org.Id && uo.UserId == userId);

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
        // Leaving ends group roles too (they must not come back on rejoining) and frees the cards
        _context.BadgeCards.RemoveRange(_context.BadgeCards.Where(c => c.UserId == userId && c.OrganizationId == org.Id));
        _context.GroupMembers.RemoveRange(_context.GroupMembers.Where(gm => gm.UserId == userId && gm.Group.OrganizationId == org.Id));

        _logger.LogInformation("Member {UserId} removed from org {OrgId} by user {CallerId}", userId, org.Id, callerUserId);

        await _context.SaveChangesAsync();

        return ServiceResult.Ok();
    }

    // ────────────────────────────────────────────────────
    //  Settings
    // ────────────────────────────────────────────────────

    public async Task<ServiceResult<OrganizationSettingsResponse>> UpdateSettingsAsync(
        string slug, int callerUserId, UpdateOrganizationSettingsRequest request)
    {
        var org = await _context.FindOrgBySlugAsync(slug, tracked: true);
        if (org == null)
            return ServiceResult.NotFound<OrganizationSettingsResponse>("Organization not found");

        var callerRole = await _context.GetOrgRoleAsync(callerUserId, org.Id);
        if (callerRole == null || callerRole < OrganizationRole.Admin)
            return ServiceResult.Forbidden<OrganizationSettingsResponse>();

        if (request.AutoPauseEnabled.HasValue)   org.AutoPauseEnabled = request.AutoPauseEnabled.Value;
        if (request.EditPastEntriesMode.HasValue) org.EditPastEntriesMode = request.EditPastEntriesMode.Value;
        if (request.EditPauseMode.HasValue)       org.EditPauseMode = request.EditPauseMode.Value;
        if (request.InitialOvertimeMode.HasValue) org.InitialOvertimeMode = request.InitialOvertimeMode.Value;
        if (request.JoinPolicy.HasValue)          org.JoinPolicy = request.JoinPolicy.Value;
        if (request.WorkScheduleChangeMode.HasValue) org.WorkScheduleChangeMode = request.WorkScheduleChangeMode.Value;
        if (request.MemberTimeEntryVisibility.HasValue) org.MemberTimeEntryVisibility = request.MemberTimeEntryVisibility.Value;
        if (request.Require2fa == true && !org.Require2fa)
        {
            // Otherwise the admin would lock themselves out of the settings they just changed
            var callerHas2fa = await _context.Users.AnyAsync(u => u.Id == callerUserId && u.TwoFactorEnabled);
            if (!callerHas2fa)
                return ServiceResult.BadRequest<OrganizationSettingsResponse>("Enable two-factor authentication for your own account before requiring it for the organization.");
        }
        if (request.Require2fa.HasValue) org.Require2fa = request.Require2fa.Value;
        if (request.CsvImportMode.HasValue) org.CsvImportMode = request.CsvImportMode.Value;
        if (request.AbsenceSelfServiceMode.HasValue) org.AbsenceSelfServiceMode = request.AbsenceSelfServiceMode.Value;
        if (request.VacationVisibility.HasValue) org.VacationVisibility = request.VacationVisibility.Value;
        if (request.SickDayVisibility.HasValue) org.SickDayVisibility = request.SickDayVisibility.Value;
        if (request.DefaultVacationDays.HasValue) org.DefaultVacationDays = request.DefaultVacationDays.Value;
        if (request.BusinessDayEnd != null) org.BusinessDayEnd = TimeOnly.ParseExact(request.BusinessDayEnd, "HH:mm");
        if (request.TimeZone != null)
        {
            if (!TimeZoneInfo.TryFindSystemTimeZoneById(request.TimeZone, out _))
                return ServiceResult.BadRequest<OrganizationSettingsResponse>($"Unknown time zone '{request.TimeZone}'.");
            org.TimeZone = request.TimeZone;
        }

        org.UpdatedAt = DateTime.UtcNow;
        org.SettingsUpdatedAt = DateTime.UtcNow;
        await _context.SaveChangesAsync();

        _logger.LogInformation("Settings updated for organization {Slug} by user {UserId}", slug, callerUserId);

        // Notify all org members except the caller
        var memberUserIds = await _context.UserOrganizations
            .Where(uo => uo.OrganizationId == org.Id && uo.UserId != callerUserId)
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

        return ServiceResult.Ok(new OrganizationSettingsResponse
        {
            AutoPauseEnabled = org.AutoPauseEnabled,
            EditPastEntriesMode = org.EditPastEntriesMode.ToString(),
            EditPauseMode = org.EditPauseMode.ToString(),
            InitialOvertimeMode = org.InitialOvertimeMode.ToString(),
            JoinPolicy = org.JoinPolicy.ToString(),
            WorkScheduleChangeMode = org.WorkScheduleChangeMode.ToString(),
            MemberTimeEntryVisibility = org.MemberTimeEntryVisibility,
            Require2fa = org.Require2fa,
            CsvImportMode = org.CsvImportMode.ToString(),
            AbsenceSelfServiceMode = org.AbsenceSelfServiceMode.ToString(),
            BusinessDayEnd = org.BusinessDayEnd.ToString("HH:mm"),
            TimeZone = org.TimeZone
        });
    }

    // ────────────────────────────────────────────────────
    //  Time overview (admin)
    // ────────────────────────────────────────────────────

    public async Task<ServiceResult<List<MemberTimeOverviewResponse>>> GetTimeOverviewAsync(
        string slug, int callerUserId, DateTime? from, DateTime? to)
    {
        var org = await _context.FindOrgBySlugAsync(slug, tracked: true);
        if (org == null)
            return ServiceResult.NotFound<List<MemberTimeOverviewResponse>>("Organization not found");

        var callerRole = await _context.GetOrgRoleAsync(callerUserId, org.Id);
        if (callerRole == null)
            return ServiceResult.Forbidden<List<MemberTimeOverviewResponse>>();

        var isOrgAdmin = callerRole >= OrganizationRole.Admin;

        // Determine which members the caller can see
        List<int>? visibleMemberIds = null;
        if (isOrgAdmin && org.MemberTimeEntryVisibility)
        {
            // Org admin with visibility enabled → sees all
            visibleMemberIds = null; // null = all
        }
        else
        {
            // Check Group Admin with CanViewAllTimeEntries
            var groupAdminMemberIds = await GetGroupAdminVisibleMemberIdsAsync(callerUserId, org.Id);
            // Check regular group members with group-level TimeEntryVisibility = AllMembers
            var groupMemberIds = await GetGroupMemberTimeEntryVisibleUserIdsAsync(callerUserId, org.Id);
            var combined = groupAdminMemberIds.Union(groupMemberIds).Distinct().ToList();
            if (combined.Count == 0)
                return ServiceResult.Forbidden<List<MemberTimeOverviewResponse>>();
            visibleMemberIds = combined;
        }

        // Default: the current week in the organization's time zone
        var clock = OrgClock.For(org);
        var (localWeekStart, localWeekEnd) = clock.DayRangeUtc(null, null);
        var weekStart = from ?? localWeekStart;
        var weekEnd = to ?? localWeekEnd;

        var members = await _context.UserOrganizations
            .AsNoTracking()
            .Where(uo => uo.OrganizationId == org.Id)
            .Include(uo => uo.User)
            .ToListAsync();

        if (visibleMemberIds != null)
            members = members.Where(m => visibleMemberIds.Contains(m.UserId)).ToList();

        var memberIds = members.Select(m => m.UserId).ToList();

        var timeEntries = await _context.TimeEntries
            .AsNoTracking()
            .Where(te => memberIds.Contains(te.UserId)
                      && te.OrganizationId == org.Id
                      && !te.IsRunning
                      && te.StartTime >= weekStart
                      && te.StartTime < weekEnd)
            .ToListAsync();

        var today = clock.Today();
        var schedules = await _context.WorkSchedules
            .AsNoTracking()
            .Where(s => memberIds.Contains(s.UserId)
                     && s.OrganizationId == org.Id
                     && s.ValidFrom <= today
                     && (s.ValidTo == null || s.ValidTo >= today))
            .ToListAsync();

        var result = members.Select(m =>
        {
            var memberEntries = timeEntries.Where(te => te.UserId == m.UserId).ToList();
            var entries = memberEntries.Where(te => !te.IsFailure).ToList();
            var totalMinutes = entries.Sum(e =>
                e.EndTime.HasValue ? (e.EndTime.Value - e.StartTime).TotalMinutes : 0);
            // Net per local day, clamped at 0 like DailyReportAggregator, so both agree
            var netMinutes = entries.GroupBy(e => clock.LocalDate(e.StartTime)).Sum(day => Math.Max(0,
                day.Sum(e => e.EndTime.HasValue ? (e.EndTime.Value - e.StartTime).TotalMinutes : 0) - day.Sum(e => e.PauseDurationMinutes)));

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
                NetTrackedMinutes = Math.Round(netMinutes, 1),
                FailureEntryCount = memberEntries.Count - entries.Count,
                EntryCount = entries.Count
            };
        }).ToList();

        return ServiceResult.Ok(result);
    }

    public async Task<ServiceResult<PaginatedResponse<TimeEntryResponse>>> GetMemberEntriesAsync(
        string slug, int callerUserId, int userId, DateTime? from, DateTime? to, int limit, int offset)
    {
        var org = await _context.FindOrgBySlugAsync(slug, tracked: true);
        if (org == null)
            return ServiceResult.NotFound<PaginatedResponse<TimeEntryResponse>>("Organization not found");

        var callerRole = await _context.GetOrgRoleAsync(callerUserId, org.Id);
        if (callerRole == null)
            return ServiceResult.Forbidden<PaginatedResponse<TimeEntryResponse>>();

        var isOrgAdmin = callerRole >= OrganizationRole.Admin;

        if (isOrgAdmin && org.MemberTimeEntryVisibility)
        {
            // Org admin with visibility → allowed for any member
        }
        else
        {
            // Check Group Admin can view this specific member
            var groupAdminMemberIds = await GetGroupAdminVisibleMemberIdsAsync(callerUserId, org.Id);
            // Check regular group member with group-level TimeEntryVisibility = AllMembers
            var groupMemberIds = await GetGroupMemberTimeEntryVisibleUserIdsAsync(callerUserId, org.Id);
            if (!groupAdminMemberIds.Contains(userId) && !groupMemberIds.Contains(userId))
                return ServiceResult.Forbidden<PaginatedResponse<TimeEntryResponse>>();
        }

        var memberExists = await _context.UserOrganizations
            .AsNoTracking()
            .AnyAsync(uo => uo.OrganizationId == org.Id && uo.UserId == userId);
        if (!memberExists)
            return ServiceResult.NotFound<PaginatedResponse<TimeEntryResponse>>("Member not found in this organization.");

        (limit, offset) = PaginationDefaults.Normalize(limit, offset);

        // Default: the current week in the organization's time zone
        var (localWeekStart, localWeekEnd) = OrgClock.For(org).DayRangeUtc(null, null);
        var weekStart = from ?? localWeekStart;
        var weekEnd = to ?? localWeekEnd;

        var query = _context.TimeEntries
            .Where(te => te.UserId == userId
                      && te.OrganizationId == org.Id
                      && te.StartTime >= weekStart
                      && te.StartTime < weekEnd);

        var totalCount = await query.CountAsync();

        var entries = await query
            .OrderByDescending(te => te.StartTime)
            .Skip(offset)
            .Take(limit)
            .Select(te => new TimeEntryResponse
            {
                Id = te.Id,
                UserId = te.UserId,
                OrganizationId = te.OrganizationId,
                Description = te.Description,
                StartTime = te.StartTime,
                EndTime = te.EndTime,
                IsRunning = te.IsRunning,
                DurationMinutes = te.EndTime.HasValue && !te.IsFailure
                    ? Math.Round((te.EndTime.Value - te.StartTime).TotalMinutes, 1)
                    : (double?)null,
                PauseDurationMinutes = te.PauseDurationMinutes,
                NetDurationMinutes = te.EndTime.HasValue && !te.IsFailure
                    ? Math.Round((te.EndTime.Value - te.StartTime).TotalMinutes - te.PauseDurationMinutes, 1)
                    : (double?)null,
                IsFailure = te.IsFailure,
                CreatedAt = te.CreatedAt,
                Source = te.Source.ToString()
            })
            .ToListAsync();

        return ServiceResult.Ok(new PaginatedResponse<TimeEntryResponse>
        {
            Items = await TerminalNames.FillAsync(_context, entries),
            TotalCount = totalCount,
            Limit = limit,
            Offset = offset
        });
    }

    // ────────────────────────────────────────────────────
    //  Initial overtime (admin)
    // ────────────────────────────────────────────────────

    public async Task<ServiceResult<MemberInitialOvertimeResponse>> SetMemberInitialOvertimeAsync(
        string slug, int callerUserId, int userId, SetInitialOvertimeRequest request)
    {
        var org = await _context.FindOrgBySlugAsync(slug, tracked: true);
        if (org == null)
            return ServiceResult.NotFound<MemberInitialOvertimeResponse>("Organization not found");

        var callerRole = await _context.GetOrgRoleAsync(callerUserId, org.Id);
        if (callerRole == null || callerRole < OrganizationRole.Admin)
            return ServiceResult.Forbidden<MemberInitialOvertimeResponse>();

        var membership = await _context.UserOrganizations
            .FirstOrDefaultAsync(uo => uo.OrganizationId == org.Id && uo.UserId == userId);

        if (membership == null)
            return ServiceResult.NotFound<MemberInitialOvertimeResponse>("Member not found in this organization.");

        membership.InitialOvertimeHours = request.InitialOvertimeHours;
        await _context.SaveChangesAsync();

        _logger.LogInformation("Initial overtime set to {Hours}h for user {UserId} in org {OrgId} by user {CallerId}", request.InitialOvertimeHours, userId, org.Id, callerUserId);

        return ServiceResult.Ok(new MemberInitialOvertimeResponse { UserId = userId, InitialOvertimeHours = membership.InitialOvertimeHours });
    }

    public async Task<ServiceResult<MemberVacationDaysResponse>> SetMemberVacationDaysAsync(
        string slug, int callerUserId, int userId, SetVacationDaysRequest request)
    {
        var org = await _context.FindOrgBySlugAsync(slug, tracked: true);
        if (org == null)
            return ServiceResult.NotFound<MemberVacationDaysResponse>("Organization not found");

        var callerRole = await _context.GetOrgRoleAsync(callerUserId, org.Id);
        if (callerRole == null || callerRole < OrganizationRole.Admin)
            return ServiceResult.Forbidden<MemberVacationDaysResponse>();

        var membership = await _context.UserOrganizations
            .FirstOrDefaultAsync(uo => uo.OrganizationId == org.Id && uo.UserId == userId);

        if (membership == null)
            return ServiceResult.NotFound<MemberVacationDaysResponse>("Member not found in this organization.");

        membership.VacationDaysPerYear = request.Days;
        await _context.SaveChangesAsync();

        _logger.LogInformation("Vacation days set to {Days} for user {UserId} in org {OrgId} by user {CallerId}",
            request.Days, userId, org.Id, callerUserId);

        return ServiceResult.Ok(new MemberVacationDaysResponse { UserId = userId, VacationDaysPerYear = membership.VacationDaysPerYear });
    }

    // ────────────────────────────────────────────────────
    //  Private helpers
    // ────────────────────────────────────────────────────

    /// <summary>
    /// Returns the set of user IDs that a Group Admin can view time entries for,
    /// based on groups where they are Admin and CanViewAllTimeEntries is enabled.
    /// </summary>
    private async Task<List<int>> GetGroupAdminVisibleMemberIdsAsync(int callerUserId, int orgId)
    {
        // Find groups in this org where the caller is a Group Admin AND CanViewAllTimeEntries is true
        var adminGroupIds = await _context.GroupMembers
            .Include(gm => gm.Group)
            .Where(gm => gm.UserId == callerUserId
                      && gm.Role == GroupRole.Admin
                      && gm.Group.OrganizationId == orgId
                      && gm.Group.IsActive
                      && gm.Group.GroupAdminCanViewAllTimeEntries == true)
            .Select(gm => gm.GroupId)
            .ToListAsync();

        if (adminGroupIds.Count == 0)
            return new List<int>();

        // Get all member user IDs from those groups
        return await _context.GroupMembers
            .Where(gm => adminGroupIds.Contains(gm.GroupId))
            .Select(gm => gm.UserId)
            .Distinct()
            .ToListAsync();
    }

    /// <summary>
    /// Returns user IDs from groups where the caller is a member and group-level TimeEntryVisibility = AllMembers.
    /// </summary>
    private async Task<List<int>> GetGroupMemberTimeEntryVisibleUserIdsAsync(int callerUserId, int orgId)
    {
        var groupIds = await _context.GroupMembers
            .Include(gm => gm.Group)
            .Where(gm => gm.UserId == callerUserId
                      && gm.Group.OrganizationId == orgId
                      && gm.Group.IsActive
                      && gm.Group.TimeEntryVisibility == VisibilityMode.AllMembers)
            .Select(gm => gm.GroupId)
            .ToListAsync();

        if (groupIds.Count == 0)
            return new List<int>();

        return await _context.GroupMembers
            .Where(gm => groupIds.Contains(gm.GroupId))
            .Select(gm => gm.UserId)
            .Distinct()
            .ToListAsync();
    }
}
