using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using TimeTracking.Api.Models.Dtos;
using TimeTracking.Api.Services;

namespace TimeTracking.Api.Controllers;

[ApiController]
[Route("api/organizations")]
public class PauseRulesController : OrganizationBaseController
{
    private readonly IPauseRuleService _service;

    public PauseRulesController(IPauseRuleService service)
    {
        _service = service;
    }

    [HttpGet("{slug}/pause-rules")]
    [ProducesResponseType(typeof(List<PauseRuleResponse>), StatusCodes.Status200OK)]
    public async Task<IActionResult> GetPauseRules(string slug)
    {
        return ToResponse(await _service.GetPauseRulesAsync(slug));
    }

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
