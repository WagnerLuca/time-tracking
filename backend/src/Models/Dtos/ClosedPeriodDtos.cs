using System.ComponentModel.DataAnnotations;

namespace TimeTracking.Api.Models.Dtos;

/// <summary>Closed months of an organization and (for org admins) the audit list.</summary>
public record ClosedPeriodsResponse
{
    /// <summary>First day of each closed month, ascending.</summary>
    public required List<DateOnly> Months { get; init; }
    /// <summary>Newest first; empty for non-admins.</summary>
    public required List<ClosedPeriodEventResponse> Events { get; init; }
}

public record ClosedPeriodEventResponse
{
    public int Id { get; init; }
    public DateOnly Month { get; init; }
    /// <summary>Closed, Reopened or AdminChange.</summary>
    public required string Kind { get; init; }
    public DateTime At { get; init; }
    public string? UserName { get; init; }
    public string? Reason { get; init; }
    /// <summary>AdminChange: whose data changed (null when several).</summary>
    public string? MemberName { get; init; }
    /// <summary>AdminChange: e.g. "TimeEntry:2,AbsenceDay:1".</summary>
    public string? Summary { get; init; }
}

/// <summary>Close the months from..to (any day inside each; both inclusive). Only months that have ended.</summary>
public record CloseMonthsRequest
{
    public DateOnly From { get; init; }
    public DateOnly To { get; init; }
    [MaxLength(500)]
    public string? Reason { get; init; }
}

public record ReopenMonthRequest
{
    /// <summary>Any day of the month to reopen.</summary>
    public DateOnly Month { get; init; }
    [Required, MaxLength(500)]
    public required string Reason { get; init; }
}
