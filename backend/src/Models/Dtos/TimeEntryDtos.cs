using System.ComponentModel.DataAnnotations;

namespace TimeTracking.Api.Models.Dtos;

/// <summary>Request payload to start (clock in) a new time entry.</summary>
public record StartTimeEntryRequest
{
    /// <summary>Optional description of the work being done.</summary>
    [MaxLength(500)]
    public string? Description { get; init; }

    /// <summary>Organization ID to track time against (optional).</summary>
    public int? OrganizationId { get; init; }

    /// <summary>Organization slug (alternative to OrganizationId).</summary>
    public string? OrganizationSlug { get; init; }
}

/// <summary>Request payload to stop (clock out) the current time entry.</summary>
public record StopTimeEntryRequest
{
    /// <summary>Optional description to set/update on the entry being stopped.</summary>
    [MaxLength(500)]
    public string? Description { get; init; }
}

/// <summary>Request payload to update an existing time entry.</summary>
public record UpdateTimeEntryRequest
{
    /// <summary>New start time (UTC).</summary>
    public DateTime? StartTime { get; init; }
    /// <summary>New end time (UTC).</summary>
    public DateTime? EndTime { get; init; }

    /// <summary>Updated description.</summary>
    [MaxLength(500)]
    public string? Description { get; init; }

    /// <summary>New organization ID.</summary>
    public int? OrganizationId { get; init; }

    /// <summary>Manual pause duration in minutes (0-1440).</summary>
    [Range(0, 1440)]
    public int? PauseDurationMinutes { get; init; }
}

/// <summary>Response representing a single time entry.</summary>
public record TimeEntryResponse
{
    public int Id { get; init; }
    public int UserId { get; init; }
    public int? OrganizationId { get; init; }
    public string? OrganizationName { get; init; }
    public string? Description { get; init; }
    /// <summary>When the entry started (UTC).</summary>
    public DateTime StartTime { get; init; }
    /// <summary>When the entry ended (UTC), null if still running.</summary>
    public DateTime? EndTime { get; init; }
    /// <summary>True if the timer is currently running.</summary>
    public bool IsRunning { get; init; }
    /// <summary>Gross duration in minutes (including pauses).</summary>
    public double? DurationMinutes { get; init; }
    /// <summary>Pause duration in minutes.</summary>
    public int PauseDurationMinutes { get; init; }
    /// <summary>Net duration in minutes (gross minus pauses).</summary>
    public double? NetDurationMinutes { get; init; }
    /// <summary>
    /// Stopped automatically at the business day end; durations are null (unknown)
    /// until the member corrects the end time.
    /// </summary>
    public bool IsFailure { get; init; }
    public DateTime CreatedAt { get; init; }
    /// <summary>Where the entry was created: App, Terminal, Manual, Import or Admin.</summary>
    public string Source { get; init; } = "App";
    /// <summary>Name of the terminal that started the entry, if one did.</summary>
    public string? TerminalStartName { get; init; }
    /// <summary>Name of the terminal that stopped (or closed) the entry, if one did.</summary>
    public string? TerminalStopName { get; init; }

    /// <summary>
    /// Working Time Act warnings, filled on stop, edit and manual/admin create:
    /// "dailyMax" (more than 10 h net that day), "restPeriod" (less than 11 h rest before
    /// this day or before the next). Null where not computed (lists).
    /// </summary>
    public List<string>? Warnings { get; init; }
}

/// <summary>A finished time entry added by hand (e.g. a forgotten day).</summary>
public record CreateManualEntryRequest
{
    /// <summary>Start (UTC).</summary>
    public DateTime StartTime { get; init; }
    /// <summary>End (UTC), not in the future.</summary>
    public DateTime EndTime { get; init; }
    /// <summary>Pause in minutes; without it the organization's pause rules apply.</summary>
    [Range(0, 1440)]
    public int? PauseDurationMinutes { get; init; }
    [MaxLength(500)]
    public string? Description { get; init; }
    /// <summary>Organization to book on; null = personal entry.</summary>
    public int? OrganizationId { get; init; }
}

/// <summary>
/// Result of a manual entry: <see cref="Entry"/> when it was created directly, <see cref="Request"/>
/// when the organization requires approval (the entry is created once it is accepted).
/// </summary>
public record ManualEntryResponse
{
    public TimeEntryResponse? Entry { get; init; }
    public OrgRequestResponse? Request { get; init; }
}

/// <summary>JSON payload (RequestData) of an AddManualEntry request.</summary>
public record ManualEntryRequestData
{
    public DateTime StartTime { get; init; }
    public DateTime EndTime { get; init; }
    public int? PauseDurationMinutes { get; init; }
    public string? Description { get; init; }
}

