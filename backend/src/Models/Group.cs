using System.Text.Json.Serialization;

namespace TimeTracking.Api.Models;

public enum GroupRole
{
    Member = 0,
    Admin = 1
}

public class Group
{
    public int Id { get; set; }
    public int OrganizationId { get; set; }
    public required string Name { get; set; }
    public string? Description { get; set; }
    public string? Color { get; set; }

    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
    public DateTime UpdatedAt { get; set; } = DateTime.UtcNow;
    public bool IsActive { get; set; } = true;

    // Navigation properties
    [JsonIgnore]
    public Organization Organization { get; set; } = null!;

    [JsonIgnore]
    public ICollection<GroupMember> Members { get; set; } = new List<GroupMember>();
}

public class GroupMember
{
    public int Id { get; set; }
    public int GroupId { get; set; }
    public int UserId { get; set; }
    public GroupRole Role { get; set; } = GroupRole.Member;
    public DateTime JoinedAt { get; set; } = DateTime.UtcNow;

    // Navigation properties
    [JsonIgnore]
    public Group Group { get; set; } = null!;

    [JsonIgnore]
    public User User { get; set; } = null!;
}
