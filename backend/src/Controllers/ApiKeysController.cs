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
/// member badge IDs, and the badge punch endpoint the terminal calls.
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

    /// <summary>Revoke (delete) an API key (admins only).</summary>
    [HttpDelete("organizations/{slug}/api-keys/{keyId}")]
    [RequireOrgRole(OrganizationRole.Admin)]
    [ProducesResponseType(StatusCodes.Status204NoContent)]
    public async Task<IActionResult> DeleteApiKey(string slug, int keyId)
        => ToResponse(await _service.DeleteAsync(slug, keyId));

    /// <summary>Assign or clear (null) a member's badge ID (admins only).</summary>
    [HttpPut("organizations/{slug}/members/{memberId}/badge")]
    [RequireOrgRole(OrganizationRole.Admin)]
    [ProducesResponseType(StatusCodes.Status204NoContent)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status409Conflict)]
    public async Task<IActionResult> SetBadge(string slug, int memberId, [FromBody] SetBadgeRequest request)
        => ToResponse(await _service.SetBadgeAsync(slug, memberId, request));

    /// <summary>
    /// Terminal endpoint: authenticates with an organization API key in the <c>X-Api-Key</c>
    /// header (no user login) and toggles the badge owner's timer — stops it if running,
    /// otherwise starts a new entry in the key's organization.
    /// </summary>
    [HttpPost("terminal/punch")]
    [AllowAnonymous]
    [ProducesResponseType(typeof(PunchResponse), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status401Unauthorized)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status404NotFound)]
    public async Task<IActionResult> Punch([FromBody] PunchRequest request, [FromHeader(Name = ApiKeyHeader)] string? apiKey)
        => ToResponse(await _service.PunchAsync(apiKey, request));
}
