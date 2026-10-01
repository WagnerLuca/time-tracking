using System.ComponentModel.DataAnnotations;

namespace TimeTracking.Api.Models.Dtos;

/// <summary>Invite an email address into the organization.</summary>
public record CreateInvitationRequest
{
    [Required, EmailAddress, MaxLength(256)]
    public required string Email { get; init; }

    /// <summary>Member or Admin (only owners may invite admins).</summary>
    public OrganizationRole Role { get; init; } = OrganizationRole.Member;
}

/// <summary>A pending invitation as admins see it (never contains the token).</summary>
public record InvitationResponse
{
    public int Id { get; init; }
    public required string Email { get; init; }
    public required string Role { get; init; }
    public DateTime CreatedAt { get; init; }
    public DateTime ExpiresAt { get; init; }
}

/// <summary>Accept an invitation with the token from the mail.</summary>
public record AcceptInvitationRequest
{
    [Required]
    public required string Token { get; init; }
}

/// <summary>The organization the caller just joined.</summary>
public record AcceptedInvitationResponse
{
    public int OrganizationId { get; init; }
    public required string OrganizationSlug { get; init; }
    public required string OrganizationName { get; init; }
    public required string Role { get; init; }
}
