using Asp.Versioning;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.RateLimiting;
using TimeTracking.Api.Filters;
using TimeTracking.Api.Models;
using TimeTracking.Api.Models.Dtos;
using TimeTracking.Api.Services;

namespace TimeTracking.Api.Controllers;

/// <summary>Betriebsurlaub: enforced vacation of the organization or a group, written as the members' vacation days.</summary>
[ApiController]
[ApiVersion("1.0")]
[Route("api/v{version:apiVersion}/organizations/{slug}/company-leaves")]
[Authorize]
[EnableRateLimiting("General")]
public class CompanyLeaveController : OrganizationBaseController
{
    private readonly CompanyLeaveService _service;

    public CompanyLeaveController(CompanyLeaveService service) => _service = service;

    /// <summary>Every Betriebsurlaub of the organization, newest first (members; exclusions for admins).</summary>
    [HttpGet]
    [RequireOrgRole]
    [ProducesResponseType(typeof(List<CompanyLeaveResponse>), StatusCodes.Status200OK)]
    public async Task<IActionResult> List(string slug) =>
        GetCurrentUserId() is int userId ? ToResponse(await _service.ListAsync(slug, userId)) : Unauthorized();

    /// <summary>Create one (admin): writes a vacation day on every member's working day in the range.</summary>
    [HttpPost]
    [RequireOrgRole(OrganizationRole.Admin)]
    [ProducesResponseType(typeof(CompanyLeaveResponse), StatusCodes.Status200OK)]
    public async Task<IActionResult> Create(string slug, [FromBody] CreateCompanyLeaveRequest request) =>
        GetCurrentUserId() is int userId ? ToResponse(await _service.CreateAsync(slug, userId, request)) : Unauthorized();

    /// <summary>Change one (admin): the generated vacation days follow.</summary>
    [HttpPut("{id}")]
    [RequireOrgRole(OrganizationRole.Admin)]
    [ProducesResponseType(typeof(CompanyLeaveResponse), StatusCodes.Status200OK)]
    public async Task<IActionResult> Update(string slug, int id, [FromBody] UpdateCompanyLeaveRequest request) =>
        GetCurrentUserId() is int userId ? ToResponse(await _service.UpdateAsync(slug, userId, id, request)) : Unauthorized();

    /// <summary>Delete one (admin) with its vacation days.</summary>
    [HttpDelete("{id}")]
    [RequireOrgRole(OrganizationRole.Admin)]
    [ProducesResponseType(StatusCodes.Status200OK)]
    public async Task<IActionResult> Delete(string slug, int id) =>
        GetCurrentUserId() is int userId ? ToResponse(await _service.DeleteAsync(slug, userId, id)) : Unauthorized();

    /// <summary>Leave members out (admin); replaces the list, their vacation days are removed or written again.</summary>
    [HttpPut("{id}/exclusions")]
    [RequireOrgRole(OrganizationRole.Admin)]
    [ProducesResponseType(typeof(CompanyLeaveResponse), StatusCodes.Status200OK)]
    public async Task<IActionResult> SetExclusions(string slug, int id, [FromBody] SetExclusionsRequest request) =>
        GetCurrentUserId() is int userId ? ToResponse(await _service.SetExclusionsAsync(slug, userId, id, request)) : Unauthorized();
}
