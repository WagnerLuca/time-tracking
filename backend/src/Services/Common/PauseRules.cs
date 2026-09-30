using Microsoft.EntityFrameworkCore;
using TimeTracking.Api.Data;
using TimeTracking.Api.Models;

namespace TimeTracking.Api.Services;

public static class PauseRules
{
    /// <summary>
    /// Sets the entry's pause from the organization's automatic pause rules (the rule with the
    /// highest threshold not above the worked hours). No-op without an end time, without an
    /// organization or when auto-pause is off. Shared by stop, auto-stop on start, direct edits
    /// and approved edit requests, so all of them treat pauses the same way.
    /// </summary>
    public static async Task ApplyAsync(TimeTrackingDbContext context, TimeEntry entry)
    {
        if (entry.OrganizationId == null || !entry.EndTime.HasValue) return;

        var autoPause = await context.Organizations.AsNoTracking()
            .AnyAsync(o => o.Id == entry.OrganizationId && o.AutoPauseEnabled);
        if (!autoPause) return;

        var workHours = (entry.EndTime.Value - entry.StartTime).TotalHours;
        var rule = await context.PauseRules.AsNoTracking()
            .Where(pr => pr.OrganizationId == entry.OrganizationId && pr.MinHours <= workHours)
            .OrderByDescending(pr => pr.MinHours)
            .FirstOrDefaultAsync();

        entry.PauseDurationMinutes = rule?.PauseMinutes ?? 0;
    }
}
