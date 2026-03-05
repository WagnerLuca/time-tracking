using System.Security.Claims;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Filters;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Caching.Memory;
using TimeTracking.Api.Data;
using TimeTracking.Api.Models;

namespace TimeTracking.Api.Filters;

/// <summary>
/// Declarative authorization attribute for organization-scoped endpoints.
/// Reads the <c>{slug}</c> route parameter and verifies the caller has the
/// required <see cref="OrganizationRole"/> (Member ≤ Admin ≤ Owner).
/// <para>
/// Results are cached in-memory for <see cref="OrgRoleAuthorizationFilter.CacheDuration"/>
/// to avoid hitting the database on every request.
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
/// Action filter that enforces organization-level role requirements.
/// Injected via <see cref="RequireOrgRoleAttribute"/>.
/// Caches role lookups in-memory for <see cref="CacheDuration"/>.
/// </summary>
public class OrgRoleAuthorizationFilter : IAsyncActionFilter
{
    /// <summary>How long role lookups are cached. Short TTL balances performance vs. freshness.</summary>
    internal static readonly TimeSpan CacheDuration = TimeSpan.FromMinutes(2);

    private readonly OrganizationRole _minimumRole;
    private readonly TimeTrackingDbContext _context;
    private readonly IMemoryCache _cache;

    public OrgRoleAuthorizationFilter(OrganizationRole minimumRole, TimeTrackingDbContext context, IMemoryCache cache)
    {
        _minimumRole = minimumRole;
        _context = context;
        _cache = cache;
    }

    public async Task OnActionExecutionAsync(ActionExecutingContext context, ActionExecutionDelegate next)
    {
        // Extract org slug from route
        var slug = context.RouteData.Values["slug"]?.ToString();
        if (string.IsNullOrEmpty(slug))
        {
            context.Result = new ObjectResult(new ProblemDetails
            {
                Type = "https://httpstatuses.io/400",
                Title = "Bad Request",
                Status = StatusCodes.Status400BadRequest,
                Detail = "Organization slug is required."
            })
            { StatusCode = StatusCodes.Status400BadRequest };
            return;
        }

        // Extract userId from JWT claims
        var userIdClaim = context.HttpContext.User.FindFirst(ClaimTypes.NameIdentifier)?.Value;
        if (!int.TryParse(userIdClaim, out var userId))
        {
            context.Result = new UnauthorizedResult();
            return;
        }

        // Check the user's role in this organization (cached)
        var cacheKey = $"org_role:{slug}:{userId}";
        var role = await _cache.GetOrCreateAsync(cacheKey, async entry =>
        {
            entry.AbsoluteExpirationRelativeToNow = CacheDuration;
            return await _context.UserOrganizations
                .Where(uo => uo.Organization.Slug == slug && uo.UserId == userId)
                .Select(uo => (OrganizationRole?)uo.Role)
                .FirstOrDefaultAsync();
        });

        if (role == null)
        {
            context.Result = new ObjectResult(new ProblemDetails
            {
                Type = "https://httpstatuses.io/403",
                Title = "Forbidden",
                Status = StatusCodes.Status403Forbidden,
                Detail = "You are not a member of this organization."
            })
            { StatusCode = StatusCodes.Status403Forbidden };
            return;
        }

        if (role < _minimumRole)
        {
            context.Result = new ObjectResult(new ProblemDetails
            {
                Type = "https://httpstatuses.io/403",
                Title = "Forbidden",
                Status = StatusCodes.Status403Forbidden,
                Detail = $"This action requires {_minimumRole} role or higher."
            })
            { StatusCode = StatusCodes.Status403Forbidden };
            return;
        }

        await next();
    }
}
