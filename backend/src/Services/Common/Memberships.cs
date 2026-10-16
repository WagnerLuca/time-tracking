using Microsoft.EntityFrameworkCore;
using TimeTracking.Api.Data;
using TimeTracking.Api.Models;

namespace TimeTracking.Api.Services;

public static class Memberships
{
    /// <summary>
    /// Makes the user an active member: reactivates a previously soft-deleted membership
    /// (the unique (UserId, OrganizationId) row still exists after leaving) or adds a new one.
    /// Does not save. Returns false when the user already is an active member.
    /// </summary>
    public static async Task<bool> ActivateAsync(TimeTrackingDbContext context, int userId, Organization org, OrganizationRole role)
    {
        var existing = await context.UserOrganizations
            .IgnoreQueryFilters()
            .FirstOrDefaultAsync(uo => uo.OrganizationId == org.Id && uo.UserId == userId);

        if (existing is { IsActive: true }) return false;

        if (existing != null)
        {
            existing.IsActive = true;
            existing.Role = role;
            existing.IsAccountant = false;
            existing.JoinedAt = DateTime.UtcNow;
        }
        else
        {
            context.UserOrganizations.Add(new UserOrganization
            {
                UserId = userId,
                OrganizationId = org.Id,
                Role = role,
                IsActive = true,
                JoinedAt = DateTime.UtcNow,
                VacationDaysPerYear = org.DefaultVacationDays
            });
        }
        return true;
    }
}
