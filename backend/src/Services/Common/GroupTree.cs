using Microsoft.EntityFrameworkCore;
using TimeTracking.Api.Data;
using TimeTracking.Api.Models;

namespace TimeTracking.Api.Services;

/// <summary>
/// The one resolution helper for sub-groups (issue #59): the active groups of one organization
/// with their direct memberships, loaded in a single query (<see cref="LoadAsync"/>), answering
/// <list type="bullet">
/// <item><b>Effective settings</b> (<see cref="Effective"/>): a group's setting left on "inherit"
/// takes its parent's effective value, up the chain; at the top "inherit" stays null = the
/// organization default (and for group-admin capabilities: disabled). Applies to the rule modes,
/// the visibility matrix (per kind and column), core hours (mode + window as one setting) and the
/// group-admin capabilities. Everything that combines a member's groups (<see cref="EffectiveRuleResolver.Combine{T}"/>,
/// <see cref="EffectiveRuleResolver.CombineCoreHours"/>, <see cref="MemberVisibility.Combine"/>) is fed
/// the <i>effective</i> groups of the member's direct memberships (<see cref="EffectiveGroupsOf"/>),
/// so an override in the middle of the chain wins over its ancestors and "most permissive wins"
/// still applies across branches.</item>
/// <item><b>Effective membership</b> (<see cref="ChainGroupsOf"/>): a member of a sub-group counts
/// as a member of every ancestor group (for "in own groups", routing and notifications) without
/// being stored there.</item>
/// <item><b>Admins along the chain</b> (<see cref="AdministersWith"/>, <see cref="ActsFor"/>): an admin
/// of a group acts for the members of all its sub-groups, per that group's effective capabilities.</item>
/// </list>
/// Build it once per request; every lookup is in memory.
/// </summary>
public sealed class GroupTree
{
    /// <summary>Levels including the top level (department → team → sub-team).</summary>
    public const int MaxDepth = 3;

    private readonly Dictionary<int, Group> _groups;
    private readonly Dictionary<int, int> _parent = new();
    private readonly ILookup<int, int> _children;
    private readonly ILookup<int, GroupMember> _byUser;
    private readonly Dictionary<int, Group> _effective = new();

    public GroupTree(IEnumerable<Group> activeGroups)
    {
        _groups = activeGroups.ToDictionary(g => g.Id);
        foreach (var g in _groups.Values)
            if (g.ParentGroupId is { } p && _groups.ContainsKey(p)) _parent[g.Id] = p;
        // Defensive: a cycle that slipped into the data is cut, so every walk terminates
        foreach (var id in _groups.Keys)
        {
            var seen = new HashSet<int> { id };
            for (var cur = id; _parent.TryGetValue(cur, out var p); cur = p)
                if (!seen.Add(p)) { _parent.Remove(cur); break; }
        }
        _children = _parent.ToLookup(kv => kv.Value, kv => kv.Key);
        _byUser = _groups.Values.SelectMany(g => g.Members).ToLookup(m => m.UserId);
    }

    /// <summary>The organization's active groups with their memberships (one query).</summary>
    public static async Task<GroupTree> LoadAsync(TimeTrackingDbContext context, int orgId, CancellationToken ct = default) =>
        new(await context.Groups.AsNoTracking()
            .Where(g => g.OrganizationId == orgId && g.IsActive)
            .Include(g => g.Members)
            .ToListAsync(ct));

    public IEnumerable<Group> Groups => _groups.Values;

    public Group? Find(int groupId) => _groups.GetValueOrDefault(groupId);

    public int? ParentOf(int groupId) => _parent.TryGetValue(groupId, out var p) ? p : null;

    public IEnumerable<int> ChildrenOf(int groupId) => _children[groupId];

    /// <summary>The group itself, then its parent, up to the top.</summary>
    public IEnumerable<int> Chain(int groupId)
    {
        if (!_groups.ContainsKey(groupId)) yield break;
        for (int? cur = groupId; cur is { } id; cur = ParentOf(id)) yield return id;
    }

    /// <summary>The group itself and all its descendants.</summary>
    public IEnumerable<int> Subtree(int groupId)
    {
        if (!_groups.ContainsKey(groupId)) yield break;
        yield return groupId;
        foreach (var child in _children[groupId])
            foreach (var id in Subtree(child)) yield return id;
    }

