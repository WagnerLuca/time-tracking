using System.ComponentModel.DataAnnotations;

namespace TimeTracking.Api.Models.Dtos;

/// <summary>Automatic pause rule (deduct X minutes after Y hours worked).</summary>
public record PauseRuleResponse
{
    public int Id { get; init; }
    public int OrganizationId { get; init; }
    public double MinHours { get; init; }
    public int PauseMinutes { get; init; }
}

/// <summary>Request payload for creating a pause rule.</summary>
public record CreatePauseRuleRequest
{
    /// <summary>Minimum hours worked before this rule triggers.</summary>
    [Range(0.01, 24.0)]
    public double MinHours { get; init; }

    /// <summary>Pause minutes to deduct.</summary>
    [Range(1, 480)]
    public int PauseMinutes { get; init; }
}

/// <summary>Request payload for updating a pause rule.</summary>
public record UpdatePauseRuleRequest
{
    [Range(0.01, 24.0)]
    public double MinHours { get; init; }

    [Range(1, 480)]
    public int PauseMinutes { get; init; }
}
