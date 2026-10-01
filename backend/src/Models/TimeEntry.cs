using System.ComponentModel.DataAnnotations;

namespace TimeTracking.Api.Models;

public class TimeEntry
{
    public int Id { get; set; }
    
    [Required]
    public int UserId { get; set; }
    
    public int? OrganizationId { get; set; }
    
    [MaxLength(500)]
    public string? Description { get; set; }
    
    public DateTime StartTime { get; set; }
    
    public DateTime? EndTime { get; set; }
    
    public bool IsRunning { get; set; }
    
    /// <summary>
    /// Auto-deducted pause duration in minutes (based on org pause rules)
    /// </summary>
    public int PauseDurationMinutes { get; set; } = 0;

    /// <summary>
    /// Failure entry: the timer ran past the organization's business day end and was
    /// stopped automatically (see FailureEntryService). Counts as 0 worked time until
    /// the member corrects the end time, which clears the flag.
    /// </summary>
    public bool IsFailure { get; set; }

    /// <summary>When the "timer still running shortly before business day end" reminder went out (once per entry).</summary>
    public DateTime? DayEndReminderSentAt { get; set; }
    
    public DateTime CreatedAt { get; set; }
    
    public DateTime UpdatedAt { get; set; }
    
    // Navigation properties
    public User User { get; set; } = null!;
    public Organization? Organization { get; set; }
}