    /// <summary>1 for a top-level group.</summary>
    public int Level(int groupId) => Chain(groupId).Count();

    /// <summary>Levels of the group's subtree (1 for a group without sub-groups).</summary>
    public int Height(int groupId) => 1 + _children[groupId].Select(Height).DefaultIfEmpty(0).Max();

    /// <summary>The person's direct memberships in active groups.</summary>
    public IEnumerable<GroupMember> MembershipsOf(int userId) => _byUser[userId];

    /// <summary>The direct admins of one group.</summary>
    public IEnumerable<int> AdminsOf(int groupId) =>
        _groups.TryGetValue(groupId, out var g) ? g.Members.Where(m => m.Role == GroupRole.Admin).Select(m => m.UserId) : [];

    /// <summary>The person's groups including every ancestor of them (effective membership).</summary>
    public HashSet<int> ChainGroupsOf(int userId) => MembershipsOf(userId).SelectMany(m => Chain(m.GroupId)).ToHashSet();

    /// <summary>The effective settings of the person's direct groups, to combine across groups.</summary>
    public IEnumerable<Group> EffectiveGroupsOf(int userId) => MembershipsOf(userId).Select(m => Effective(m.GroupId));

    /// <summary>Whether the user directly administers a group whose effective settings satisfy the capability.</summary>
    private bool Administers(int userId, int groupId, Func<Group, bool> capability) =>
        AdminsOf(groupId).Contains(userId) && capability(Effective(groupId));

    /// <summary>Whether the user administers the group or one of its ancestors with the capability (that group's effective value).</summary>
    public bool AdministersWith(int userId, int groupId, Func<Group, bool> capability) =>
        Chain(groupId).Any(id => Administers(userId, id, capability));

    /// <summary>Whether the user acts as group admin with the capability for the person (via any of the person's groups, up the chain).</summary>
    public bool ActsFor(int userId, int personId, Func<Group, bool> capability) =>
        ChainGroupsOf(personId).Any(id => Administers(userId, id, capability));

    /// <summary>The groups the user directly administers.</summary>
    public IEnumerable<int> AdministeredBy(int userId) =>
        MembershipsOf(userId).Where(m => m.Role == GroupRole.Admin).Select(m => m.GroupId);

    /// <summary>
    /// The group with every setting left on "inherit" taken from its parent's effective value
    /// (recursively; at the top level the group as stored). Not tracked — never save it.
    /// </summary>
    public Group Effective(int groupId)
    {
        if (_effective.TryGetValue(groupId, out var cached)) return cached;
        var own = _groups[groupId];
        var result = ParentOf(groupId) is { } parentId ? Inherit(own, Effective(parentId)) : own;
        _effective[groupId] = result;
        return result;
    }

    /// <summary>The value a group would inherit if it left a setting on "inherit": its parent's effective group, or null at the top.</summary>
    public Group? InheritedFrom(int groupId) => ParentOf(groupId) is { } p ? Effective(p) : null;

