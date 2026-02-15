using System.ComponentModel.DataAnnotations;

namespace TimeTracking.Api.Models;

public class PauseRule
{
    public int Id { get; set; }
    
    [Required]
    public int OrganizationId { get; set; }
    
    /// <summary>
    /// Minimum work hours to trigger this pause rule
    /// </summary>
    public double MinHours { get; set; }
    
    /// <summary>
    /// Pause duration in minutes to deduct
    /// </summary>
    public int PauseMinutes { get; set; }
    
    public DateTime CreatedAt { get; set; }
    
    // Navigation
    public Organization Organization { get; set; } = null!;
}
