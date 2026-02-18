using System.Security.Claims;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using TimeTracking.Api.Data;
using TimeTracking.Api.Models;

namespace TimeTracking.Api.Controllers;

/// <summary>
/// Base controller with shared helpers for organization-scoped controllers.
/// </summary>
public abstract class OrganizationBaseController : ControllerBase
{
    protected readonly TimeTrackingDbContext _context;

    protected OrganizationBaseController(TimeTrackingDbContext context)
    {
        _context = context;
    }

    protected int? GetCurrentUserId()
    {
        var claim = User.FindFirst(ClaimTypes.NameIdentifier);
        return claim != null && int.TryParse(claim.Value, out var id) ? id : null;
    }

    protected async Task<OrganizationRole?> GetCallerRole(int organizationId)
    {
        var userId = GetCurrentUserId();
        if (userId == null) return null;

        var membership = await _context.UserOrganizations
            .FirstOrDefaultAsync(uo => uo.OrganizationId == organizationId
                                    && uo.UserId == userId.Value
                                    && uo.IsActive);
        return membership?.Role;
    }

    protected async Task<Organization?> GetOrgBySlug(string slug)
    {
        return await _context.Organizations
            .FirstOrDefaultAsync(o => o.Slug == slug && o.IsActive);
    }
}
