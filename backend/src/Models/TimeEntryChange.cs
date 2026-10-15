namespace TimeTracking.Api.Models;

public enum TimeEntryChangeKind
{
    Created = 0,
    Updated = 1,
    Deleted = 2,
    /// <summary>Created or changed by approving a request (manual entry, edit, pause, CSV import).</summary>
    ApprovedRequest = 3
}

/// <summary>
/// One recorded change of a finished time entry: who changed which fields from what to what.
/// Written by <see cref="Data.TimeTrackingDbContext"/> on save for every user-initiated change
/// (member edit, admin correction, request approval, import, manual entry); starting and
/// stopping a timer and background jobs are not recorded. Survives the entry's deletion, so
/// <see cref="TimeEntryId"/> has no foreign key.
/// </summary>
public class TimeEntryChange
{
    public int Id { get; set; }
    public int TimeEntryId { get; set; }

    /// <summary>Owner of the entry (row-level security: the owner sees their history).</summary>
    public int UserId { get; set; }

    /// <summary>Organization of the entry after the change (row-level security: its members).</summary>
    public int? OrganizationId { get; set; }

    public int? ChangedByUserId { get; set; }
    public DateTime ChangedAt { get; set; }
    public TimeEntryChangeKind Kind { get; set; }

    /// <summary>JSON array of <c>{ field, oldValue, newValue }</c> (values as invariant strings, times as ISO 8601 UTC).</summary>
    public required string Changes { get; set; }

    /// <summary>The change was made by an org admin inside a closed month (issue #57).</summary>
    public bool InClosedPeriod { get; set; }

    /// <summary>The reason the admin gave for changing a closed month.</summary>
    public string? Reason { get; set; }

    public User User { get; set; } = null!;
    public User? ChangedByUser { get; set; }
}
