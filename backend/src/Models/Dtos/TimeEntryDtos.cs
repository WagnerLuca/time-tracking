namespace TimeTracking.Api.Models.Dtos;

public record StartTimeEntryRequest
{
    public string? Description { get; init; }
    public int? OrganizationId { get; init; }
    public string? OrganizationSlug { get; init; }
}

public record StopTimeEntryRequest
{
    public string? Description { get; init; }
}

public record UpdateTimeEntryRequest
{
    public DateTime? StartTime { get; init; }
    public DateTime? EndTime { get; init; }
    public string? Description { get; init; }
    public int? OrganizationId { get; init; }
    public int? PauseDurationMinutes { get; init; }
}

public record TimeEntryResponse
{
    public int Id { get; init; }
    public int UserId { get; init; }
    public int? OrganizationId { get; init; }
    public string? OrganizationName { get; init; }
    public string? Description { get; init; }
    public DateTime StartTime { get; init; }
    public DateTime? EndTime { get; init; }
    public bool IsRunning { get; init; }
    public double? DurationMinutes { get; init; }
    public int PauseDurationMinutes { get; init; }
    public double? NetDurationMinutes { get; init; }
    public DateTime CreatedAt { get; init; }
}
