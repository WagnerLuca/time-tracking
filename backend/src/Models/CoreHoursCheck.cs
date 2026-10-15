using System.Text.Json.Serialization;

namespace TimeTracking.Api.Models;

[Flags]
public enum CoreHoursIssue
{
    None = 0,
    /// <summary>First tracked time starts later than core start + tolerance.</summary>
    Late = 1,
    /// <summary>Last tracked time ends earlier than core end − tolerance.</summary>
    Early = 2,
    /// <summary>A gap longer than the tolerance inside the core window.</summary>
    Gap = 4,
    /// <summary>No tracked time inside the core window at all.</summary>
    Missing = 8
}

/// <summary>
/// The daily core hours check of one member (written once per member and local day by the
/// background job, also for exempt or fine days, so a day is never checked or notified twice).
/// <see cref="Issues"/> = None means nothing to report. The window, half-day flag and tolerance
/// are the ones that applied that day, so later views re-evaluate the day against the rule of
/// that day, not today's settings.
/// </summary>
public class CoreHoursCheck
{
    public int Id { get; set; }
    public int OrganizationId { get; set; }
    public int UserId { get; set; }
    public DateOnly Date { get; set; }
    public TimeOnly WindowStart { get; set; }
    public TimeOnly WindowEnd { get; set; }
    /// <summary>A half-day absence: only one half of the window had to be covered.</summary>
    public bool HalfDay { get; set; }
    public int ToleranceMinutes { get; set; }
    public CoreHoursIssue Issues { get; set; }
    /// <summary>Uncovered minutes of the (half) window that was checked.</summary>
    public int MissingMinutes { get; set; }
    public DateTime CheckedAt { get; set; }
    public DateTime? NotifiedAt { get; set; }
    /// <summary>Checked by the catch-up more than <c>CoreHours.LateAfter</c> after it was due (notified as "checked late").</summary>
    public bool CheckedLate { get; set; }
    /// <summary>Flagged by a backfill when core hours were switched on: never notified.</summary>
    public bool Backfilled { get; set; }

    [JsonIgnore]
    public User User { get; set; } = null!;
    [JsonIgnore]
    public Organization Organization { get; set; } = null!;
}
