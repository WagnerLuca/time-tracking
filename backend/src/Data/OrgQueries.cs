using Microsoft.EntityFrameworkCore;
using TimeTracking.Api.Models;

namespace TimeTracking.Api.Data;

/// <summary>The lookups every organization-scoped service starts with.</summary>
public static class OrgQueries
{
    /// <summary>Active organization by slug; <paramref name="tracked"/> when the caller modifies it.</summary>
    public static Task<Organization?> FindOrgBySlugAsync(this TimeTrackingDbContext context, string slug, bool tracked = false) =>
        (tracked ? context.Organizations : context.Organizations.AsNoTracking())
            .FirstOrDefaultAsync(o => o.Slug == slug);

    /// <summary>The user's role in the organization, or null when they are not an active member.</summary>
    public static Task<OrganizationRole?> GetOrgRoleAsync(this TimeTrackingDbContext context, int userId, int organizationId) =>
        context.UserOrganizations.AsNoTracking()
            .Where(uo => uo.OrganizationId == organizationId && uo.UserId == userId)
            .Select(uo => (OrganizationRole?)uo.Role)
            .FirstOrDefaultAsync();

    /// <summary>
    /// Whether the user is an active member of the organization with the accounting role
    /// (<see cref="UserOrganization.IsAccountant"/>): sees everybody's hours whatever the org's
    /// "admins may see members' hours" setting, and may close/reopen months.
    /// </summary>
    public static Task<bool> IsAccountantAsync(this TimeTrackingDbContext context, int userId, int organizationId) =>
        context.UserOrganizations.AsNoTracking()
            .AnyAsync(uo => uo.OrganizationId == organizationId && uo.UserId == userId && uo.IsAccountant);
}
