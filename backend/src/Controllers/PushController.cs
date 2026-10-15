using Asp.Versioning;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.RateLimiting;
using TimeTracking.Api.Models.Dtos;
using TimeTracking.Api.Services;

namespace TimeTracking.Api.Controllers;

/// <summary>Web Push: the VAPID public key, this device's subscription and the user's push categories.</summary>
[ApiController]
[ApiVersion("1.0")]
[Route("api/v{version:apiVersion}/[controller]")]
[Authorize]
[EnableRateLimiting("General")]
public class PushController(PushService service) : OrganizationBaseController
{
    /// <summary>The VAPID public key (null when the server has no keys configured).</summary>
    [HttpGet("public-key")]
    [ProducesResponseType(typeof(PushPublicKeyResponse), StatusCodes.Status200OK)]
    public IActionResult GetPublicKey() => ToResponse(service.GetPublicKey());

    /// <summary>Registers this device's push subscription for the current user.</summary>
    [HttpPost("subscriptions")]
    [EnableRateLimiting("AuthModerate")]
    [ProducesResponseType(StatusCodes.Status204NoContent)]
    public async Task<IActionResult> Subscribe([FromBody] PushSubscribeRequest request)
    {
        var userId = GetCurrentUserId();
        if (userId == null) return Unauthorized();
        return ToResponse(await service.SubscribeAsync(userId.Value, request, Request.Headers.UserAgent.ToString()));
    }

    /// <summary>Removes this device's push subscription.</summary>
    [HttpPost("subscriptions/remove")]
    [EnableRateLimiting("AuthModerate")]
    [ProducesResponseType(StatusCodes.Status204NoContent)]
    public async Task<IActionResult> Unsubscribe([FromBody] PushUnsubscribeRequest request)
    {
        var userId = GetCurrentUserId();
        if (userId == null) return Unauthorized();
        return ToResponse(await service.UnsubscribeAsync(userId.Value, request.Endpoint ?? ""));
    }

    /// <summary>Which push categories the current user receives.</summary>
    [HttpGet("settings")]
    [ProducesResponseType(typeof(PushSettingsDto), StatusCodes.Status200OK)]
    public async Task<IActionResult> GetSettings()
    {
        var userId = GetCurrentUserId();
        if (userId == null) return Unauthorized();
        return ToResponse(await service.GetSettingsAsync(userId.Value));
    }

    /// <summary>Switches push categories on or off (for all of the user's devices).</summary>
    [HttpPut("settings")]
    [ProducesResponseType(typeof(PushSettingsDto), StatusCodes.Status200OK)]
    public async Task<IActionResult> UpdateSettings([FromBody] PushSettingsDto settings)
    {
        var userId = GetCurrentUserId();
        if (userId == null) return Unauthorized();
        return ToResponse(await service.UpdateSettingsAsync(userId.Value, settings));
    }
}
