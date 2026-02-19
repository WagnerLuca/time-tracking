using System.Text.Json.Serialization;

namespace TimeTracking.Api.Models;

public class Holiday
{
    public int Id { get; set; }
    public int OrganizationId { get; set; }
    
    /// <summary>Date of the holiday (date only, no time).</summary>
    public DateOnly Date { get; set; }
    
    /// <summary>Name of the holiday, e.g. "Christmas", "New Year".</summary>
    public required string Name { get; set; }

    /// <summary>If true, this holiday repeats every year on the same month/day.</summary>
    public bool IsRecurring { get; set; }

    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingDefault)]
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;

    // Navigation
    [JsonIgnore]
    public Organization Organization { get; set; } = null!;
}
