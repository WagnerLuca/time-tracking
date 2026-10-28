using System.ComponentModel.DataAnnotations;

namespace TimeTracking.Api.Models.Dtos;

// Generic Organization Request DTOs

/// <summary>Response representing an organization request (join, edit entry, etc.).</summary>
public record OrgRequestResponse
{
    public int Id { get; init; }
    public int UserId { get; init; }
    public required string UserFirstName { get; init; }
    public required string UserLastName { get; init; }
    public required string UserEmail { get; init; }
    public int OrganizationId { get; init; }
    public required string OrganizationName { get; init; }
    public required string OrganizationSlug { get; init; }
    public required string Type { get; init; }
    public required string Status { get; init; }
    public string? Message { get; init; }
    public int? RelatedEntityId { get; init; }
    public string? RequestData { get; init; }
    public DateTime CreatedAt { get; init; }
    public DateTime? RespondedAt { get; init; }
    public string? RespondedByName { get; init; }
    /// <summary>Reason the approver gave with the decision, if any.</summary>
    public string? ResponseReason { get; init; }
    public int? GroupId { get; init; }
    public string? GroupName { get; init; }
    /// <summary>Pending only: the first closed month approving it would change (only org admins may approve it, with a reason).</summary>
    public DateOnly? ClosedMonth { get; init; }
}

/// <summary>Request payload for creating a new organization request.</summary>
public record CreateOrgRequestRequest
{
    [MaxLength(1000)]
    public string? Message { get; init; }

    public RequestType Type { get; init; } = RequestType.JoinOrganization;
    public int? RelatedEntityId { get; init; }

    [MaxLength(200_000)]   // a CSV import of 1000 rows fits comfortably
    public string? RequestData { get; init; }
}

public record RespondToOrgRequestRequest
{
    /// <summary>
    /// true = Accept, false = Decline
    /// </summary>
    public bool Accept { get; init; }

    /// <summary>Optional reason shown to the member (e.g. why the request was declined).</summary>
    [MaxLength(1000)]
    public string? Reason { get; init; }
}

/// <summary>
/// Admin notification: pending requests for their orgs
/// </summary>
public record AdminNotificationResponse
{
    public int PendingRequests { get; init; }
    public required List<OrgRequestResponse> Requests { get; init; }
}

/// <summary>
/// User notification: requests that have been responded to
/// </summary>
public record UserNotificationResponse
{
    public int Count { get; init; }
    public required List<OrgRequestResponse> Requests { get; init; }
}

/// <summary>
/// JSON payload for EditPastEntry request data
/// </summary>
public record EditEntryRequestData
{
    public DateTime? StartTime { get; init; }
    public DateTime? EndTime { get; init; }
    public string? Description { get; init; }
}

/// <summary>
/// JSON payload for CsvImport request data.
/// Contains the actual entries to import when the request is approved.
/// </summary>
public record CsvImportRequestData
{
    public List<CsvImportEntry>? Entries { get; init; }
    public string? FileName { get; init; }
}

public record CsvImportEntry
{
    public string? Date { get; init; }
    public string? StartTime { get; init; }
    public string? EndTime { get; init; }
    public int PauseMinutes { get; init; }
    public string? Description { get; init; }
}

/// <summary>
/// JSON payload for RequestAbsence request data.
/// Contains the absence days to create when the request is approved.
/// </summary>
public record AbsenceRequestData
{
    /// <summary>Dates to mark as absent (yyyy-MM-dd).</summary>
    public List<DateOnly> Dates { get; init; } = new();
    public AbsenceType Type { get; init; } = AbsenceType.Vacation;
    public int? CustomTypeId { get; init; }
    public bool IsHalfDay { get; init; }
    public string? Note { get; init; }
}

/// <summary>
/// JSON payload for WorkScheduleChange request data (issue #82): "a new schedule from <see cref="ValidFrom"/>" with its
/// targets; approving applies it like a direct change (an existing future schedule is replaced). Older requests' ValidTo is ignored.
/// </summary>
public record WorkScheduleChangeRequestData : ScheduleTargets
{
    /// <summary>First day the new schedule is effective.</summary>
    public DateOnly ValidFrom { get; init; }
}
