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
                JoinPolicy = o.JoinPolicy.ToString()
            })
            .ToListAsync();

        // In the caller's own organizations the count follows the visibility matrix (no hidden people);
        // other organizations' sizes are not the caller's business (#63)
        var counts = callerUserId is int caller
            ? await VisibleMemberCountsAsync(caller, organizations.Select(o => o.Id))
            : new Dictionary<int, int>();
        organizations = organizations.Select(o => o with { MemberCount = counts.TryGetValue(o.Id, out var c) ? c : null }).ToList();

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
                TerminalDoubleScanSeconds = o.TerminalDoubleScanSeconds,
                TerminalOfflineHours = o.TerminalOfflineHours,
                TerminalUnknownCardLimit = o.TerminalUnknownCardLimit,
                SettingsUpdatedAt = o.SettingsUpdatedAt,
                CreatedAt = o.CreatedAt,
                ClosedMonths = _context.ClosedPeriods.Where(p => p.OrganizationId == o.Id).OrderBy(p => p.Month).Select(p => p.Month).ToList(),
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
                        PersonnelNumber = uo.PersonnelNumber,
                        IsAccountant = uo.IsAccountant,
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

        // Vacation days used for each member (current year, in the org's time zone)
        var today = OrgClock.For(organization.TimeZone).Today();
        var memberIds = visibility.Ids(MemberInfo.VacationBalance).ToList();
        var usageByUser = await VacationBalances.UsageAsync(_context, organization.Id, memberIds, today.Year, today);

        // Group memberships for tag display (e.g. "Frontend Admin"): direct ones, then the
        // ancestors of those groups as inherited memberships ("über Team Küche")
        var tree = visibility.Tree;
        var groupsByUser = visibleMembers.ToDictionary(m => m.Id, m =>
        {
            var direct = tree.MembershipsOf(m.Id).ToList();
            var result = direct.Select(gm => tree.Find(gm.GroupId)!).Select((g, i) => new MemberGroupResponse
            {
                GroupId = g.Id, Name = g.Name, Color = g.Color, IsGroupAdmin = direct[i].Role == GroupRole.Admin
            }).OrderBy(x => x.Name).ToList();
            var seen = result.Select(x => x.GroupId).ToHashSet();
            foreach (var gm in direct.OrderBy(gm => tree.Find(gm.GroupId)!.Name))
                foreach (var id in tree.Chain(gm.GroupId).Skip(1).Where(seen.Add))
                {
                    var g = tree.Find(id)!;
                    result.Add(new MemberGroupResponse { GroupId = g.Id, Name = g.Name, Color = g.Color, ViaGroupName = tree.Find(gm.GroupId)!.Name });
                }
            return result;
        });

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
                Role = m.Role,   // the org role is shown whenever the person is (#63)
                VacationVisible = visible,
                VacationDaysPerYear = visible ? m.VacationDaysPerYear : 0,
                VacationDaysUsed = visible ? used : 0,
                VacationDaysTaken = visible ? taken : 0,
                VacationDaysPlanned = visible ? planned : 0,
                VacationDaysRemaining = visible ? VacationBalances.Remaining(m.VacationDaysPerYear, usage) : 0,
                CardCount = callerIsAdmin ? m.CardCount : 0,
                PersonnelNumber = callerIsAdmin ? m.PersonnelNumber : null,
                // Starting overtime balance is payroll data: the person and admins only
                InitialOvertimeHours = callerIsAdmin || m.Id == callerUserId.Value ? m.InitialOvertimeHours : 0,
                Groups = can.HasFlag(MemberInfo.Groups) ? groupsByUser.GetValueOrDefault(m.Id) ?? new List<MemberGroupResponse>() : new List<MemberGroupResponse>(),
                VisibleInfo = MemberVisibility.Kinds.Where(k => can.HasFlag(k)).Select(k => k.ToString()).ToList(),
                IsPresent = can.HasFlag(MemberInfo.Presence) ? present.Contains(m.Id) : null,
                CanCorrectEntries = m.Id != callerUserId.Value && Enum.TryParse<OrganizationRole>(m.Role, out var memberRole)
                    && MemberEntryService.MayCorrect(callerIsAdmin, callerUserId.Value, m.Id, memberRole, tree)
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
                IsAccountant = uo.IsAccountant,
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
        // Leaving ends the accounting role too; a rejoin starts without it
        membership.IsAccountant = false;
        // The number goes free for the next hire; payroll exports list active members only, so no
        // past period needs it (the unique index would otherwise keep holding it on the inactive row)
        membership.PersonnelNumber = null;
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

    public const string CoreHoursOrderMessage = "Core working hours need a valid start and a different end.";
    public const string CoreHoursBackfillMessage = "Past days to check must be between 1 and 31.";

    /// <summary>
    /// A core hours setting (org or group) changed: stamps <see cref="Organization.CoreHoursChangedAt"/> (late
    /// catch-up checks never reach back past it) and, with <paramref name="backfillDays"/>, asks the background
    /// check to flag the last N days silently (<see cref="CoreHours.CheckDueAsync"/>). Enqueued rather than run in
    /// the request: the next minute's loop does it with the same batched evaluation, as system work (RLS).
    /// </summary>
    internal static void CoreHoursChanged(Organization org, int? backfillDays)
    {
        var now = DateTime.UtcNow;
        org.CoreHoursChangedAt = now;
        if (backfillDays is not { } days) return;
        var from = OrgClock.For(org).Today(now).AddDays(-days);
        if (org.CoreHoursBackfillFrom == null || from < org.CoreHoursBackfillFrom) org.CoreHoursBackfillFrom = from;
    }

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
        var coreBefore = (org.CoreHoursEnabled, org.CoreHoursStart, org.CoreHoursEnd, org.CoreHoursToleranceMinutes);
        if (request.CoreHoursEnabled.HasValue) org.CoreHoursEnabled = request.CoreHoursEnabled.Value;
        if (request.CoreHoursStart != null) org.CoreHoursStart = TimeOnly.ParseExact(request.CoreHoursStart, "HH:mm");
        if (request.CoreHoursEnd != null) org.CoreHoursEnd = TimeOnly.ParseExact(request.CoreHoursEnd, "HH:mm");
        if (request.CoreHoursToleranceMinutes.HasValue) org.CoreHoursToleranceMinutes = request.CoreHoursToleranceMinutes.Value;
        // End before start = a night window past midnight; only an empty window is invalid
        if (org.CoreHoursStart == org.CoreHoursEnd)
            return ServiceResult.BadRequest<OrganizationSettingsResponse>(CoreHoursOrderMessage);
        if (request.CoreHoursBackfillDays is < 1 or > CoreHours.MaxBackfillDays)
            return ServiceResult.BadRequest<OrganizationSettingsResponse>(CoreHoursBackfillMessage);
        if (coreBefore != (org.CoreHoursEnabled, org.CoreHoursStart, org.CoreHoursEnd, org.CoreHoursToleranceMinutes) || request.CoreHoursBackfillDays != null)
            CoreHoursChanged(org, org.CoreHoursEnabled ? request.CoreHoursBackfillDays : null);
        if (request.TerminalDoubleScanSeconds is { } doubleScan)
        {
            if (doubleScan is < TerminalLimits.MinDoubleScanSeconds or > TerminalLimits.MaxDoubleScanSeconds)
                return ServiceResult.BadRequest<OrganizationSettingsResponse>("The double-scan window must be between 5 and 600 seconds.");
            org.TerminalDoubleScanSeconds = doubleScan;
        }
        if (request.TerminalOfflineHours is { } offline)
        {
            if (offline is < TerminalLimits.MinOfflineHours or > TerminalLimits.MaxOfflineHours)
                return ServiceResult.BadRequest<OrganizationSettingsResponse>("The offline window must be between 1 and 168 hours.");
            org.TerminalOfflineHours = offline;
        }
        if (request.TerminalUnknownCardLimit is { } unknown)
        {
            if (unknown is < TerminalLimits.MinUnknownCardLimit or > TerminalLimits.MaxUnknownCardLimit)
                return ServiceResult.BadRequest<OrganizationSettingsResponse>("The unknown-card budget must be between 5 and 200 scans per 10 minutes.");
            org.TerminalUnknownCardLimit = unknown;
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
            Visibility = MemberVisibility.Rows(org.VisibleInGroups, org.VisibleOutsideGroups),
            BusinessDayEnd = org.BusinessDayEnd.ToString("HH:mm"),
            TimeZone = org.TimeZone,
            CoreHoursEnabled = org.CoreHoursEnabled,
            CoreHoursStart = org.CoreHoursStart.ToString("HH:mm"),
            CoreHoursEnd = org.CoreHoursEnd.ToString("HH:mm"),
            CoreHoursToleranceMinutes = org.CoreHoursToleranceMinutes,
            TerminalDoubleScanSeconds = org.TerminalDoubleScanSeconds,
            TerminalOfflineHours = org.TerminalOfflineHours,
            TerminalUnknownCardLimit = org.TerminalUnknownCardLimit
        });
    }

    // ────────────────────────────────────────────────────
    //  Team overview (dashboard, issue #61)
    // ────────────────────────────────────────────────────

    public async Task<ServiceResult<TeamOverviewResponse>> GetTeamOverviewAsync(string slug, int callerUserId)
    {
        var org = await _context.FindOrgBySlugAsync(slug);
        if (org == null)
            return ServiceResult.NotFound<TeamOverviewResponse>("Organization not found");

        // Org admins lead everyone, group admins the members of the groups they administer and of their sub-groups
        var visibility = await MemberVisibility.ForCallerAsync(_context, org, callerUserId);
        var led = visibility.AdministeredGroupIds;
        if (!visibility.CallerIsOrgAdmin && led.Count == 0)
            return ServiceResult.Forbidden<TeamOverviewResponse>();

        var members = (await _context.UserOrganizations.AsNoTracking()
                .Where(uo => uo.OrganizationId == org.Id && uo.UserId != callerUserId && uo.User.IsActive)
                .Select(uo => new { uo.UserId, uo.User.FirstName, uo.User.LastName, uo.User.ProfileImageUrl, uo.InitialOvertimeHours })
                .ToListAsync())
            .Where(m => visibility.Can(m.UserId, MemberInfo.Member)
                     && (visibility.CallerIsOrgAdmin || visibility.GroupsOf(m.UserId).Any(g => led.Contains(g.Id))))
            .ToList();

        var groups = members.SelectMany(m => visibility.GroupsOf(m.UserId))
            .Where(g => visibility.CallerIsOrgAdmin || led.Contains(g.Id))
            .DistinctBy(g => g.Id)
            .OrderBy(g => g.Name)
            .Select(g => new TeamGroupResponse(g.Id, g.Name))
            .ToList();
        var groupIds = groups.Select(g => g.Id).ToHashSet();

        var timeIds = members.Where(m => visibility.Can(m.UserId, MemberInfo.TrackedTime)).Select(m => m.UserId).ToList();
        var presenceIds = members.Where(m => visibility.Can(m.UserId, MemberInfo.Presence)).Select(m => m.UserId).ToHashSet();
        var clock = OrgClock.For(org);
        var today = clock.Today();
        var entries = await DailyReportAggregator.LoadEntriesAsync(_context, org.Id,
            timeIds.Union(presenceIds).ToList(), clock.StartOfDayUtc(today.AddDays(1)));
        var running = RunningSince(entries);
        var failures = entries.Where(e => e.IsFailure).GroupBy(e => e.UserId).ToDictionary(g => g.Key, g => g.Count());
        var balances = await DailyReportAggregator.BalancesAsync(_context, org,
            members.Where(m => timeIds.Contains(m.UserId)).ToDictionary(m => m.UserId, m => m.InitialOvertimeHours),
            entries.Where(e => timeIds.Contains(e.UserId)));
        var violations = await CoreHours.OpenViolationsAsync(_context, timeIds, org.Id, today, today);

        return ServiceResult.Ok(new TeamOverviewResponse
        {
            Groups = groups,
            Members = members.Select(m =>
            {
                var times = balances.TryGetValue(m.UserId, out var balance);
                var present = presenceIds.Contains(m.UserId) ? running.ContainsKey(m.UserId) : (bool?)null;
                return new TeamMemberResponse
                {
                    UserId = m.UserId,
                    FirstName = m.FirstName,
                    LastName = m.LastName,
                    ProfileImageUrl = m.ProfileImageUrl,
                    GroupIds = visibility.GroupsOf(m.UserId).Select(g => g.Id).Where(groupIds.Contains).ToList(),
                    BalanceHours = times ? Math.Round(balance, 2) : null,
                    Present = present,
                    PresentSince = present == true ? running[m.UserId] : null,
                    FailureCount = times ? failures.GetValueOrDefault(m.UserId) : null,
                    CoreHoursViolationToday = times ? violations.ContainsKey(m.UserId) : null
                };
            }).ToList()
        });
    }

    /// <summary>Presence (team overview, Organigramm): who runs a timer right now, since when — failure entries never count.</summary>
    private static Dictionary<int, DateTime> RunningSince(IEnumerable<TimeEntry> entries) =>
        entries.Where(e => e.IsRunning && !e.IsFailure).GroupBy(e => e.UserId).ToDictionary(g => g.Key, g => g.Min(e => e.StartTime));

    // ────────────────────────────────────────────────────
    //  Organigramm (issue #66)
    // ────────────────────────────────────────────────────

    /// <summary>
    /// Groups (with parents) and every person the caller may see, with their direct memberships and presence —
    /// 5 queries however large the organization: org, the matrix (members + group tree), names, running timers.
    /// Balances are not computed here (that would be the team overview's 4 extra queries over everyone's whole
    /// history on each open); the person card asks the balance endpoint for the one person clicked.
    /// </summary>
    public async Task<ServiceResult<OrgChartResponse>> GetOrgChartAsync(string slug, int callerUserId)
    {
        var org = await _context.FindOrgBySlugAsync(slug);
        if (org == null)
            return ServiceResult.NotFound<OrgChartResponse>("Organization not found");

        var visibility = await MemberVisibility.ForCallerAsync(_context, org, callerUserId);
        var tree = visibility.Tree;
        var visibleIds = visibility.Ids(MemberInfo.Member);
        if (!visibleIds.Contains(callerUserId))
            return ServiceResult.Forbidden<OrgChartResponse>();

        var people = (await _context.UserOrganizations.AsNoTracking()
                .Where(uo => uo.OrganizationId == org.Id && uo.User.IsActive)
                .Select(uo => new { uo.UserId, uo.User.FirstName, uo.User.LastName, uo.User.Email, uo.User.ProfileImageUrl, uo.Role })
                .ToListAsync())
            .Where(p => visibleIds.Contains(p.UserId))
            .OrderBy(p => p.LastName).ThenBy(p => p.FirstName)
            .ToList();

        var presenceIds = people.Select(p => p.UserId).Where(id => visibility.Can(id, MemberInfo.Presence)).ToList();
        var running = RunningSince(await _context.TimeEntries.AsNoTracking()
            .Where(e => e.OrganizationId == org.Id && e.IsRunning && presenceIds.Contains(e.UserId))
            .Select(e => new TimeEntry { UserId = e.UserId, StartTime = e.StartTime, IsRunning = e.IsRunning, IsFailure = e.IsFailure })
            .ToListAsync());

        return ServiceResult.Ok(new OrgChartResponse
        {
            OrganizationName = org.Name,
            Groups = tree.Groups.OrderBy(g => g.Name).Select(g => new OrgChartGroupResponse(
                g.Id, g.Name, g.Description, g.Color, tree.ParentOf(g.Id),
                visibility.CallerIsOrgAdmin || tree.AdministersWith(callerUserId, g.Id, x => x.GroupAdminCanManageMembers == true))).ToList(),
            People = people.Select(p =>
            {
                var can = visibility.Of(p.UserId);
                var present = can.HasFlag(MemberInfo.Presence) ? running.ContainsKey(p.UserId) : (bool?)null;
                return new OrgChartPersonResponse
                {
                    UserId = p.UserId,
                    FirstName = p.FirstName,
                    LastName = p.LastName,
                    ProfileImageUrl = p.ProfileImageUrl,
                    Role = p.Role.ToString(),
                    Email = can.HasFlag(MemberInfo.Email) ? p.Email : null,
                    Groups = can.HasFlag(MemberInfo.Groups)
                        ? tree.MembershipsOf(p.UserId).Select(m => new OrgChartMembershipResponse(m.GroupId, m.Role == GroupRole.Admin)).ToList()
                        : null,
                    Present = present,
                    PresentSince = present == true ? running[p.UserId] : null,
                    VisibleInfo = MemberVisibility.Kinds.Where(k => can.HasFlag(k)).Select(k => k.ToString()).ToList()
                };
            }).ToList()
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
        var visibility = await MemberVisibility.ForCallerAsync(_context, org, callerUserId);
        HashSet<int>? visibleMemberIds = null;
        if (!(callerRole >= OrganizationRole.Admin && org.MemberTimeEntryVisibility))
        {
            visibleMemberIds = visibility.Ids(MemberInfo.TrackedTime);
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
                Email = visibility.Can(m.UserId, MemberInfo.Email) ? m.User.Email : null,
                Role = m.Role.ToString(),
                WeeklyWorkHours = visibility.Can(m.UserId, MemberInfo.WorkSchedule) ? schedule?.WeeklyWorkHours : null,
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

    public async Task<ServiceResult<MemberPersonnelNumberResponse>> SetMemberPersonnelNumberAsync(
        string slug, int callerUserId, int userId, SetPersonnelNumberRequest request)
    {
        var org = await _context.FindOrgBySlugAsync(slug);
        if (org == null)
            return ServiceResult.NotFound<MemberPersonnelNumberResponse>("Organization not found");
        if (await _context.GetOrgRoleAsync(callerUserId, org.Id) is not >= OrganizationRole.Admin)
            return ServiceResult.Forbidden<MemberPersonnelNumberResponse>();

        var membership = await _context.UserOrganizations
            .FirstOrDefaultAsync(uo => uo.OrganizationId == org.Id && uo.UserId == userId);
        if (membership == null)
            return ServiceResult.NotFound<MemberPersonnelNumberResponse>("Member not found in this organization.");

        var number = string.IsNullOrWhiteSpace(request.PersonnelNumber) ? null : request.PersonnelNumber.Trim();
        const string taken = "This personnel number is already used by another member.";
        // Inactive memberships keep theirs, and the unique index covers them too
        if (number != null && await _context.UserOrganizations.IgnoreQueryFilters()
                .AnyAsync(uo => uo.OrganizationId == org.Id && uo.UserId != userId && uo.PersonnelNumber == number))
            return ServiceResult.Conflict<MemberPersonnelNumberResponse>(taken);

        membership.PersonnelNumber = number;
        try
        {
            await _context.SaveChangesAsync();
        }
        catch (DbUpdateException ex) when (DbErrors.IsUniqueViolation(ex))
        {
            return ServiceResult.Conflict<MemberPersonnelNumberResponse>(taken);   // another admin gave it away at the same time
        }
        _logger.LogInformation("Personnel number of user {UserId} in org {OrgId} set by user {CallerId}", userId, org.Id, callerUserId);
        return ServiceResult.Ok(new MemberPersonnelNumberResponse { UserId = userId, PersonnelNumber = number });
    }

    public const string AccountantOwnerOnlyMessage = "Only the owner can change the accounting role of admins or of yourself.";

    /// <summary>
    /// Grants or revokes the accounting role: the Owner for anyone, admins for members only — not for
    /// themselves or other admins, so an admin cannot lift "admins may see members' hours" for themselves.
    /// </summary>
    public async Task<ServiceResult<MemberAccountantResponse>> SetMemberAccountantAsync(
        string slug, int callerUserId, int userId, SetAccountantRequest request)
    {
        var org = await _context.FindOrgBySlugAsync(slug);
        if (org == null) return ServiceResult.NotFound<MemberAccountantResponse>("Organization not found");
        var callerRole = await _context.GetOrgRoleAsync(callerUserId, org.Id);
        if (callerRole is not >= OrganizationRole.Admin) return ServiceResult.Forbidden<MemberAccountantResponse>();

        var membership = await _context.UserOrganizations.FirstOrDefaultAsync(uo => uo.OrganizationId == org.Id && uo.UserId == userId);
        if (membership == null) return ServiceResult.NotFound<MemberAccountantResponse>("Member not found in this organization.");
        if (callerRole != OrganizationRole.Owner && (userId == callerUserId || membership.Role >= OrganizationRole.Admin))
            return ServiceResult.Forbidden<MemberAccountantResponse>(AccountantOwnerOnlyMessage, "accounting.ownerOnly");

        if (membership.IsAccountant != request.IsAccountant)
        {
            membership.IsAccountant = request.IsAccountant;
            await _context.SaveChangesAsync();
            _logger.LogInformation("Accounting role of user {UserId} in org {OrgId} set to {Value} by user {CallerId}",
                userId, org.Id, request.IsAccountant, callerUserId);
        }
        return ServiceResult.Ok(new MemberAccountantResponse { UserId = userId, IsAccountant = membership.IsAccountant });
    }
}
