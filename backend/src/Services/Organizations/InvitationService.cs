using System.Security.Cryptography;
using Microsoft.EntityFrameworkCore;
using TimeTracking.Api.Data;
using TimeTracking.Api.Models;
using TimeTracking.Api.Models.Dtos;

namespace TimeTracking.Api.Services;

public interface IInvitationService
{
    Task<ServiceResult<InvitationResponse>> CreateAsync(string slug, int callerUserId, CreateInvitationRequest request);
    Task<ServiceResult<List<InvitationResponse>>> ListPendingAsync(string slug);
    Task<ServiceResult> RevokeAsync(string slug, int invitationId);
    Task<ServiceResult<AcceptedInvitationResponse>> AcceptAsync(int userId, string token);
}

/// <summary>
/// Joining an organization by invitation: an admin invites an email address, the person
/// accepts while signed in with that address. Role checks for create/list/revoke happen in the
/// controller via RequireOrgRole(Admin); inviting an admin additionally needs the owner.
/// </summary>
public class InvitationService : IInvitationService
{
    private static readonly TimeSpan Lifetime = TimeSpan.FromDays(7);

    private readonly TimeTrackingDbContext _context;
    private readonly IEmailSender _email;
    private readonly EmailSettings _emailSettings;
    private readonly ILogger<InvitationService> _logger;

    public InvitationService(TimeTrackingDbContext context, IEmailSender email, EmailSettings emailSettings, ILogger<InvitationService> logger)
    {
        _context = context;
        _email = email;
        _emailSettings = emailSettings;
        _logger = logger;
    }

    public async Task<ServiceResult<InvitationResponse>> CreateAsync(string slug, int callerUserId, CreateInvitationRequest request)
    {
        var org = await _context.FindOrgBySlugAsync(slug);
        if (org == null) return ServiceResult.NotFound<InvitationResponse>("Organization not found");

        if (request.Role == OrganizationRole.Owner)
            return ServiceResult.BadRequest<InvitationResponse>("Invite as Member or Admin; ownership is transferred separately.");
        var callerRole = await _context.GetOrgRoleAsync(callerUserId, org.Id);
        if (request.Role == OrganizationRole.Admin && callerRole != OrganizationRole.Owner)
            return ServiceResult.Forbidden<InvitationResponse>("Only the owner can invite admins.");

        var email = request.Email.Trim();
        var alreadyMember = await _context.UserOrganizations
            .AnyAsync(uo => uo.OrganizationId == org.Id && uo.User.Email.ToLower() == email.ToLower());
        if (alreadyMember)
            return ServiceResult.Conflict<InvitationResponse>("This person is already a member.");

        // One open invitation per address: a new one replaces the old link
        foreach (var old in _context.OrgInvitations.Where(i => i.OrganizationId == org.Id && i.Email.ToLower() == email.ToLower()
                                                              && i.AcceptedAt == null && i.RevokedAt == null))
            old.RevokedAt = DateTime.UtcNow;

        var token = Convert.ToBase64String(RandomNumberGenerator.GetBytes(32)).TrimEnd('=').Replace('+', '-').Replace('/', '_');
        var invitation = new OrgInvitation
        {
            OrganizationId = org.Id,
            Email = email,
            Role = request.Role,
            TokenHash = TokenService.HashToken(token),
            InvitedByUserId = callerUserId,
            ExpiresAt = DateTime.UtcNow.Add(Lifetime)
        };
        _context.OrgInvitations.Add(invitation);
        await _context.SaveChangesAsync();

        var inviter = await _context.Users.AsNoTracking().Where(u => u.Id == callerUserId)
            .Select(u => u.FirstName + " " + u.LastName).FirstOrDefaultAsync();
        await _email.SendAsync(email, $"Einladung zu {org.Name} / Invitation to {org.Name}",
            $"{inviter} lädt dich zur Zeiterfassung von „{org.Name}“ ein. / {inviter} invites you to {org.Name} on Time Tracking.\n\n" +
            $"{_emailSettings.FrontendBaseUrl}/invite?token={Uri.EscapeDataString(token)}\n\n" +
            "Melde dich mit dieser E-Mail-Adresse an oder registriere dich damit. Der Link gilt 7 Tage. / " +
            "Sign in or register with this email address. The link is valid for 7 days.");

        _logger.LogInformation("Invitation {InvitationId} into org {OrgId} created by user {UserId}", invitation.Id, org.Id, callerUserId);
        return ServiceResult.Ok(ToResponse(invitation));
    }

    public async Task<ServiceResult<List<InvitationResponse>>> ListPendingAsync(string slug)
    {
        var org = await _context.FindOrgBySlugAsync(slug);
        if (org == null) return ServiceResult.NotFound<List<InvitationResponse>>("Organization not found");

        var now = DateTime.UtcNow;
        var pending = await _context.OrgInvitations.AsNoTracking()
            .Where(i => i.OrganizationId == org.Id && i.AcceptedAt == null && i.RevokedAt == null && i.ExpiresAt > now)
            .OrderByDescending(i => i.CreatedAt)
            .ToListAsync();
        return ServiceResult.Ok(pending.Select(ToResponse).ToList());
    }

    public async Task<ServiceResult> RevokeAsync(string slug, int invitationId)
    {
        var invitation = await _context.OrgInvitations
            .FirstOrDefaultAsync(i => i.Id == invitationId && i.Organization.Slug == slug && i.AcceptedAt == null && i.RevokedAt == null);
        if (invitation == null) return ServiceResult.NotFound("Invitation not found");

        invitation.RevokedAt = DateTime.UtcNow;
        await _context.SaveChangesAsync();
        return ServiceResult.Ok();
    }

    public async Task<ServiceResult<AcceptedInvitationResponse>> AcceptAsync(int userId, string token)
    {
        var hash = TokenService.HashToken(token.Trim());
        var invitation = await _context.OrgInvitations
            .Include(i => i.Organization)
            .FirstOrDefaultAsync(i => i.TokenHash == hash);
        if (invitation == null || invitation.AcceptedAt != null || invitation.RevokedAt != null || invitation.ExpiresAt <= DateTime.UtcNow)
            return ServiceResult.BadRequest<AcceptedInvitationResponse>("This invitation is invalid or has expired.");

        var user = await _context.Users.FindAsync(userId);
        if (user == null || !user.Email.Equals(invitation.Email, StringComparison.OrdinalIgnoreCase))
            return ServiceResult.Forbidden<AcceptedInvitationResponse>(
                "This invitation was sent to a different email address. Sign in with that address to accept it.");

        await Memberships.ActivateAsync(_context, userId, invitation.Organization, invitation.Role);
        invitation.AcceptedAt = DateTime.UtcNow;
        // The link went to this mailbox, so the address is proven
        user.EmailConfirmed = true;
        await _context.SaveChangesAsync();

        _logger.LogInformation("Invitation {InvitationId} accepted by user {UserId}", invitation.Id, userId);
        return ServiceResult.Ok(new AcceptedInvitationResponse
        {
            OrganizationId = invitation.OrganizationId,
            OrganizationSlug = invitation.Organization.Slug,
            OrganizationName = invitation.Organization.Name,
            Role = invitation.Role.ToString()
        });
    }

    private static InvitationResponse ToResponse(OrgInvitation i) => new()
    {
        Id = i.Id,
        Email = i.Email,
        Role = i.Role.ToString(),
        CreatedAt = i.CreatedAt,
        ExpiresAt = i.ExpiresAt
    };
}
