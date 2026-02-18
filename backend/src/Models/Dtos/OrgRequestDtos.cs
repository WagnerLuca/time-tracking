namespace TimeTracking.Api.Models.Dtos;

// Generic Organization Request DTOs

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
}

public record CreateOrgRequestRequest
{
    public string? Message { get; init; }
    public RequestType Type { get; init; } = RequestType.JoinOrganization;
    public int? RelatedEntityId { get; init; }
    public string? RequestData { get; init; }
}

public record RespondToOrgRequestRequest
{
    /// <summary>
    /// true = Accept, false = Decline
    /// </summary>
    public bool Accept { get; init; }
}

/// <summary>
/// Admin notification: pending requests for their orgs
/// </summary>
public record AdminNotificationResponse
{
    public int PendingRequests { get; init; }
    public required List<OrgRequestResponse> Requests { get; init; }
}
