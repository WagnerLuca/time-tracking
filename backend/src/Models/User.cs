using System.Text.Json.Serialization;

namespace TimeTracking.Api.Models;

public class User
{
    public int Id { get; set; }
    public required string Email { get; set; }
    
    [JsonIgnore]
    public string? PasswordHash { get; set; }  // Nullable for OAuth-only users
    
    public required string FirstName { get; set; }
    public required string LastName { get; set; }
    public bool EmailConfirmed { get; set; } = false;
    
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingDefault)]
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
    
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingDefault)]
    public DateTime UpdatedAt { get; set; }
    
    public bool IsActive { get; set; } = true;
    
    // OAuth fields
    public string? GitHubId { get; set; }
    public string? GitHubUsername { get; set; }
    public string? ProfileImageUrl { get; set; }
    
    // Security fields
    [JsonIgnore]
    public string? RefreshToken { get; set; }
    
    [JsonIgnore]
    public DateTime? RefreshTokenExpiryTime { get; set; }
    
    public DateTime? LastLoginAt { get; set; }
    
    // Navigation properties
    [JsonIgnore]
    public ICollection<UserOrganization> UserOrganizations { get; set; } = new List<UserOrganization>();
}
