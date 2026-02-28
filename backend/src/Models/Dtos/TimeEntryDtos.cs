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
    public DateTime CreatedAt { get; init; }
}
