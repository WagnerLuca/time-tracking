using System.Security.Claims;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Filters;
using Microsoft.EntityFrameworkCore;
using TimeTracking.Api.Data;
using TimeTracking.Api.Models;

namespace TimeTracking.Api.Filters;

/// <summary>
/// Declarative authorization attribute for organization-scoped endpoints.
/// Reads the <c>{slug}</c> route parameter and verifies the caller has the
/// required <see cref="OrganizationRole"/> (Member ≤ Admin ≤ Owner), and — when the
/// organization requires two-factor authentication — that the caller has it enabled.
/// <para>
/// The role is looked up on every request (one indexed query) rather than cached, so a
/// demotion, removal, org deletion or slug rename takes effect immediately.
/// </para>
/// <para>
/// Usage:
/// <code>
/// [RequireOrgRole]                              // any member
/// [RequireOrgRole(OrganizationRole.Admin)]      // admin or owner
/// [RequireOrgRole(OrganizationRole.Owner)]      // owner only
/// </code>
/// </para>
/// </summary>
[AttributeUsage(AttributeTargets.Method | AttributeTargets.Class, AllowMultiple = false)]
public class RequireOrgRoleAttribute : TypeFilterAttribute
{
    public RequireOrgRoleAttribute(OrganizationRole minimumRole = OrganizationRole.Member)
        : base(typeof(OrgRoleAuthorizationFilter))
    {
        Arguments = new object[] { minimumRole };
    }
}

/// <summary>
/// Action filter that enforces organization-level role and 2FA requirements.
/// Injected via <see cref="RequireOrgRoleAttribute"/>.
/// </summary>
public class OrgRoleAuthorizationFilter : IAsyncActionFilter
{
    private readonly OrganizationRole _minimumRole;
    private readonly TimeTrackingDbContext _context;

    public OrgRoleAuthorizationFilter(OrganizationRole minimumRole, TimeTrackingDbContext context)
    {
        _minimumRole = minimumRole;
        _context = context;
    }

    public async Task OnActionExecutionAsync(ActionExecutingContext context, ActionExecutionDelegate next)
    {
        var slug = context.RouteData.Values["slug"]?.ToString();
        if (string.IsNullOrEmpty(slug))
        {
            context.Result = Problem(StatusCodes.Status400BadRequest, "Bad Request", "Organization slug is required.");
            return;
        }

        var userIdClaim = context.HttpContext.User.FindFirst(ClaimTypes.NameIdentifier)?.Value;
        if (!int.TryParse(userIdClaim, out var userId))
        {
            context.Result = new UnauthorizedResult();
            return;
        }

        var membership = await _context.UserOrganizations
            .Where(uo => uo.Organization.Slug == slug && uo.UserId == userId)
            .Select(uo => new { uo.Role, uo.Organization.Require2fa, uo.User.TwoFactorEnabled })
            .FirstOrDefaultAsync();

        if (membership == null)
        {
            context.Result = Problem(StatusCodes.Status403Forbidden, "Forbidden", "You are not a member of this organization.");
            return;
        }

        if (membership.Role < _minimumRole)
        {
            context.Result = Problem(StatusCodes.Status403Forbidden, "Forbidden", $"This action requires {_minimumRole} role or higher.");
            return;
        }

        if (membership.Require2fa && !membership.TwoFactorEnabled)
        {
            context.Result = Problem(StatusCodes.Status403Forbidden, "TwoFactorRequired",
                "This organization requires two-factor authentication. Enable it in your account settings to continue.");
            return;
        }

        await next();
    }

    private static ObjectResult Problem(int status, string title, string detail) =>
        new(new ProblemDetails { Type = $"https://httpstatuses.io/{status}", Title = title, Status = status, Detail = detail })
        { StatusCode = status };
}
