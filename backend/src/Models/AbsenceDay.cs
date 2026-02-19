using System.Text.Json.Serialization;

namespace TimeTracking.Api.Models;

public enum AbsenceType
{
    SickDay = 0,
    Vacation = 1,
    Other = 2
}

public class AbsenceDay
{
    public int Id { get; set; }
    public int UserId { get; set; }
    public int OrganizationId { get; set; }

    /// <summary>Date of the absence (date only).</summary>
    public DateOnly Date { get; set; }

    public AbsenceType Type { get; set; } = AbsenceType.SickDay;

    /// <summary>Optional note, e.g. reason.</summary>
    public string? Note { get; set; }

    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingDefault)]
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;

    // Navigation
    [JsonIgnore]
    public User User { get; set; } = null!;
    [JsonIgnore]
    public Organization Organization { get; set; } = null!;
}
