using Asp.Versioning;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.RateLimiting;
using TimeTracking.Api.Models.Dtos;
using TimeTracking.Api.Services;

namespace TimeTracking.Api.Controllers;

/// <summary>
/// Manages automatic pause rules for an organization.
/// </summary>
[ApiController]
[ApiVersion("1.0")]
[Route("api/v{version:apiVersion}/organizations")]
[EnableRateLimiting("General")]
public class PauseRulesController : OrganizationBaseController
{
    private readonly IPauseRuleService _service;
    private readonly ILogger<PauseRulesController> _logger;

    public PauseRulesController(IPauseRuleService service, ILogger<PauseRulesController> logger)
    {
        _service = service;
        _logger = logger;
    }

    /// <summary>List all pause rules for an organization (members only).</summary>
    /// <param name="slug">Organization URL slug.</param>
    [HttpGet("{slug}/pause-rules")]
    [Authorize]
    [ProducesResponseType(typeof(List<PauseRuleResponse>), StatusCodes.Status200OK)]
    public async Task<IActionResult> GetPauseRules(string slug)
    {
        var userId = GetCurrentUserId();
        if (userId == null) return Unauthorized();
        return ToResponse(await _service.GetPauseRulesAsync(slug, userId.Value));
    }

    /// <summary>Create a new pause rule (admin only).</summary>
    /// <param name="slug">Organization URL slug.</param>
    [HttpPost("{slug}/pause-rules")]
    [Authorize]
    [ProducesResponseType(typeof(PauseRuleResponse), StatusCodes.Status201Created)]
    public async Task<IActionResult> CreatePauseRule(
        string slug, [FromBody] CreatePauseRuleRequest request)
    {
        var userId = GetCurrentUserId();
        if (userId == null) return Unauthorized();
        return ToCreatedResponse(await _service.CreatePauseRuleAsync(slug, userId.Value, request));
    }

    /// <summary>Update an existing pause rule (admin only).</summary>
    /// <param name="slug">Organization URL slug.</param>
    /// <param name="ruleId">Pause rule ID.</param>
    [HttpPut("{slug}/pause-rules/{ruleId}")]
    [Authorize]
    [ProducesResponseType(typeof(PauseRuleResponse), StatusCodes.Status200OK)]
    public async Task<IActionResult> UpdatePauseRule(
        string slug, int ruleId, [FromBody] UpdatePauseRuleRequest request)
    {
        var userId = GetCurrentUserId();
        if (userId == null) return Unauthorized();
        return ToResponse(await _service.UpdatePauseRuleAsync(slug, userId.Value, ruleId, request));
    }

    /// <summary>Delete a pause rule (admin only).</summary>
    /// <param name="slug">Organization URL slug.</param>
    /// <param name="ruleId">Pause rule ID.</param>
    [HttpDelete("{slug}/pause-rules/{ruleId}")]
    [Authorize]
    [ProducesResponseType(StatusCodes.Status204NoContent)]
    public async Task<IActionResult> DeletePauseRule(string slug, int ruleId)
    {
        var userId = GetCurrentUserId();
        if (userId == null) return Unauthorized();
        return ToResponse(await _service.DeletePauseRuleAsync(slug, userId.Value, ruleId));
    }
}