    private static Group Inherit(Group g, Group p)
    {
        var ownIn = g.VisibilityAllowInGroups | g.VisibilityDenyInGroups;
        var ownOut = g.VisibilityAllowOutsideGroups | g.VisibilityDenyOutsideGroups;
        var ownCore = g.CoreHoursMode != null;
        return new Group
        {
            Id = g.Id,
            OrganizationId = g.OrganizationId,
            Name = g.Name,
            Description = g.Description,
            Color = g.Color,
            ParentGroupId = g.ParentGroupId,
            IsActive = g.IsActive,
            CreatedAt = g.CreatedAt,
            UpdatedAt = g.UpdatedAt,
            Members = g.Members,
            EditPastEntriesMode = g.EditPastEntriesMode ?? p.EditPastEntriesMode,
            EditPauseMode = g.EditPauseMode ?? p.EditPauseMode,
            CsvImportMode = g.CsvImportMode ?? p.CsvImportMode,
            WorkScheduleChangeMode = g.WorkScheduleChangeMode ?? p.WorkScheduleChangeMode,
            InitialOvertimeMode = g.InitialOvertimeMode ?? p.InitialOvertimeMode,
            AbsenceSelfServiceMode = g.AbsenceSelfServiceMode ?? p.AbsenceSelfServiceMode,
            VisibilityAllowInGroups = g.VisibilityAllowInGroups | (p.VisibilityAllowInGroups & ~ownIn),
            VisibilityDenyInGroups = g.VisibilityDenyInGroups | (p.VisibilityDenyInGroups & ~ownIn),
            VisibilityAllowOutsideGroups = g.VisibilityAllowOutsideGroups | (p.VisibilityAllowOutsideGroups & ~ownOut),
            VisibilityDenyOutsideGroups = g.VisibilityDenyOutsideGroups | (p.VisibilityDenyOutsideGroups & ~ownOut),
            CoreHoursMode = ownCore ? g.CoreHoursMode : p.CoreHoursMode,
            CoreHoursStart = ownCore ? g.CoreHoursStart : p.CoreHoursStart,
            CoreHoursEnd = ownCore ? g.CoreHoursEnd : p.CoreHoursEnd,
            GroupAdminCanApproveAbsences = g.GroupAdminCanApproveAbsences ?? p.GroupAdminCanApproveAbsences,
            GroupAdminCanApproveTimeEdits = g.GroupAdminCanApproveTimeEdits ?? p.GroupAdminCanApproveTimeEdits,
            GroupAdminCanApproveScheduleChanges = g.GroupAdminCanApproveScheduleChanges ?? p.GroupAdminCanApproveScheduleChanges,
            GroupAdminCanManageMembers = g.GroupAdminCanManageMembers ?? p.GroupAdminCanManageMembers,
            GroupAdminCanViewAllTimeEntries = g.GroupAdminCanViewAllTimeEntries ?? p.GroupAdminCanViewAllTimeEntries,
            GroupAdminCanExportData = g.GroupAdminCanExportData ?? p.GroupAdminCanExportData,
        };
    }

    /// <summary>Whether a group's admins may approve this kind of request (pass an <see cref="Effective"/> group).</summary>
    public static bool CanApprove(Group group, RequestType type) => type switch
    {
        RequestType.EditPastEntry or RequestType.EditPause or RequestType.CsvImport or RequestType.SetInitialOvertime
            or RequestType.AddManualEntry
            => group.GroupAdminCanApproveTimeEdits == true,
        RequestType.WorkScheduleChange => group.GroupAdminCanApproveScheduleChanges == true,
        RequestType.RequestAbsence => group.GroupAdminCanApproveAbsences == true,
        _ => false
    };

    /// <summary>Whether a group's admins may approve any kind of request.</summary>
    public static bool CanApproveAny(Group group) =>
        group.GroupAdminCanApproveTimeEdits == true || group.GroupAdminCanApproveScheduleChanges == true || group.GroupAdminCanApproveAbsences == true;

    /// <summary>
    /// Where a member's new request goes: the nearest group (the member's own groups first, then
    /// their parents, …) whose effective capabilities approve the type and that has an admin;
    /// null = the organization admins.
    /// </summary>
    public int? RoutingGroupFor(int userId, RequestType type)
    {
        if (type == RequestType.JoinOrganization) return null;
        var level = MembershipsOf(userId).Select(m => m.GroupId).Distinct().OrderBy(id => id).ToList();
        var seen = level.ToHashSet();
        while (level.Count > 0)
        {
            foreach (var id in level)
                if (CanApprove(Effective(id), type) && AdminsOf(id).Any()) return id;
            level = level.Select(ParentOf).OfType<int>().Where(seen.Add).OrderBy(id => id).ToList();
        }
        return null;
    }

    /// <summary>Everyone who may decide a request routed to the group: the admins along its chain whose group approves the type, without duplicates.</summary>
    public HashSet<int> ApproversOf(int groupId, RequestType type) =>
        Chain(groupId).Where(id => CanApprove(Effective(id), type)).SelectMany(AdminsOf).ToHashSet();

    // ── Structure changes ──

    public const string CycleMessage = "A group cannot be moved into itself or one of its sub-groups.";
    public const string TooDeepMessage = "Groups can be nested at most 3 levels deep.";

    /// <summary>Checks placing <paramref name="groupId"/> (null = a new group) below <paramref name="parentId"/>; null when fine, else the error message.</summary>
    public string? CheckPlacement(int? groupId, int parentId)
    {
        if (groupId is { } id && Chain(parentId).Contains(id)) return CycleMessage;
        var height = groupId is { } g && _groups.ContainsKey(g) ? Height(g) : 1;
        return Level(parentId) + height > MaxDepth ? TooDeepMessage : null;
    }
}
