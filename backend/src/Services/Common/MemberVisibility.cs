using Microsoft.EntityFrameworkCore;
using TimeTracking.Api.Data;
using TimeTracking.Api.Models;
using TimeTracking.Api.Models.Dtos;

namespace TimeTracking.Api.Services;

/// <summary>
/// The member visibility matrix (issue #60): which kinds of information (<see cref="MemberInfo"/>)
/// a caller may see about each person of an organization. Every endpoint that shows other people
/// asks this one class; build it once per request with <see cref="ForCallerAsync"/> (two queries)
/// and then call <see cref="Can"/> / <see cref="Ids"/> as often as needed.
/// <list type="bullet">
/// <item>Members always see everything about themselves.</item>
/// <item>Org admins/owners see everything about everyone, except tracked times, which follow the
/// organization's <see cref="Organization.MemberTimeEntryVisibility"/> ("admins may see members' hours").</item>
/// <item>A normal member sees person P per the matrix: column "in own groups" when they share at
/// least one active group with P — counting ancestors of both sides' groups (sub-groups, <see cref="GroupTree"/>),
/// so a department and its teams are one "own group" — else "outside own groups". <b>The data owner's groups decide</b>:
/// P's effective column is P's groups' effective (inherited along the parent chain) overrides combined with <see cref="Combine"/> — the rule of
/// <see cref="EffectiveRuleResolver.Combine{T}"/> per kind: a group override replaces the org default
/// in either direction, the most permissive overriding group wins, no override = org default.</item>
/// <item>Nothing is visible about a person whose <see cref="MemberInfo.Member"/> is hidden.</item>
/// <item>Group admins (org admins too, which matters for tracked times) keep their capability-based
/// rights over the members of the groups they administer and of all their sub-groups (per the administered
/// group's effective capabilities), on top of the matrix: they always see them (name, email, groups);
/// <c>GroupAdminCanApproveAbsences</c> adds all absence kinds, notes, the vacation balance and appointments with their notes;
/// <c>GroupAdminCanViewAllTimeEntries</c> adds tracked times, work schedule and presence;
/// <c>GroupAdminCanExportData</c> adds tracked times and work schedule; and
/// <c>GroupAdminCanManageMembers</c> lets them see every org member (to pick whom to add).</item>
/// </list>
/// Hidden people are left out of lists and counts entirely; a direct request for one is a 404.
/// </summary>
public sealed class MemberVisibility
{
    /// <summary>Every kind, in matrix row order.</summary>
    public static readonly MemberInfo[] Kinds = Enum.GetValues<MemberInfo>().Where(k => k != MemberInfo.None).ToArray();
    public static readonly MemberInfo AllKinds = Kinds.Aggregate(MemberInfo.None, (a, k) => a | k);

    private const MemberInfo Absences = MemberInfo.Vacation | MemberInfo.SickDays | MemberInfo.OtherAbsences
                                        | MemberInfo.AbsenceNotes | MemberInfo.VacationBalance
                                        | MemberInfo.Appointments | MemberInfo.AppointmentNotes;

    private readonly Dictionary<int, MemberInfo> _visible;

    private MemberVisibility(Dictionary<int, MemberInfo> visible, int callerUserId, bool callerIsOrgAdmin,
        GroupTree tree, IReadOnlySet<int>? administeredGroupIds = null)
    {
        _visible = visible;
        CallerUserId = callerUserId;
        CallerIsOrgAdmin = callerIsOrgAdmin;
        Tree = tree;
        AdministeredGroupIds = administeredGroupIds ?? new HashSet<int>();
    }

    /// <summary>The organization's groups as loaded for the matrix (reuse it instead of loading groups again).</summary>
    public GroupTree Tree { get; }

    public int CallerUserId { get; }
    public bool CallerIsOrgAdmin { get; }

    /// <summary>The active groups the caller is a group admin of, including all their sub-groups (whose members they lead).</summary>
    public IReadOnlySet<int> AdministeredGroupIds { get; }

    /// <summary>The person's direct active groups (as loaded for the matrix; not filtered by what the caller may see).</summary>
    public IEnumerable<(int Id, string Name)> GroupsOf(int personId) =>
        Tree.MembershipsOf(personId).Select(m => (m.GroupId, Tree.Find(m.GroupId)!.Name));

