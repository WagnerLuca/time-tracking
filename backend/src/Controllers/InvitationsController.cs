using Asp.Versioning;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.RateLimiting;
using TimeTracking.Api.Filters;
using TimeTracking.Api.Models;
using TimeTracking.Api.Models.Dtos;
using TimeTracking.Api.Services;

namespace TimeTracking.Api.Controllers;

/// <summary>Invitations into an organization (the only way to add someone else as a member).</summary>
[ApiController]
[ApiVersion("1.0")]
[Route("api/v{version:apiVersion}")]
[Authorize]
[EnableRateLimiting("General")]
public class InvitationsController : OrganizationBaseController
{
    private readonly IInvitationService _service;

    public InvitationsController(IInvitationService service) => _service = service;

    /// <summary>Invite an email address (admins; inviting admins needs the owner). Mails a link valid for 7 days.</summary>
    [HttpPost("organizations/{slug}/invitations")]
    [RequireOrgRole(OrganizationRole.Admin)]
    [EnableRateLimiting("AuthModerate")]
    [ProducesResponseType(typeof(InvitationResponse), StatusCodes.Status201Created)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status409Conflict)]
    public async Task<IActionResult> Create(string slug, [FromBody] CreateInvitationRequest request)
    {
        if (GetCurrentUserId() is not int userId) return Unauthorized();
        return ToCreatedResponse(await _service.CreateAsync(slug, userId, request));
    }

    /// <summary>Pending invitations (admins).</summary>
    [HttpGet("organizations/{slug}/invitations")]
    [RequireOrgRole(OrganizationRole.Admin)]
    [ProducesResponseType(typeof(List<InvitationResponse>), StatusCodes.Status200OK)]
    public async Task<IActionResult> List(string slug) => ToResponse(await _service.ListPendingAsync(slug));

    /// <summary>Revoke a pending invitation (admins).</summary>
    [HttpDelete("organizations/{slug}/invitations/{invitationId}")]
    [RequireOrgRole(OrganizationRole.Admin)]
    [ProducesResponseType(StatusCodes.Status204NoContent)]
    public async Task<IActionResult> Revoke(string slug, int invitationId) => ToResponse(await _service.RevokeAsync(slug, invitationId));

    /// <summary>Accept an invitation with the token from the mail; the signed-in account must have the invited address.</summary>
    [HttpPost("invitations/accept")]
    [EnableRateLimiting("AuthModerate")]
    [ProducesResponseType(typeof(AcceptedInvitationResponse), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status400BadRequest)]
    public async Task<IActionResult> Accept([FromBody] AcceptInvitationRequest request)
    {
        if (GetCurrentUserId() is not int userId) return Unauthorized();
        return ToResponse(await _service.AcceptAsync(userId, request.Token));
    }
}
