using Asp.Versioning;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.RateLimiting;
using TimeTracking.Api.Filters;
using TimeTracking.Api.Models;
using TimeTracking.Api.Models.Dtos;
using TimeTracking.Api.Services;

namespace TimeTracking.Api.Controllers;

/// <summary>Closed months of an organization (issue #57).</summary>
[ApiController]
[ApiVersion("1.0")]
[Route("api/v{version:apiVersion}/organizations/{slug}/closed-periods")]
[Authorize]
[EnableRateLimiting("General")]
public class ClosedPeriodsController(ClosedPeriodService service) : OrganizationBaseController
{
    /// <summary>The closed months; org admins and accountants also get the audit list.</summary>
    [HttpGet]
    [RequireOrgRole]
    [ProducesResponseType(typeof(ClosedPeriodsResponse), StatusCodes.Status200OK)]
    public async Task<IActionResult> Get(string slug) =>
        GetCurrentUserId() is int userId ? ToResponse(await service.GetAsync(slug, userId)) : Unauthorized();

    /// <summary>Close the months from..to (admins and accountants; only months that have ended).</summary>
    [HttpPost]
    [RequireOrgRole(OrganizationRole.Admin, OrAccountant = true)]
    [ProducesResponseType(typeof(ClosedPeriodsResponse), StatusCodes.Status200OK)]
    public async Task<IActionResult> Close(string slug, [FromBody] CloseMonthsRequest request) =>
        GetCurrentUserId() is int userId ? ToResponse(await service.CloseAsync(slug, userId, request)) : Unauthorized();

    /// <summary>Reopen one month (admins and accountants, with a reason).</summary>
    [HttpPost("reopen")]
    [RequireOrgRole(OrganizationRole.Admin, OrAccountant = true)]
    [ProducesResponseType(typeof(ClosedPeriodsResponse), StatusCodes.Status200OK)]
    public async Task<IActionResult> Reopen(string slug, [FromBody] ReopenMonthRequest request) =>
        GetCurrentUserId() is int userId ? ToResponse(await service.ReopenAsync(slug, userId, request)) : Unauthorized();
}