    /// <summary>Everything the caller may see about the person (None for hidden people and non-members).</summary>
    public MemberInfo Of(int personId) => _visible.GetValueOrDefault(personId);

    /// <summary>Whether the caller may see all of <paramref name="kind"/> about the person.</summary>
    public bool Can(int personId, MemberInfo kind) => (Of(personId) & kind) == kind;

    /// <summary>
    /// For a direct request about one person: null when the caller may see <paramref name="kind"/>,
    /// NotFound when the person is hidden (or no member), Forbidden when only this kind is hidden.
    /// </summary>
    public ServiceErrorType? Check(int personId, MemberInfo kind) =>
        !Can(personId, MemberInfo.Member) ? ServiceErrorType.NotFound
        : !Can(personId, kind) ? ServiceErrorType.Forbidden
        : null;

    /// <summary>The people about whom the caller may see <paramref name="kind"/>.</summary>
    public HashSet<int> Ids(MemberInfo kind) => _visible.Where(p => (p.Value & kind) == kind).Select(p => p.Key).ToHashSet();

    /// <summary>
    /// One column of a person's effective matrix: <see cref="EffectiveRuleResolver.Combine{T}"/> per
    /// kind — a kind overridden by any of the groups is visible iff one of them allows it (most
    /// permissive wins), every other kind keeps the organization default.
    /// </summary>
    public static MemberInfo Combine(MemberInfo orgDefault, IEnumerable<(MemberInfo Allow, MemberInfo Deny)> groupOverrides)
    {
        MemberInfo overridden = MemberInfo.None, allowed = MemberInfo.None;
        foreach (var (allow, deny) in groupOverrides)
        {
            overridden |= allow | deny;
            allowed |= allow;
        }
        var result = allowed | (orgDefault & ~overridden);
        return result.HasFlag(MemberInfo.Member) ? result : MemberInfo.None;
    }

    /// <summary>What normal members see about a person with these (active, effective — see <see cref="GroupTree.EffectiveGroupsOf"/>) groups: (in their groups, outside).</summary>
    public static (MemberInfo InGroups, MemberInfo OutsideGroups) EffectiveFor(Organization org, IEnumerable<Group> ownerGroups)
    {
        var groups = ownerGroups.ToList();
        return (Combine(org.VisibleInGroups, groups.Select(g => (g.VisibilityAllowInGroups, g.VisibilityDenyInGroups))),
                Combine(org.VisibleOutsideGroups, groups.Select(g => (g.VisibilityAllowOutsideGroups, g.VisibilityDenyOutsideGroups))));
    }

    public static async Task<MemberVisibility> ForCallerAsync(TimeTrackingDbContext context, Organization org, int callerUserId)
    {
        var members = await context.UserOrganizations.AsNoTracking()
            .Where(uo => uo.OrganizationId == org.Id)
            .Select(uo => new { uo.UserId, uo.Role })
            .ToListAsync();
        var visible = new Dictionary<int, MemberInfo>();
        var caller = members.FirstOrDefault(m => m.UserId == callerUserId);
        if (caller == null) return new MemberVisibility(visible, callerUserId, false, new GroupTree([]));

        var isOrgAdmin = caller.Role >= OrganizationRole.Admin;
        var adminView = org.MemberTimeEntryVisibility ? AllKinds : AllKinds & ~MemberInfo.TrackedTime;

        // Sub-groups: shared membership counts ancestors on both sides, overrides and admin
        // capabilities are the groups' effective (inherited) values, see GroupTree
        var tree = await GroupTree.LoadAsync(context, org.Id);
        var callerGroupIds = tree.ChainGroupsOf(callerUserId);
        var administered = tree.AdministeredBy(callerUserId).ToDictionary(id => id, tree.Effective);
        var managesMembers = administered.Values.Any(g => g.GroupAdminCanManageMembers == true);

        foreach (var m in members)
        {
            if (m.UserId == callerUserId)
            {
                visible[m.UserId] = AllKinds;
                continue;
            }
            var groups = tree.EffectiveGroupsOf(m.UserId).ToList();
            var chain = tree.ChainGroupsOf(m.UserId);
            var flags = isOrgAdmin ? adminView
                : chain.Overlaps(callerGroupIds)
                ? Combine(org.VisibleInGroups, groups.Select(g => (g.VisibilityAllowInGroups, g.VisibilityDenyInGroups)))
                : Combine(org.VisibleOutsideGroups, groups.Select(g => (g.VisibilityAllowOutsideGroups, g.VisibilityDenyOutsideGroups)));

            foreach (var id in chain)
            {
                if (!administered.TryGetValue(id, out var admin)) continue;
                flags |= MemberInfo.Member | MemberInfo.Email | MemberInfo.Groups;
                if (admin.GroupAdminCanApproveAbsences == true) flags |= Absences;
                if (admin.GroupAdminCanViewAllTimeEntries == true) flags |= MemberInfo.TrackedTime | MemberInfo.WorkSchedule | MemberInfo.Presence;
                if (admin.GroupAdminCanExportData == true) flags |= MemberInfo.TrackedTime | MemberInfo.WorkSchedule;
            }
            if (managesMembers) flags |= MemberInfo.Member;
            if (flags != MemberInfo.None) visible[m.UserId] = flags;
        }
        return new MemberVisibility(visible, callerUserId, isOrgAdmin, tree,
            administered.Keys.SelectMany(tree.Subtree).ToHashSet());
    }

