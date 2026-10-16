using Microsoft.EntityFrameworkCore;
using TimeTracking.Api.Data;
using TimeTracking.Api.Models;
using TimeTracking.Api.Models.Dtos;

namespace TimeTracking.Api.Services;

/// <summary>Closing and reopening months (issue #57; org admins and accountants); the write guard itself is <see cref="ClosedPeriods"/>.</summary>
public class ClosedPeriodService(TimeTrackingDbContext context)
{
    /// <summary>Audit rows returned (newest first).</summary>
    private const int EventLimit = 200;

    public async Task<ServiceResult<ClosedPeriodsResponse>> GetAsync(string slug, int callerUserId)
    {
        var org = await context.FindOrgBySlugAsync(slug);
        if (org == null) return ServiceResult.NotFound<ClosedPeriodsResponse>("Organization not found");
        var seesLog = await context.GetOrgRoleAsync(callerUserId, org.Id) >= OrganizationRole.Admin
            || await context.IsAccountantAsync(callerUserId, org.Id);
        return ServiceResult.Ok(await StateAsync(org.Id, seesLog));
    }

    /// <summary>Closes every month from..to that has ended (org-local) and is still open.</summary>
    public async Task<ServiceResult<ClosedPeriodsResponse>> CloseAsync(string slug, int callerUserId, CloseMonthsRequest request)
    {
        var org = await context.FindOrgBySlugAsync(slug);
        if (org == null) return ServiceResult.NotFound<ClosedPeriodsResponse>("Organization not found");

        var (from, to) = (ClosedPeriods.MonthOf(request.From), ClosedPeriods.MonthOf(request.To));
        if (to < from) return ServiceResult.BadRequest<ClosedPeriodsResponse>("The end date must not be before the start date.");
        if (from.Year < 2000) return ServiceResult.BadRequest<ClosedPeriodsResponse>("Dates must be in the year 2000 or later.");
        if (from.AddMonths(ClosedPeriods.MaxMonthsPerClose) <= to)
            return ServiceResult.BadRequest<ClosedPeriodsResponse>("Close at most 24 months at once.", "period.tooManyMonths");
        // A month can be closed once its last day is over in the organization's time zone
        if (to.AddMonths(1) > OrgClock.For(org).Today())
            return ServiceResult.BadRequest<ClosedPeriodsResponse>("Only months that have ended can be closed.", "period.notEnded");

        var now = DateTime.UtcNow;
        var reason = string.IsNullOrWhiteSpace(request.Reason) ? null : request.Reason.Trim();
        for (var attempt = 1; ; attempt++)
        {
            var already = (await ClosedPeriods.MonthsAsync(context, org.Id)).ToHashSet();
            for (var month = from; month <= to; month = month.AddMonths(1))
            {
                if (already.Contains(month)) continue;
                context.ClosedPeriods.Add(new ClosedPeriod { OrganizationId = org.Id, Month = month, ClosedAt = now, ClosedByUserId = callerUserId });
                context.ClosedPeriodEvents.Add(new ClosedPeriodEvent
                {
                    OrganizationId = org.Id, Month = month, Kind = ClosedPeriodEventKind.Closed, UserId = callerUserId, At = now, Reason = reason
                });
            }
            try
            {
                await context.SaveChangesAsync();
                break;
            }
            catch (DbUpdateException ex) when (DbErrors.IsUniqueViolation(ex) && attempt < 3)
            {
                // Another admin closed one of the months at the same time, failing the whole save: close the rest
                context.ChangeTracker.Clear();
            }
        }
        return ServiceResult.Ok(await StateAsync(org.Id, isAdmin: true));
    }

    public async Task<ServiceResult<ClosedPeriodsResponse>> ReopenAsync(string slug, int callerUserId, ReopenMonthRequest request)
    {
        var org = await context.FindOrgBySlugAsync(slug);
        if (org == null) return ServiceResult.NotFound<ClosedPeriodsResponse>("Organization not found");
        if (string.IsNullOrWhiteSpace(request.Reason))
            return ServiceResult.BadRequest<ClosedPeriodsResponse>("Give a reason for reopening the month.", "period.reasonRequired");

        var month = ClosedPeriods.MonthOf(request.Month);
        var closed = await context.ClosedPeriods.FirstOrDefaultAsync(p => p.OrganizationId == org.Id && p.Month == month);
        if (closed == null) return ServiceResult.NotFound<ClosedPeriodsResponse>("This month is not closed.", "period.notClosed");

        context.ClosedPeriods.Remove(closed);
        context.ClosedPeriodEvents.Add(new ClosedPeriodEvent
        {
            OrganizationId = org.Id, Month = month, Kind = ClosedPeriodEventKind.Reopened, UserId = callerUserId,
            At = DateTime.UtcNow, Reason = request.Reason.Trim()
        });
        await context.SaveChangesAsync();
        return ServiceResult.Ok(await StateAsync(org.Id, isAdmin: true));
    }

    private async Task<ClosedPeriodsResponse> StateAsync(int orgId, bool isAdmin) => new()
    {
        Months = await ClosedPeriods.MonthsAsync(context, orgId),
        Events = !isAdmin ? [] : await context.ClosedPeriodEvents.AsNoTracking()
            .Where(e => e.OrganizationId == orgId)
            .OrderByDescending(e => e.At).ThenByDescending(e => e.Id)
            .Take(EventLimit)
            .Select(e => new ClosedPeriodEventResponse
            {
                Id = e.Id,
                Month = e.Month,
                Kind = e.Kind.ToString(),
                At = e.At,
                UserName = e.User == null ? null : e.User.FirstName + " " + e.User.LastName,
                Reason = e.Reason,
                MemberName = e.MemberUser == null ? null : e.MemberUser.FirstName + " " + e.MemberUser.LastName,
                Summary = e.Summary
            })
            .ToListAsync()
    };
}
