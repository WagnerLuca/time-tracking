using System.Text.Json.Serialization;

namespace TimeTracking.Api.Models;

/// <summary>
/// Tri-state rule mode for organization settings.
/// Disabled = feature off entirely.
/// RequiresApproval = user must request, admin approves.
/// Allowed = user can do it themselves.
/// </summary>
public enum RuleMode
{
    Disabled = 0,
    RequiresApproval = 1,
    Allowed = 2
}

/// <summary>
/// The kinds of information about a person that the member visibility matrix governs
/// (see <c>MemberVisibility</c>). Bit flags: the organization stores one set per column
/// (in / outside the viewer's groups), a group one allow and one deny set per column.
/// The numeric values are persisted — never renumber them.
/// </summary>
[Flags]
public enum MemberInfo
{
    None = 0,
    /// <summary>The person appears at all (name, avatar, join date). Every other kind requires it.</summary>
    Member = 1,
    Email = 2,
    /// <summary>Group memberships and group roles (group admin); the org role is always shown with the person.</summary>
    Groups = 4,
    /// <summary>Whether a timer is running right now.</summary>
    Presence = 8,
    Vacation = 16,
    SickDays = 32,
    /// <summary>"Other" and custom absence types.</summary>
    OtherAbsences = 64,
    /// <summary>The note on an absence that is visible anyway.</summary>
    AbsenceNotes = 128,
    /// <summary>Vacation allowance, taken, planned and remaining days.</summary>
    VacationBalance = 256,
    /// <summary>Work schedule (target hours).</summary>
    WorkSchedule = 512,
    /// <summary>Tracked times and everything derived from them (balance, reports, compliance).</summary>
    TrackedTime = 1024
}