    // ── Matrix <-> DTOs ──

    public static List<VisibilityRuleResponse> Rows(MemberInfo inGroups, MemberInfo outsideGroups) =>
        Kinds.Select(k => new VisibilityRuleResponse(k.ToString(), inGroups.HasFlag(k), outsideGroups.HasFlag(k))).ToList();

    public static List<GroupVisibilityRuleResponse> Rows(Group g) =>
        Kinds.Select(k => new GroupVisibilityRuleResponse(k.ToString(),
            Cell(g.VisibilityAllowInGroups, g.VisibilityDenyInGroups, k),
            Cell(g.VisibilityAllowOutsideGroups, g.VisibilityDenyOutsideGroups, k))).ToList();

    private static bool? Cell(MemberInfo allow, MemberInfo deny, MemberInfo kind) =>
        allow.HasFlag(kind) ? true : deny.HasFlag(kind) ? false : null;

    public const string UnknownKindMessage = "Unknown kind of information.";

    /// <summary>One kind by name (exactly one defined flag), or null.</summary>
    public static MemberInfo? ParseKind(string? name) =>
        Enum.TryParse<MemberInfo>(name, ignoreCase: true, out var kind) && Kinds.Contains(kind) && !int.TryParse(name, out _)
            ? kind : null;

    /// <summary>Applies the sent rows to the organization's two columns; null on success, else the error message.</summary>
    public static string? Apply(Organization org, IEnumerable<VisibilityRuleRequest> rules)
    {
        foreach (var rule in rules)
        {
            if (ParseKind(rule.Kind) is not { } kind) return UnknownKindMessage;
            org.VisibleInGroups = Set(org.VisibleInGroups, kind, rule.InGroups);
            org.VisibleOutsideGroups = Set(org.VisibleOutsideGroups, kind, rule.OutsideGroups);
        }
        return null;
    }

    /// <summary>Applies the sent rows to a group's overrides (null = inherit); null on success, else the error message.</summary>
    public static string? Apply(Group group, IEnumerable<GroupVisibilityRuleRequest> rules)
    {
        foreach (var rule in rules)
        {
            if (ParseKind(rule.Kind) is not { } kind) return UnknownKindMessage;
            group.VisibilityAllowInGroups = Set(group.VisibilityAllowInGroups, kind, rule.InGroups == true);
            group.VisibilityDenyInGroups = Set(group.VisibilityDenyInGroups, kind, rule.InGroups == false);
            group.VisibilityAllowOutsideGroups = Set(group.VisibilityAllowOutsideGroups, kind, rule.OutsideGroups == true);
            group.VisibilityDenyOutsideGroups = Set(group.VisibilityDenyOutsideGroups, kind, rule.OutsideGroups == false);
        }
        return null;
    }

    private static MemberInfo Set(MemberInfo set, MemberInfo kind, bool on) => on ? set | kind : set & ~kind;
}
