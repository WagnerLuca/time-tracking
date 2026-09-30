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

    /// <summary>If true, this holiday counts as half a day (e.g. half-day off).</summary>
    public bool IsHalfDay { get; set; }

    /// <summary>
    /// The date this holiday falls on in <paramref name="year"/>, or null when it does not occur
    /// that year (a one-off holiday of another year, or a recurring 29 February in a non-leap year).
    /// </summary>
    public DateOnly? DateIn(int year)
    {
        if (!IsRecurring) return Date.Year == year ? Date : null;
        if (Date.Month == 2 && Date.Day == 29 && !DateTime.IsLeapYear(year)) return null;
        return new DateOnly(year, Date.Month, Date.Day);
    }

    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingDefault)]
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;

    // Navigation
    [JsonIgnore]
    public Organization Organization { get; set; } = null!;
}
