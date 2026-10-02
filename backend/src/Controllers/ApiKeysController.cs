using Asp.Versioning;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.RateLimiting;
using TimeTracking.Api.Filters;
using TimeTracking.Api.Models;
using TimeTracking.Api.Models.Dtos;
using TimeTracking.Api.Services;

namespace TimeTracking.Api.Controllers;

/// <summary>
/// Organization API keys for terminals (e.g. an RFID scanner at the entrance),
/// members' cards, the punch protocol, and the punch endpoint the terminal calls.
/// </summary>
[ApiController]
[ApiVersion("1.0")]
[Route("api/v{version:apiVersion}")]
[Authorize]
[EnableRateLimiting("General")]
public class ApiKeysController : OrganizationBaseController
{
    /// <summary>Header a terminal sends its key in.</summary>
    public const string ApiKeyHeader = "X-Api-Key";

    private readonly IApiKeyService _service;

    public ApiKeysController(IApiKeyService service)
    {
        _service = service;
    }

    /// <summary>List the organization's API keys (admins only). Secrets are never returned.</summary>
    [HttpGet("organizations/{slug}/api-keys")]
    [RequireOrgRole(OrganizationRole.Admin)]
    [ProducesResponseType(typeof(List<ApiKeyResponse>), StatusCodes.Status200OK)]
    public async Task<IActionResult> GetApiKeys(string slug)
        => ToResponse(await _service.ListAsync(slug));

    /// <summary>Create an API key (admins only). The plaintext key is returned once, in this response only.</summary>
    [HttpPost("organizations/{slug}/api-keys")]
    [RequireOrgRole(OrganizationRole.Admin)]
    [ProducesResponseType(typeof(CreatedApiKeyResponse), StatusCodes.Status201Created)]
    public async Task<IActionResult> CreateApiKey(string slug, [FromBody] CreateApiKeyRequest request)
    {
        if (GetCurrentUserId() is not int userId) return Unauthorized();
        return ToCreatedResponse(await _service.CreateAsync(slug, userId, request));
    }

    /// <summary>Rename an API key (admins only); the name is the terminal name in the punch protocol and on entries.</summary>
    [HttpPut("organizations/{slug}/api-keys/{keyId}")]
    [RequireOrgRole(OrganizationRole.Admin)]
    [ProducesResponseType(typeof(ApiKeyResponse), StatusCodes.Status200OK)]
    public async Task<IActionResult> RenameApiKey(string slug, int keyId, [FromBody] UpdateApiKeyRequest request)
        => ToResponse(await _service.RenameAsync(slug, keyId, request));

    /// <summary>Revoke (delete) an API key (admins only). Its punch protocol rows stay.</summary>
    [HttpDelete("organizations/{slug}/api-keys/{keyId}")]
    [RequireOrgRole(OrganizationRole.Admin)]
    [ProducesResponseType(StatusCodes.Status204NoContent)]
    public async Task<IActionResult> DeleteApiKey(string slug, int keyId)
        => ToResponse(await _service.DeleteAsync(slug, keyId));

    /// <summary>A member's terminal cards (admins only); only the last characters of each card value.</summary>
    [HttpGet("organizations/{slug}/members/{memberId}/cards")]
    [RequireOrgRole(OrganizationRole.Admin)]
    [ProducesResponseType(typeof(List<BadgeCardResponse>), StatusCodes.Status200OK)]
    public async Task<IActionResult> GetCards(string slug, int memberId)
        => ToResponse(await _service.ListCardsAsync(slug, memberId));

    /// <summary>Add a card to a member (admins only): typed in (<c>cardId</c>) or adopted from an unknown scan (<c>fromPunchId</c>).</summary>
    [HttpPost("organizations/{slug}/members/{memberId}/cards")]
    [RequireOrgRole(OrganizationRole.Admin)]
    [ProducesResponseType(typeof(BadgeCardResponse), StatusCodes.Status201Created)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status409Conflict)]
    public async Task<IActionResult> AddCard(string slug, int memberId, [FromBody] AddBadgeCardRequest request)
        => ToCreatedResponse(await _service.AddCardAsync(slug, memberId, request));

    /// <summary>Change a card's label or block/unblock it (admins only).</summary>
    [HttpPut("organizations/{slug}/members/{memberId}/cards/{cardId}")]
    [RequireOrgRole(OrganizationRole.Admin)]
    [ProducesResponseType(typeof(BadgeCardResponse), StatusCodes.Status200OK)]
    public async Task<IActionResult> UpdateCard(string slug, int memberId, int cardId, [FromBody] UpdateBadgeCardRequest request)
        => ToResponse(await _service.UpdateCardAsync(slug, memberId, cardId, request));

    /// <summary>Remove a card (admins only).</summary>
    [HttpDelete("organizations/{slug}/members/{memberId}/cards/{cardId}")]
    [RequireOrgRole(OrganizationRole.Admin)]
    [ProducesResponseType(StatusCodes.Status204NoContent)]
    public async Task<IActionResult> DeleteCard(string slug, int memberId, int cardId)
        => ToResponse(await _service.DeleteCardAsync(slug, memberId, cardId));

    /// <summary>Cards scanned in the last 7 days that belong to nobody, grouped per card, newest first (admins only).</summary>
    [HttpGet("organizations/{slug}/terminal/unknown-cards")]
    [RequireOrgRole(OrganizationRole.Admin)]
    [ProducesResponseType(typeof(List<UnknownCardResponse>), StatusCodes.Status200OK)]
    public async Task<IActionResult> GetUnknownCards(string slug)
        => ToResponse(await _service.ListUnknownCardsAsync(slug));

    /// <summary>Punch protocol, newest first (admins only). <paramref name="from"/>/<paramref name="to"/> are UTC instants on the scan time; limit 1-1000 (default 200).</summary>
    [HttpGet("organizations/{slug}/terminal/punches")]
    [RequireOrgRole(OrganizationRole.Admin)]
    [ProducesResponseType(typeof(List<TerminalPunchResponse>), StatusCodes.Status200OK)]
    public async Task<IActionResult> GetPunches(string slug, [FromQuery] DateTime? from, [FromQuery] DateTime? to, [FromQuery] int? limit)
        => ToResponse(await _service.ListPunchesAsync(slug, from, to, limit));

    /// <summary>
    /// Terminal endpoint: authenticates with an organization API key in the <c>X-Api-Key</c>
    /// header (no user login) and toggles the card owner's timer: stops it if running,
    /// otherwise starts a new entry in the key's organization. See docs/TERMINAL.md for the
    /// double-scan, offline queue and idempotency rules.
    /// </summary>
    [HttpPost("terminal/punch")]
    [AllowAnonymous]
    [EnableRateLimiting("Terminal")]
    [ProducesResponseType(typeof(PunchResponse), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status400BadRequest)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status401Unauthorized)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status403Forbidden)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status404NotFound)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status409Conflict)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status429TooManyRequests)]
    public async Task<IActionResult> Punch([FromBody] PunchRequest request, [FromHeader(Name = ApiKeyHeader)] string? apiKey)
        => ToResponse(await _service.PunchAsync(apiKey, request));
}