public class Organization
{
    public int Id { get; set; }
    public required string Name { get; set; }
    public string? Description { get; set; }
    public required string Slug { get; set; }  // URL-friendly identifier
    
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingDefault)]
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
    
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingDefault)]
    public DateTime UpdatedAt { get; set; }
    
    public bool IsActive { get; set; } = true;
    
    // Settings
    public string? LogoUrl { get; set; }
    public string? Website { get; set; }
    
    /// <summary>
    /// When true, automatic pause deductions are applied based on PauseRules
    /// </summary>
    public bool AutoPauseEnabled { get; set; } = true;
    
    /// <summary>
    /// Controls whether members can edit their past time entries.
    /// Allowed = user can edit freely.
    /// RequiresApproval = user must request and admin approves.
    /// Disabled = editing past entries is not available.
    /// </summary>
    public RuleMode EditPastEntriesMode { get; set; } = RuleMode.Allowed;
    
    /// <summary>
    /// Controls whether members can override the auto-deducted pause duration.
    /// Allowed = user can edit freely.
    /// RequiresApproval = user must request and admin approves.
    /// Disabled = pause editing is not available.
    /// </summary>
    public RuleMode EditPauseMode { get; set; } = RuleMode.Allowed;
    
    /// <summary>
    /// Controls whether members can set their own initial overtime balance.
    /// Allowed = user can set freely.
    /// RequiresApproval = user must request and admin approves.
    /// Disabled = initial overtime is not available.
    /// </summary>
    public RuleMode InitialOvertimeMode { get; set; } = RuleMode.Allowed;
    
    /// <summary>
    /// Controls how users can join this organization.
    /// Allowed = anyone can join freely.
    /// RequiresApproval = user submits a join request that admin must approve.
    /// Disabled = only admins can manually add members.
    /// </summary>
    public RuleMode JoinPolicy { get; set; } = RuleMode.RequiresApproval;
    
    /// <summary>
    /// Controls whether members can change their own work schedule periods.
    /// Allowed = user can add/edit schedule periods freely.
    /// RequiresApproval = user must request and admin approves.
    /// Disabled = only admins can manage schedule periods.
    /// </summary>
    public RuleMode WorkScheduleChangeMode { get; set; } = RuleMode.Allowed;
    
    /// <summary>
    /// When true, admins and owners can view members' time entries and time overview.
    /// When false, nobody can see other members' tracked working hours.
    /// </summary>
    public bool MemberTimeEntryVisibility { get; set; } = false;
    
    /// <summary>
    /// When true, all members of this organization must have two-factor authentication enabled.
    /// Members without 2FA will be prompted to set it up before they can use the application.
    /// </summary>
    public bool Require2fa { get; set; } = false;
    
    /// <summary>
    /// Controls whether members can import time entries from CSV files.
    /// Allowed = user can import freely.
    /// RequiresApproval = user must request and admin approves.
    /// Disabled = CSV import is not available.
    /// </summary>
    public RuleMode CsvImportMode { get; set; } = RuleMode.Disabled;
    
    /// <summary>
    /// Controls whether members can create their own absence days (vacation, sick, ...).
    /// Allowed = user can add absences directly.
    /// RequiresApproval = user must submit an absence request that an admin (org or group) approves.
    /// Disabled = only admins can add absences.
    /// </summary>
    public RuleMode AbsenceSelfServiceMode { get; set; } = RuleMode.Allowed;

    /// <summary>
    /// Member visibility matrix, column "in own groups": what a normal member sees about people
    /// sharing at least one active group with them. Groups override it per cell (see MemberVisibility).
    /// </summary>
    public MemberInfo VisibleInGroups { get; set; } = DefaultVisibility;

    /// <summary>Member visibility matrix, column "outside own groups".</summary>
    public MemberInfo VisibleOutsideGroups { get; set; } = DefaultVisibility;

    /// <summary>New organizations: colleagues appear with email and groups, absences and times stay private.</summary>
    public const MemberInfo DefaultVisibility = MemberInfo.Member | MemberInfo.Email | MemberInfo.Groups | MemberInfo.AbsenceNotes;
    
    /// <summary>
    /// Default vacation days per year for new members.
    /// </summary>
    public double DefaultVacationDays { get; set; }

    /// <summary>
    /// Local time at which a business day ends. A timer still running then is stopped
    /// and marked as a failure entry. 00:00 = midnight; e.g. 04:00 for late-night gastro shifts.
    /// </summary>
    public TimeOnly BusinessDayEnd { get; set; } = TimeOnly.MinValue;

    /// <summary>IANA time zone the business day end refers to.</summary>
    public string TimeZone { get; set; } = "Europe/Berlin";

    /// <summary>
    /// Core working hours (Kernarbeitszeit): on their working days members must have tracked
    /// time covering [<see cref="CoreHoursStart"/>, <see cref="CoreHoursEnd"/>) local time.
    /// Groups can override it (<see cref="Group.CoreHoursMode"/>). See <c>CoreHours</c>.
    /// </summary>
    public bool CoreHoursEnabled { get; set; }
    public TimeOnly CoreHoursStart { get; set; } = new(9, 0);
    public TimeOnly CoreHoursEnd { get; set; } = new(15, 0);
    /// <summary>Allowed late start, early leave and gap length before a day counts as a violation.</summary>
    public int CoreHoursToleranceMinutes { get; set; } = 5;
    /// <summary>
    /// When a core hours setting (org or group override) last changed. A day checked late (the
    /// catch-up after downtime) is never one whose window started before this: those days were
    /// not under the new rule, and only an explicit backfill marks them.
    /// </summary>
    public DateTime? CoreHoursChangedAt { get; set; }
    /// <summary>A requested backfill ("also check the last N days"): the background check flags
    /// the days from here up to the change silently, then clears it.</summary>
    public DateOnly? CoreHoursBackfillFrom { get; set; }

    /// <summary>
    /// Overtime balance checkpoints (<c>BalanceCheckpoints</c>): bumped by every save that changes an org-wide input of
    /// the balance in an ended month (holidays, custom absence credit modes, the time zone); a checkpoint is only valid
    /// while its <see cref="BalanceCheckpoint.OrgVersion"/> matches.
    /// </summary>
    public int BalanceVersion { get; set; }
    /// <summary>First day of the org-local month whose checkpoints the background pre-warm last computed.</summary>
    public DateOnly? BalanceWarmedMonth { get; set; }

    /// <summary>Terminal (docs/TERMINAL.md): a second scan of the same card within this many seconds toggles nothing.</summary>
    public int TerminalDoubleScanSeconds { get; set; } = TerminalLimits.DefaultDoubleScanSeconds;
    /// <summary>Terminal: oldest queued (offline) scan a terminal may still send, in hours.</summary>
    public int TerminalOfflineHours { get; set; } = TerminalLimits.DefaultOfflineHours;
    /// <summary>Terminal: unknown/blocked scans one key may send per 10 minutes before it gets 429s.</summary>
    public int TerminalUnknownCardLimit { get; set; } = TerminalLimits.DefaultUnknownCardLimit;
    
    /// <summary>
    /// Timestamp of the last settings update. Used to notify members of rule changes.
    /// </summary>
    public DateTime? SettingsUpdatedAt { get; set; }
    
    // Navigation properties
    [JsonIgnore]
    public ICollection<UserOrganization> UserOrganizations { get; set; } = new List<UserOrganization>();
    
    [JsonIgnore]
    public ICollection<PauseRule> PauseRules { get; set; } = new List<PauseRule>();
    
    [JsonIgnore]
    public ICollection<Holiday> Holidays { get; set; } = new List<Holiday>();
}

/// <summary>Defaults and allowed ranges of the per-organization terminal settings.</summary>
public static class TerminalLimits
{
    public const int DefaultDoubleScanSeconds = 60, MinDoubleScanSeconds = 5, MaxDoubleScanSeconds = 600;
    public const int DefaultOfflineHours = 72, MinOfflineHours = 1, MaxOfflineHours = 168;
    public const int DefaultUnknownCardLimit = 20, MinUnknownCardLimit = 5, MaxUnknownCardLimit = 200;
}
