namespace TimeTracking.Api.Models;

/// <summary>
/// Invitation of an email address into an organization. Replaces adding members by internal
/// user id, which let any organization admin look up (and enroll) every account. The person
/// must accept it while signed in with that address; only the SHA-256 of the token is stored.
/// </summary>
public class OrgInvitation
{
    public int Id { get; set; }
    public int OrganizationId { get; set; }
    public required string Email { get; set; }
    public OrganizationRole Role { get; set; } = OrganizationRole.Member;
    public required string TokenHash { get; set; }
    public int InvitedByUserId { get; set; }
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
    public DateTime ExpiresAt { get; set; }
    public DateTime? AcceptedAt { get; set; }
    public DateTime? RevokedAt { get; set; }

    public Organization Organization { get; set; } = null!;
}
