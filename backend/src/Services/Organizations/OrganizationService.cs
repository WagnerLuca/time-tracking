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

    public async Task<ServiceResult<PaginatedResponse<OrganizationResponse>>> GetOrganizationsAsync(int limit, int offset, int? callerUserId = null)
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

        // In the caller's own organizations the count follows the visibility matrix (no hidden people)
        if (callerUserId is int caller)
        {
            var counts = await VisibleMemberCountsAsync(caller, organizations.Select(o => o.Id));
            organizations = organizations.Select(o => counts.TryGetValue(o.Id, out var c) ? o with { MemberCount = c } : o).ToList();
        }

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
                DefaultVacationDays = o.DefaultVacationDays,
                BusinessDayEnd = o.BusinessDayEnd.ToString("HH:mm"),
                TimeZone = o.TimeZone,
                CoreHoursEnabled = o.CoreHoursEnabled,
                CoreHoursStart = o.CoreHoursStart.ToString("HH:mm"),
                CoreHoursEnd = o.CoreHoursEnd.ToString("HH:mm"),
                CoreHoursToleranceMinutes = o.CoreHoursToleranceMinutes,
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

        // The member visibility matrix decides who is listed at all and which details are filled in
        var org = await _context.Organizations.AsNoTracking().FirstAsync(o => o.Id == organization.Id);
        var visibility = await MemberVisibility.ForCallerAsync(_context, org, callerUserId!.Value);
        var visibleMembers = organization.Members.Where(m => visibility.Can(m.Id, MemberInfo.Member)).ToList();

        // Compute vacation days used for each member (current year, in the org's time zone)
        var today = OrgClock.For(organization.TimeZone).Today();
        var currentYear = today.Year;
        var yearStart = new DateOnly(currentYear, 1, 1);
        var yearEnd = new DateOnly(currentYear, 12, 31);
        var memberIds = visibility.Ids(MemberInfo.VacationBalance).ToList();

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

        // Presence: who has a timer running in this organization right now
        var presenceIds = visibility.Ids(MemberInfo.Presence).ToList();
        var present = (await _context.TimeEntries.AsNoTracking()
                .Where(te => te.OrganizationId == organization.Id && te.IsRunning && presenceIds.Contains(te.UserId))
                .Select(te => te.UserId)
                .ToListAsync())
            .ToHashSet();

        var callerIsAdmin = visibility.CallerIsOrgAdmin;

        var membersWithVacation = visibleMembers.Select(m =>
        {
            var can = visibility.Of(m.Id);
            var visible = can.HasFlag(MemberInfo.VacationBalance);
            var usage = usageByUser.GetValueOrDefault(m.Id);
            var taken = usage?.Taken ?? 0;
            var planned = usage?.Planned ?? 0;
            var used = taken + planned;
            return m with
            {
                Email = can.HasFlag(MemberInfo.Email) ? m.Email : null,
                Role = can.HasFlag(MemberInfo.Groups) ? m.Role : null,
                VacationVisible = visible,
                VacationDaysPerYear = visible ? m.VacationDaysPerYear : 0,
                VacationDaysUsed = visible ? used : 0,
                VacationDaysTaken = visible ? taken : 0,
                VacationDaysPlanned = visible ? planned : 0,
                VacationDaysRemaining = visible ? Math.Max(0, m.VacationDaysPerYear - used) : 0,
                CardCount = callerIsAdmin ? m.CardCount : 0,
                // Starting overtime balance is payroll data: the person and admins only
                InitialOvertimeHours = callerIsAdmin || m.Id == callerUserId.Value ? m.InitialOvertimeHours : 0,
                Groups = can.HasFlag(MemberInfo.Groups) ? groupsByUser.GetValueOrDefault(m.Id) ?? new List<MemberGroupResponse>() : new List<MemberGroupResponse>(),
                VisibleInfo = MemberVisibility.Kinds.Where(k => can.HasFlag(k)).Select(k => k.ToString()).ToList(),
                IsPresent = can.HasFlag(MemberInfo.Presence) ? present.Contains(m.Id) : null
            };
        }).ToList();

        return ServiceResult.Ok(organization with
        {
            Members = membersWithVacation,
            Visibility = MemberVisibility.Rows(org.VisibleInGroups, org.VisibleOutsideGroups)
        });
    }

    /// <summary>Per organization (of the given ones the caller belongs to): how many members the caller can see.</summary>
    private async Task<Dictionary<int, int>> VisibleMemberCountsAsync(int callerUserId, IEnumerable<int> orgIds)
    {
        var ids = orgIds.ToList();
        var orgs = await _context.Organizations.AsNoTracking()
            .Where(o => ids.Contains(o.Id) && o.UserOrganizations.Any(uo => uo.UserId == callerUserId))
            .ToListAsync();
        var counts = new Dictionary<int, int>();
        foreach (var org in orgs)
            counts[org.Id] = (await MemberVisibility.ForCallerAsync(_context, org, callerUserId)).Ids(MemberInfo.Member).Count;
        return counts;
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

        var counts = await VisibleMemberCountsAsync(callerUserId, organizations.Select(o => o.OrganizationId));
        return ServiceResult.Ok(organizations.Select(o => o with { MemberCount = counts.GetValueOrDefault(o.OrganizationId, o.MemberCount) }).ToList());
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

    public const string CoreHoursOrderMessage = "Core working hours must start before they end.";

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
        if (request.Visibility != null && MemberVisibility.Apply(org, request.Visibility) is { } visibilityError)
            return ServiceResult.BadRequest<OrganizationSettingsResponse>(visibilityError);
        if (request.DefaultVacationDays.HasValue) org.DefaultVacationDays = request.DefaultVacationDays.Value;
        if (request.BusinessDayEnd != null) org.BusinessDayEnd = TimeOnly.ParseExact(request.BusinessDayEnd, "HH:mm");
        if (request.TimeZone != null)
        {
            if (!TimeZoneInfo.TryFindSystemTimeZoneById(request.TimeZone, out _))
                return ServiceResult.BadRequest<OrganizationSettingsResponse>($"Unknown time zone '{request.TimeZone}'.");
            org.TimeZone = request.TimeZone;
        }
        if (request.CoreHoursEnabled.HasValue) org.CoreHoursEnabled = request.CoreHoursEnabled.Value;
        if (request.CoreHoursStart != null) org.CoreHoursStart = TimeOnly.ParseExact(request.CoreHoursStart, "HH:mm");
        if (request.CoreHoursEnd != null) org.CoreHoursEnd = TimeOnly.ParseExact(request.CoreHoursEnd, "HH:mm");
        if (request.CoreHoursToleranceMinutes.HasValue) org.CoreHoursToleranceMinutes = request.CoreHoursToleranceMinutes.Value;
        if (org.CoreHoursStart >= org.CoreHoursEnd)
            return ServiceResult.BadRequest<OrganizationSettingsResponse>(CoreHoursOrderMessage);

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
            Visibility = MemberVisibility.Rows(org.VisibleInGroups, org.VisibleOutsideGroups),
            BusinessDayEnd = org.BusinessDayEnd.ToString("HH:mm"),
            TimeZone = org.TimeZone,
            CoreHoursEnabled = org.CoreHoursEnabled,
            CoreHoursStart = org.CoreHoursStart.ToString("HH:mm"),
            CoreHoursEnd = org.CoreHoursEnd.ToString("HH:mm"),
            CoreHoursToleranceMinutes = org.CoreHoursToleranceMinutes
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

        // Admins (while the org lets them see members' hours) see all; everyone else the people whose
        // tracked times the visibility matrix or their group admin rights show them
        HashSet<int>? visibleMemberIds = null;
        if (!(callerRole >= OrganizationRole.Admin && org.MemberTimeEntryVisibility))
        {
            visibleMemberIds = (await MemberVisibility.ForCallerAsync(_context, org, callerUserId)).Ids(MemberInfo.TrackedTime);
            if (!visibleMemberIds.Any(id => id != callerUserId))
                return ServiceResult.Forbidden<List<MemberTimeOverviewResponse>>();
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

        // A hidden person does not exist for the caller; a visible one's hours may still be hidden
        var visibility = await MemberVisibility.ForCallerAsync(_context, org, callerUserId);
        if (!visibility.Can(userId, MemberInfo.Member))
            return ServiceResult.NotFound<PaginatedResponse<TimeEntryResponse>>("Member not found in this organization.");
        if (!visibility.Can(userId, MemberInfo.TrackedTime))
            return ServiceResult.Forbidden<PaginatedResponse<TimeEntryResponse>>();

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
}