/// <summary>An admin creates a finished entry for a member.</summary>
public record CreateMemberEntryRequest
{
    public DateTime StartTime { get; init; }
    public DateTime EndTime { get; init; }
    [Range(0, 1440)]
    public int? PauseDurationMinutes { get; init; }
    [MaxLength(500)]
    public string? Description { get; init; }
}

/// <summary>An admin corrects a member's entry; unset fields stay. Setting the end time resolves a failure entry.</summary>
public record UpdateMemberEntryRequest
{
    public DateTime? StartTime { get; init; }
    public DateTime? EndTime { get; init; }
    [Range(0, 1440)]
    public int? PauseDurationMinutes { get; init; }
    [MaxLength(500)]
    public string? Description { get; init; }
}

/// <summary>One recorded change of a time entry.</summary>
public record TimeEntryChangeResponse
{
    public int Id { get; init; }
    public int TimeEntryId { get; init; }
    public int? OrganizationId { get; init; }
    public int? ChangedByUserId { get; init; }
    public string? ChangedByName { get; init; }
    public DateTime ChangedAt { get; init; }
    /// <summary>Created, Updated, Deleted or ApprovedRequest.</summary>
    public required string Kind { get; init; }
    public required List<TimeEntryFieldChangeResponse> Changes { get; init; }
    /// <summary>An org admin changed the entry inside a closed month (issue #57), with <see cref="Reason"/>.</summary>
    public bool InClosedPeriod { get; init; }
    public string? Reason { get; init; }
}

/// <summary>A field before/after a change. Times are ISO 8601 UTC, numbers invariant, null = not set.</summary>
public record TimeEntryFieldChangeResponse
{
    /// <summary>StartTime, EndTime, PauseDurationMinutes, Description, OrganizationId or IsFailure.</summary>
    public required string Field { get; init; }
    public string? OldValue { get; init; }
    public string? NewValue { get; init; }
}

/// <summary>A day of a closed month as frozen when it was closed (issue #81): what the calendar shows for it.</summary>
public record FrozenDayResponse
{
    public DateOnly Date { get; init; }
    /// <summary>The schedule's target less the holiday credit.</summary>
    public double TargetHours { get; init; }
    /// <summary>Hours an absence took off the target.</summary>
    public double CreditHours { get; init; }
    /// <summary>Net worked hours.</summary>
    public double WorkedHours { get; init; }
    /// <summary>Report status (Worked, Missing, Holiday, Half Holiday, Vacation, Sick, a custom type's name, Weekend, Day Off, Needs Correction).</summary>
    public required string Status { get; init; }
    public string? HolidayName { get; init; }
    /// <summary>SickDay, Vacation or Other.</summary>
    public string? AbsenceType { get; init; }
    public bool AbsenceHalfDay { get; init; }
}

/// <summary>A day that breaks the Working Time Act (see WorkingTimeAct) or misses the core working hours (see CoreHours).</summary>
public record ComplianceDayResponse
{
    public DateOnly Date { get; init; }
    public double NetHours { get; init; }
    /// <summary>Hours between the previous day's last entry and this day's first; null without entries the day before.</summary>
    public double? RestHoursBefore { get; init; }
    /// <summary>More than 10 h net work.</summary>
    public bool DailyMax { get; init; }
    /// <summary>Less than 11 h rest before this day.</summary>
    public bool RestPeriod { get; init; }
    /// <summary>The tracked time does not cover the core working hours (and still doesn't).</summary>
    public bool CoreHours { get; init; }
    /// <summary>With <see cref="CoreHours"/>: "late", "early", "gap" and/or "missing" (nothing tracked).</summary>
    public List<string>? CoreHoursIssues { get; init; }
    /// <summary>With <see cref="CoreHours"/>: uncovered minutes of the checked window.</summary>
    public int CoreHoursMissingMinutes { get; init; }
    /// <summary>With <see cref="CoreHours"/>: the window that was checked, "HH:mm-HH:mm" (one half on a half day).</summary>
    public string? CoreHoursWindow { get; init; }
    /// <summary>With <see cref="CoreHours"/>: flagged by a backfill when core hours were switched on (never notified).</summary>
    public bool CoreHoursBackfilled { get; init; }
    /// <summary>With <see cref="CoreHours"/>: planned appointments that were counted as covered ("HH:mm-HH:mm" or "allDay").</summary>
    public List<string>? CoreHoursAppointments { get; init; }
}
